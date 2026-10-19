namespace BossMod;

// utility that recalculates ai hints based on different data sources (eg active bossmodule, etc)
// when there is no active bossmodule (eg in outdoor or on trash), we try to guess things based on world state (eg actor casts)
public sealed class AIHintsBuilder : IDisposable
{
    private const float RaidwideSize = 30f;
    private const float HalfWidth = 0.5f;
    public readonly Pathfinding.ObstacleMapManager Obstacles;
    private readonly WorldState _ws;
    private readonly BossModuleManager _bmm;
    private readonly ZoneModuleManager _zmm;
    private readonly EventSubscriptions _subscriptions;
    private readonly RotationSolverRebornModule? _rsr;
    private readonly Dictionary<ulong, (Actor Caster, Actor? Target, AOEShape Shape, bool IsCharge)> _activeAOEs = [];
    private readonly Dictionary<ulong, (Actor Caster, Actor? Target, AOEShape Shape)> _activeGazes = [];
    private readonly List<Actor> _invincible = [];
    private ArenaBoundsCircle? _activeFateBounds;
    private ArenaBoundsRect? _obstacleViewBounds;
    private bool isRSRpaused;

    private static readonly uint[] invincibleStatuses =
    [
        151u, 198u, 325u, 328u, 385u, 394u,
        469u, 529u, 592u, 656u, 671u, 775u,
        776u, 895u, 969u, 981u, 1240u, 1302u,
        1303u, 1567u, 1570u, 1697u, 1829u, 1936u,
        2413u, 2654u, 3012u, 3039u, 3052u, 3054u,
        4410u, 4175u
    ];
    private static readonly HashSet<uint> ignore = [27503u, 33626u]; // action IDs that the AI should ignore
    private static readonly PartyRolesConfig _config = Service.Config.Get<PartyRolesConfig>();
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<uint, (byte, byte, byte, uint, string, string, string, int, bool, uint)> _spellCache = new(); // 模組元件在背景解析重播的執行緒上也會讀寫

    public AIHintsBuilder(WorldState ws, BossModuleManager bmm, ZoneModuleManager zmm, RotationSolverRebornModule? rsr)
    {
        _ws = ws;
        _bmm = bmm;
        _zmm = zmm;
        _rsr = rsr;
        Obstacles = new(ws);
        _subscriptions = new
        (
            ws.Actors.CastStarted.Subscribe(OnCastStarted),
            ws.Actors.CastFinished.Subscribe(OnCastFinished),
            ws.Actors.StatusGain.Subscribe(OnStatusGain),
            ws.Actors.StatusLose.Subscribe(OnStatusLose),
            ws.Client.ActiveFateChanged.Subscribe(_ => _activeFateBounds = null)
        );
    }

    public void Dispose()
    {
        _subscriptions.Dispose();
        Obstacles.Dispose();
    }

    // 唯讀存取口（雷達顯示用）：CalculateAutoHints 這一幀用的同一份讀條猜測。只在主執行緒讀，不要跨幀保存列舉結果。
    public Dictionary<ulong, (Actor Caster, Actor? Target, AOEShape Shape, bool IsCharge)>.ValueCollection GuessedAOEs => _activeAOEs.Values;
    public Dictionary<ulong, (Actor Caster, Actor? Target, AOEShape Shape)>.ValueCollection GuessedGazes => _activeGazes.Values;

    public void Update(AIHints hints, int playerSlot, bool moveImminent)
    {
        var player = _ws.Party[playerSlot];

        hints.Clear();
        if (moveImminent || player?.PendingKnockbacks.Count > 0)
            hints.MaxCastTime = 0;
        if (player != null)
        {
            var playerAssignment = _config[_ws.Party.Members[playerSlot].ContentId];
            var activeModule = _bmm.ActiveModule?.StateMachine.ActivePhase != null ? _bmm.ActiveModule : null;
            var outOfCombatPriority = activeModule?.ShouldPrioritizeAllEnemies == true ? 0 : AIHints.Enemy.PriorityUndesirable;
            FillEnemies(hints, playerAssignment == PartyRolesConfig.Assignment.MT || playerAssignment == PartyRolesConfig.Assignment.OT && !OtherTankInParty(player), outOfCombatPriority);
            if (activeModule != null)
            {
                activeModule.CalculateAIHints(playerSlot, ref player, ref playerAssignment, ref hints);
                // SimpleBossModule 換回 DefaultBounds（跟著玩家走的方形），那不是場地
                hints.PathfindMapBoundsIsArena = !ReferenceEquals(hints.PathfindMapBounds, AIHints.DefaultBounds);
            }
            else
            {
                CalculateAutoHints(hints, player);
                _zmm.ActiveModule?.CalculateAIHints(playerSlot, player, hints);
            }
        }
        hints.Normalize();
        if (_rsr != null)
        {
            var soon = _ws.CurrentTime.AddSeconds(0.5);
            var hasForbiddenDirection = hints.ForbiddenDirections.Count > 0;

            if (!isRSRpaused && (hasForbiddenDirection && hints.ForbiddenDirections[0].activation < soon || hints.ImminentSpecialMode.mode == AIHints.SpecialMode.Pyretic && hints.ImminentSpecialMode.activation < soon) && _rsr.IsInstalled)
            {
                _rsr.PauseRSR();
                isRSRpaused = true;
                if (hints.ImminentSpecialMode.mode == AIHints.SpecialMode.Pyretic)
                {
                    hints.ForceCancelCast = true;
                }
            }
            else if (isRSRpaused && (!hasForbiddenDirection || hints.ForbiddenDirections[0].activation > soon) && (hints.ImminentSpecialMode.mode != AIHints.SpecialMode.Pyretic || hints.ImminentSpecialMode.activation > soon) && _rsr.IsInstalled)
            {
                _rsr.UnPauseRSR();
                isRSRpaused = false;
            }
        }
    }

    // 隊伍與聯盟（不含 NPC、不含死者）裡有沒有玩家以外的坦克。獨立成方法：寫成 lambda 的話，閉包物件會在 Update 一進來就配置（每幀一個）。
    private bool OtherTankInParty(Actor player)
    {
        foreach (var p in _ws.Party.WithoutSlot(false, false, true))
            if (p != player && p.Role == Role.Tank)
                return true;
        return false;
    }

    // Fill list of potential targets from world state
    private void FillEnemies(AIHints hints, bool playerIsDefaultTank, int priorityPassive = AIHints.Enemy.PriorityUndesirable)
    {
        var allowedFateID = Utils.IsPlayerSyncedToFate(_ws) ? _ws.Client.ActiveFate.ID : default;

        foreach (var actor in _ws.Actors.Actors.Values)
        {
            if (!actor.IsTargetable || actor.IsAlly || actor.IsDead)
                continue;
            var index = actor.CharacterSpawnIndex;
            if (index < 0 || index >= hints.Enemies.Length)
                continue;

            int priority;
            if (actor.FateID != 0)
            {
                if (actor.FateID != allowedFateID)
                    priority = AIHints.Enemy.PriorityInvincible;  // fate mob in fate we are NOT a part of can't be damaged at all
                else
                    priority = 0; // Relevant fate mob
            }
            else if (actor.PendingDead)
                priority = AIHints.Enemy.PriorityPointless; // Mob is about to die
            else if (actor.AggroPlayer)
                priority = 0; // Aggroed player
            else if (actor.InCombat && _ws.Party.FindSlot(actor.TargetID) >= 0)
                priority = 0; // Assisting party members
            else
                priority = priorityPassive; // Default undesirable

            var enemy = hints.Enemies[index] = new(actor, priority, playerIsDefaultTank);
            hints.PotentialTargets.Add(enemy);
        }
    }

    private void CalculateAutoHints(AIHints hints, Actor player)
    {
        var inFate = Utils.IsPlayerSyncedToFate(_ws);
        var center = inFate ? _ws.Client.ActiveFate.Center : player.PosRot.XYZ();
        var (e, bitmap) = Obstacles.Find(center);
        var resolution = bitmap?.PixelSize ?? 0.5f;
        if (inFate)
        {
            hints.PathfindMapCenter = new(_ws.Client.ActiveFate.Center.XZ());
            hints.PathfindMapBounds = (_activeFateBounds ??= new ArenaBoundsCircle(_ws.Client.ActiveFate.Radius, resolution, true));
            hints.PathfindMapBoundsIsArena = true;
            if (e != null && bitmap != null)
            {
                var originCell = (hints.PathfindMapCenter - e.Origin) / resolution;
                var originX = (int)originCell.X;
                var originZ = (int)originCell.Z;
                var halfSize = (int)(_ws.Client.ActiveFate.Radius / resolution);
                hints.PathfindMapObstacles = new(bitmap, new(originX - halfSize, originZ - halfSize, originX + halfSize, originZ + halfSize));
            }
        }
        else if (e != null && bitmap != null)
        {
            var originCell = (player.Position - e.Origin) / resolution;
            var originX = (int)originCell.X;
            var originZ = (int)originCell.Z;
            // if player is too close to the border, adjust origin
            originX = Math.Min(originX, bitmap.Width - e.ViewWidth);
            originZ = Math.Min(originZ, bitmap.Height - e.ViewHeight);
            originX = Math.Max(originX, e.ViewWidth);
            originZ = Math.Max(originZ, e.ViewHeight);
            // TODO: consider quantizing even more, to reduce jittering when player moves?..
            hints.PathfindMapCenter = e.Origin + resolution * new WDir(originX, originZ);
            hints.PathfindMapBounds = GetObstacleViewBounds(e.ViewWidth * resolution, e.ViewHeight * resolution, resolution);
            hints.PathfindMapObstacles = new(bitmap, new(originX - e.ViewWidth, originZ - e.ViewHeight, originX + e.ViewWidth, originZ + e.ViewHeight));
        }
        else
        {
            hints.PathfindMapCenter = player.Position.Rounded(5);
            // try to keep player near grid center
            var playerOffset = player.Position - hints.PathfindMapCenter;
            var playerOffetX = playerOffset.X;
            var playerOffsetZ = playerOffset.Z;
            var pathfindMapCenterX = hints.PathfindMapCenter.X;
            var pathfindMapCenterZ = hints.PathfindMapCenter.Z;
            if (playerOffetX < -1.25f)
                pathfindMapCenterX -= 2.5f;
            else if (playerOffetX > 1.25f)
                pathfindMapCenterX += 2.5f;
            if (playerOffsetZ < -1.25f)
                pathfindMapCenterZ -= 2.5f;
            else if (playerOffsetZ > 1.25f)
                pathfindMapCenterZ += 2.5f;
            hints.PathfindMapCenter = new(pathfindMapCenterX, pathfindMapCenterZ);
            // keep default bounds
        }

        foreach (var aoe in _activeAOEs.Values)
        {
            var caster = aoe.Caster.CastInfo!;
            var target = caster.LocXZ;
            var rot = caster.Rotation;
            var finishAt = _ws.FutureTime(caster.NPCRemainingTime);
            if (aoe.IsCharge)
                hints.AddForbiddenZone(ShapeDistance.Rect(aoe.Caster.Position.Quantized(), target, ((AOEShapeRect)aoe.Shape).HalfWidth), finishAt, aoe.Caster.InstanceID);
            else
                hints.AddForbiddenZone(aoe.Shape, target, rot, finishAt);
        }

        foreach (var gaze in _activeGazes.Values)
        {
            var target = gaze.Target?.Position ?? gaze.Caster.CastInfo!.LocXZ;
            var rot = gaze.Caster.CastInfo!.Rotation;
            var finishAt = _ws.FutureTime(gaze.Caster.CastInfo.NPCRemainingTime);
            if (gaze.Shape.Check(player.Position, target, rot))
                hints.ForbiddenDirections.Add((Angle.FromDirection(target - player.Position), 45.Degrees(), finishAt));
        }

        var count = _invincible.Count;
        for (var i = 0; i < count; ++i)
        {
            hints.SetPriority(_invincible[i], AIHints.Enemy.PriorityInvincible);
        }
    }

    // 尋路底圖快取在邊界實例上，每幀 new 會讓它永不命中、每幀重建整張圖；輸入不變就沿用同一個實例。
    // 只從 Update 呼叫（單一執行緒）。跨執行緒共用實例與 AIHints.DefaultBounds 相同：底圖惰性建立，併發時頂多多建一次、內容相同。
    private ArenaBoundsRect GetObstacleViewBounds(float halfWidth, float halfHeight, float resolution)
    {
        var bounds = _obstacleViewBounds;
        if (bounds == null || bounds.HalfWidth != halfWidth || bounds.HalfHeight != halfHeight || bounds.MapResolution != resolution)
            _obstacleViewBounds = bounds = new ArenaBoundsRect(halfWidth, halfHeight, MapResolution: resolution);
        return bounds;
    }

    private void OnCastStarted(Actor actor)
    {
        if (_bmm.ActiveModule?.StateMachine.ActivePhase != null) // no need to do all of this if it won't be used anyway
            return;
        if (actor.Type is not ActorType.Enemy and not ActorType.Helper || actor.IsAlly)
            return;
        var castInfo = actor.CastInfo!;
        if (GuessCastShape(actor, castInfo, out var isCharge, out var isGaze) is not AOEShape shape)
            return;
        var target = _ws.Actors.Find(castInfo.TargetID);
        if (isGaze)
            _activeGazes[actor.InstanceID] = (actor, target, shape);
        else
            _activeAOEs[actor.InstanceID] = (actor, target, shape, isCharge);
    }

    // 沒有模組時怎麼從讀條猜形狀；模組裡的 Components.UnclaimedCastAOEs 共用這一支。回 null＝這個讀條不猜。
    // 呼叫端自己先篩施放者（敵方 Enemy／Helper）。isCharge＝矩形從施放者拉到落點；isGaze＝背對類，形狀只用來判在不在範圍內。
    public static AOEShape? GuessCastShape(Actor actor, ActorCastInfo castInfo, out bool isCharge, out bool isGaze)
    {
        isCharge = isGaze = false;
        var actionID = castInfo.Action.ID;
        if (ignore.Contains(actionID))
            return null;

        var data = GetSpellData(actionID);

        // gaze
        if (data.VFX == 25)
        {
            isGaze = true;
            return GuessShape(ref data, ref actor);
        }

        if (!castInfo.IsSpell() || data.CastType == 1)
            return null;
        if (data.CastType is 2 or 5 && data.EffectRange >= RaidwideSize)
            return null;
        if (GuessShape(ref data, ref actor) is not AOEShape shape)
        {
            Service.Log($"[AutoHints] Unknown cast type {data.CastType} for {castInfo.Action}");
            return null;
        }
        isCharge = data.CastType == 8;
        return shape;
    }

    private void OnCastFinished(Actor actor)
    {
        _activeAOEs.Remove(actor.InstanceID);
        _activeGazes.Remove(actor.InstanceID);
    }

    private void OnStatusGain(Actor actor, int index)
    {
        var statusID = actor.Statuses[index].ID;
        for (var i = 0; i < 32; ++i)
        {
            if (statusID == invincibleStatuses[i])
            {
                _invincible.Add(actor);
                return;
            }
        }
    }

    private void OnStatusLose(Actor actor, int index)
    {
        var statusID = actor.Statuses[index].ID;
        for (var i = 0; i < 32; ++i)
        {
            if (statusID == invincibleStatuses[i])
            {
                _invincible.Remove(actor);
                return;
            }
        }
    }

    private static Angle DetermineConeAngle(ref (byte, byte, byte, uint RowId, string Name, string PathAlly, string Path, int Pos, bool Omen, uint) data)
    {
        if (!data.Omen)
        {
            Service.Log($"[AutoHints] No omen data for {data.RowId} '{data.Name}'...");
            return 180f.Degrees();
        }
        var path = data.Path;
        var pos = data.Pos;
        if (pos < 0 || pos + 6 > path.Length || !int.TryParse(path.AsSpan(pos + 3, 3), out var angle))
        {
            if (TryParseTwoDigitFan(path, pos, out angle))
                return angle.Degrees();
            Service.Log($"[AutoHints] Can't determine angle from omen ({path}/{data.PathAlly}) for {data.RowId} '{data.Name}'...");
            return 180.Degrees();
        }
        return angle.Degrees();
    }

    // Omen 路徑的 fanNN（兩位數、後面是底線或字串結尾、值 >= 10）與三位數 fanNNN 同義：扇形全角（度）。
    // 刻意不採用其他形狀（fan01x、fan02de 是編號不是角度；fan28m60、fan90r 語意未證實）：猜窄了 AI 會站進範圍裡。
    private static bool TryParseTwoDigitFan(string path, int pos, out int angle)
    {
        angle = 0;
        if (pos < 0 || pos + 5 > path.Length || !char.IsAsciiDigit(path[pos + 3]) || !char.IsAsciiDigit(path[pos + 4]))
            return false;
        if (pos + 5 < path.Length && path[pos + 5] != '_')
            return false;
        angle = (path[pos + 3] - '0') * 10 + (path[pos + 4] - '0');
        return angle >= 10;
    }

    private static AOEShape? GuessShape(ref (byte CastType, byte EffectRange, byte XAxisModifier, uint, string, string, string, int, bool, uint) data, ref Actor actor) => data.CastType switch
    {
        2 => new AOEShapeCircle(data.EffectRange), // used for some point-blank aoes and enemy location-targeted - does not add caster hitbox
        3 => new AOEShapeCone(data.EffectRange + actor.HitboxRadius, DetermineConeAngle(ref data) * HalfWidth),
        4 => new AOEShapeRect(data.EffectRange + actor.HitboxRadius, data.XAxisModifier * HalfWidth),
        5 => new AOEShapeCircle(data.EffectRange + actor.HitboxRadius),
        //6 => custom shapes
        //7 => new AOEShapeCircle(data.EffectRange), - used for player ground-targeted circles a-la asylum
        8 => new AOEShapeRect(default, data.XAxisModifier * HalfWidth), // charges
        10 => new AOEShapeDonut(3, data.EffectRange),
        11 => new AOEShapeCross(data.EffectRange, data.XAxisModifier * HalfWidth),
        12 => new AOEShapeRect(data.EffectRange, data.XAxisModifier * HalfWidth),
        13 => new AOEShapeCone(data.EffectRange, DetermineConeAngle(ref data) * HalfWidth),
        _ => null
    };

    private static (byte CastType, byte EffectRange, byte XAxisModifier, uint RowId, string Name, string PathAlly, string path, int pos, bool Omen, uint VFX) GetSpellData(uint actionID)
    {
        if (_spellCache.TryGetValue(actionID, out var actionRow))
            return actionRow;
        var row = Service.LuminaRow<Lumina.Excel.Sheets.Action>(actionID);
        (byte, byte, byte, uint, string, string, string, int, bool, uint)? data;
        var omenPath = row!.Value.Omen.Value.Path.ToString();
        data = (row.Value.CastType, row.Value.EffectRange, row.Value.XAxisModifier, row.Value.RowId, row.Value.Name.ToString(), row.Value.Omen.Value.PathAlly.ToString(), omenPath, omenPath.IndexOf("fan", StringComparison.Ordinal), row.Value.Omen.ValueNullable != null, row.Value.VFX.RowId);
        return _spellCache[actionID] = data!.Value;
    }

    // 沒有模組時會被降成「打了沒用」的無敵類狀態；Components.UnclaimedCastAOEs 共用同一張表
    public static bool IsInvincibleStatus(uint statusID) => Array.IndexOf(invincibleStatuses, statusID) >= 0;
}
