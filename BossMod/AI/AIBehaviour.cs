using BossMod.Autorotation;
using BossMod.Pathfinding;
using System.Threading;

namespace BossMod.AI;

public record struct Targeting(AIHints.Enemy Target, float PreferredRange = 2.6f, Positional PreferredPosition = Positional.Any, bool PreferTanking = false);

// constantly follow master
sealed class AIBehaviour(AIController ctrl, RotationModuleManager autorot, Preset? aiPreset) : IDisposable
{
    public WorldState WorldState => autorot.Bossmods.WorldState;
    public float ForceMovementIn = float.MaxValue; // TODO: reconsider
    public Preset? AIPreset = aiPreset;
    private static readonly AIConfig _config = Service.Config.Get<AIConfig>();
    private readonly NavigationDecision.Context _naviCtx = new();
    private NavigationDecision _naviDecision;
    private bool _afkMode;
    private bool _followMaster; // if true, our navigation target is master rather than primary target - this happens e.g. in outdoor or in dungeons during gathering trash
    private WPos _masterPrevPos;
    private WPos _masterMovementStart;
    private DateTime _masterLastMoved;
    private DateTime _navStartTime; // if current time is < this, navigation won't start
    private WPos? _preDodgeAnchor; // position to try returning to once a forced dodge is over, if ReturnToPreDodgePosition is enabled
    private bool _wasForcedDodging;
    private DateTime _preDodgeAnchorExpiry;
    private static readonly SemaphoreSlim _semaphore = new(1, 1);
    private static readonly Random random = new();
    private bool cancel; // used to cancel autorotation AI preset during async

    #region 深牢專用 preset 的執行期覆蓋

    private static readonly Global.DeepDungeon.AutoDDConfig _ddConfig = Service.Config.Get<Global.DeepDungeon.AutoDDConfig>();

    private Preset? _ddPreset;
    private string? _ddPresetResolvedFor;
    private bool _ddPresetMissingLogged;

    /// <summary>
    /// 深牢裡要改用的 preset；null＝沒設定，或設定的名字在 preset 庫裡找不到（照常用原本的）。
    /// </summary>
    /// <remarks>
    /// 🔴 這是<b>執行期覆蓋</b>，刻意不去寫 <c>AIConfig.AIAutorotPresetName</c>：
    /// 寫設定會製造設定檔 churn，而且遊戲或外掛在深牢裡崩潰時會把使用者永久卡在切換後的狀態。
    /// 覆蓋只活在記憶體裡，離開深牢或重載外掛就自然消失。
    /// <para>
    /// ⚠️ <b>必須快取</b>：<c>PresetDatabase.AllPresets</c> 是屬性，每次存取都會
    /// <c>[.. DefaultPresets, .. UserPresets]</c> 配一個新的 List，而這裡是每幀都會走到的路徑。
    /// </para>
    /// <para>
    /// ⚠️ 快取只在<b>名字改變</b>時失效。preset 物件是不可變的（資料庫註解明講
    /// "presets in the database are immutable"，編輯會產生新物件），所以「改了 preset 內容但沒改名」
    /// 時這裡仍會拿著舊物件，直到名字變動或重新載入。為了不在每幀配置記憶體，這個取捨是刻意的。
    /// </para>
    /// </remarks>
    private Preset? ResolveDeepDungeonPreset()
    {
        var name = _ddConfig.DeepDungeonPreset;
        if (string.IsNullOrEmpty(name))
            return null;

        if (_ddPresetResolvedFor == name)
            return _ddPreset;

        _ddPresetResolvedFor = name;
        _ddPreset = null;

        var presets = autorot.Database.Presets.AllPresets;
        var count = presets.Count;
        for (var i = 0; i < count; ++i)
        {
            if (presets[i].Name == name)
            {
                _ddPreset = presets[i];
                _ddPresetMissingLogged = false;
                Service.Logger.Information($"[DD] 深牢期間改用循環預設「{name}」。");
                return _ddPreset;
            }
        }

        // 找不到＝設定裡的名字被改名或刪掉了。不是錯誤，維持原本的 preset。
        if (!_ddPresetMissingLogged)
        {
            _ddPresetMissingLogged = true;
            Service.Logger.Information($"[DD] 設定指定的深牢循環預設「{name}」不存在，維持原本的預設、不切換。");
        }
        return null;
    }

    #endregion

    public void Dispose()
    {
        cancel = true;
    }

    public async Task Execute(Actor player, Actor master)
    {
        if (await _semaphore.WaitAsync(0).ConfigureAwait(false))
        {
            try
            {
                ForceMovementIn = float.MaxValue;
                if (player.IsDead)
                    return;

                // keep master in focus
                if (_config.FocusTargetMaster)
                    FocusMaster(master);

                _afkMode = _config.AutoAFK && !master.InCombat && (WorldState.CurrentTime - _masterLastMoved).TotalSeconds > _config.AFKModeTimer;
                var gazeImminent = autorot.Hints.ForbiddenDirections.Count != 0 && autorot.Hints.ForbiddenDirections[0].activation <= WorldState.FutureTime(0.5d);
                var pyreticImminent = autorot.Hints.ImminentSpecialMode.mode == AIHints.SpecialMode.Pyretic && autorot.Hints.ImminentSpecialMode.activation <= WorldState.FutureTime(1d);
                var misdirectionMode = autorot.Hints.ImminentSpecialMode.mode == AIHints.SpecialMode.Misdirection && autorot.Hints.ImminentSpecialMode.activation <= WorldState.CurrentTime;
                var forbidTargeting = _config.ForbidActions || _afkMode || gazeImminent || pyreticImminent;
                var hadNavi = _naviDecision.Destination != null;

                Targeting target = default;
                if (!forbidTargeting && AIPreset != null && (!_config.ForbidAIMovementMounted || _config.ForbidAIMovementMounted && player.MountId == 0))
                {
                    target = SelectPrimaryTarget(player, master);
                    if (_config.ManualTarget)
                    {
                        var t = autorot.WorldState.Actors.Find(player.TargetID);
                        if (t != null)
                            target.Target = new AIHints.Enemy(t, 100, false);
                        else
                            target = default;
                    }
                    if (target.Target != null || TargetIsForbidden(player.TargetID))
                        autorot.Hints.ForcedTarget ??= target.Target?.Actor;
                    AdjustTargetPositional(player, ref target);
                }

                var followTarget = _config.FollowTarget;
                _followMaster = master != player;

                // note: if there are pending knockbacks, don't update navigation decision to avoid fucking up positioning
                if (player.PendingKnockbacks.Count == 0)
                {
                    var actorTarget = autorot.WorldState.Actors.Find(player.TargetID);
                    var naviDecision = followTarget && actorTarget != null
                        ? await BuildNavigationDecision(player, actorTarget, target).ConfigureAwait(false)
                        : await BuildNavigationDecision(player, master, target).ConfigureAwait(false);
                    _naviDecision = naviDecision;

                    // there is a difference between having a small positive leeway and having a negative one for pathfinding, prefer to keep positive
                    _naviDecision.LeewaySeconds = Math.Max(0, _naviDecision.LeewaySeconds - 0.1f);
                }

                var masterIsMoving = TrackMasterMovement(master);
                var moveWithMaster = masterIsMoving && _followMaster && master != player;
                ForceMovementIn = moveWithMaster || gazeImminent || pyreticImminent ? 0f : _naviDecision.LeewaySeconds;

                TrackPreDodgeAnchor(player, moveWithMaster || gazeImminent || pyreticImminent);

                if (_config.MoveDelay != 0d && !hadNavi && _naviDecision.Destination != null)
                    _navStartTime = WorldState.FutureTime(_config.MoveDelay);

                if (!forbidTargeting && !cancel)
                {
                    // 🔑 只掛預設，不影響走位/閃避/選目標——那些在這個判斷之外。
                    //    深牢判定用 DeepDungeon.DungeonId：它直接來自遊戲的深牢 instance content director
                    //    （不在深牢時是 None），不需要另外維護一份 territory 清單。
                    var inDeepDungeon = autorot.WorldState.DeepDungeon.DungeonId != DeepDungeonState.DungeonType.None;
                    var autorotAllowed = !_config.AutorotOnlyInDeepDungeon || inDeepDungeon;
                    // 深牢裡改用指定的 preset；沒設定或找不到就退回原本的（不是錯誤）。
                    // 📌 與「只在深牢出招」的互動：深牢外 autorotAllowed 為 false ⇒ Preset 直接是 null，
                    //    這裡選了哪個 preset 都不影響，兩個開關同時開啟時語意是一致的。
                    var preset = inDeepDungeon ? ResolveDeepDungeonPreset() ?? AIPreset : AIPreset;
                    autorot.Preset = target.Target != null && autorotAllowed ? preset : null;
                }
                UpdateMovement(player, master, target, gazeImminent || pyreticImminent, misdirectionMode ? autorot.Hints.MisdirectionThreshold : default, !forbidTargeting ? autorot.Hints.ActionsToExecute : null);
            }
            finally
            {
                _semaphore.Release();
            }
        }
    }

    // returns null if we're to be idle, otherwise target to attack
    private Targeting SelectPrimaryTarget(Actor player, Actor master)
    {
        // we prefer not to switch targets unnecessarily, so start with current target - it could've been selected manually or by AI on previous frames
        // if current target is not among valid targets, clear it - this opens way for future target selection heuristics
        var targetId = autorot.Hints.ForcedTarget?.InstanceID ?? player.TargetID;
        var target = autorot.Hints.PriorityTargets.FirstOrDefault(e => e.Actor.InstanceID == targetId);

        // if we don't have a valid target yet, use some heuristics to select some 'ok' target to attack
        // try assisting master, otherwise (if player is own master, or if master has no valid target) just select closest valid target
        target ??= master != player ? autorot.Hints.PriorityTargets.FirstOrDefault(t => master.TargetID == t.Actor.InstanceID) : null;
        target ??= autorot.Hints.PriorityTargets.MinBy(e => (e.Actor.Position - player.Position).LengthSq());

        // if the previous line returned no target, there aren't any priority targets at all - give up
        if (target == null)
            return default;

        // TODO: rethink all this... ai module should set forced target if it wants to switch... figure out positioning and stuff
        // now give class module a chance to improve targeting
        // typically it would switch targets for multidotting, or to hit more targets with AOE
        // in case of ties, it should prefer to return original target - this would prevent useless switches
        var targeting = new Targeting(target!, player.Role is Role.Melee or Role.Tank ? 2.6f : 24.5f);

        var pos = autorot.Hints.RecommendedPositional;
        if (pos.Target != null && targeting.Target.Actor == pos.Target)
            targeting.PreferredPosition = pos.Pos;

        return /*autorot.SelectTargetForAI(targeting) ??*/ targeting;
    }

    private void AdjustTargetPositional(Actor player, ref Targeting targeting)
    {
        if (targeting.Target == null || targeting.PreferredPosition == Positional.Any)
            return; // nothing to adjust

        if (targeting.PreferredPosition == Positional.Front)
        {
            // 'front' is tank-specific positional; no point doing anything if we're not tanking target
            if (targeting.Target.Actor.TargetID != player.InstanceID)
                targeting.PreferredPosition = Positional.Any;
            return;
        }

        // if target-of-target is player, don't try flanking, it's probably impossible... - unless target is currently casting (TODO: reconsider?)
        // skip if targeting a dummy, they don't rotate
        if (targeting.Target.Actor.TargetID == player.InstanceID && targeting.Target.Actor.CastInfo == null && targeting.Target.Actor.NameID != 541)
            targeting.PreferredPosition = Positional.Any;
    }

    // remembers the position player was standing at right before a forced dodge, so we can try to walk back to it once the dodge is over
    private void TrackPreDodgeAnchor(Actor player, bool forcedByExternalFactor)
    {
        if (!_config.ReturnToPreDodgePosition)
        {
            _preDodgeAnchor = null;
            _wasForcedDodging = false;
            return;
        }

        var forcedDodging = !forcedByExternalFactor && ForceMovementIn <= 0f && _naviDecision.Destination != null;
        if (forcedDodging && !_wasForcedDodging)
            _preDodgeAnchor = player.Position; // just started dodging - remember where we came from
        _wasForcedDodging = forcedDodging;

        if (_preDodgeAnchor == null)
            return;

        if (!forcedDodging && (player.Position - _preDodgeAnchor.Value).LengthSq() < 1f)
            _preDodgeAnchor = null; // arrived back, done
        else if (!forcedDodging && !IsAnchorSafe(_preDodgeAnchor.Value))
            _preDodgeAnchor = null; // spot became dangerous or is out of bounds - give up
        else if (_preDodgeAnchor != null && WorldState.CurrentTime > _preDodgeAnchorExpiry)
            _preDodgeAnchor = null; // took too long, give up and let normal positioning take over
        if (forcedDodging)
            _preDodgeAnchorExpiry = WorldState.FutureTime(_config.ReturnToPreDodgePositionTimeout);
    }

    // true for Gold Saucer minigames, where standing further into a safe zone than strictly necessary costs nothing meaningful (as opposed to dungeons/trials/raids, where positioning still needs to stay precise)
    private bool IsCasualContent() => autorot.Bossmods.ActiveModule?.Info?.GroupType == BossModuleInfo.GroupType.GoldSaucer;

    // true if pos is inside the arena bounds and not inside any (even future) forbidden zone
    private bool IsAnchorSafe(WPos pos)
    {
        if (!autorot.Hints.PathfindMapBounds.Contains(pos - autorot.Hints.PathfindMapCenter))
            return false;
        foreach (var z in autorot.Hints.ForbiddenZones)
            if (z.shapeDistance(pos) < 0f)
                return false;
        return true;
    }

    private async Task<NavigationDecision> BuildNavigationDecision(Actor player, Actor master, Targeting targeting)
    {
        if (_config.ForbidMovement || _config.ForbidAIMovementMounted && player.MountId != 0
            || autorot.Hints.ImminentSpecialMode.mode == AIHints.SpecialMode.NoMovement && autorot.Hints.ImminentSpecialMode.activation <= WorldState.FutureTime(1d))
            return new() { LeewaySeconds = float.MaxValue };

        if (autorot.Hints.ImminentSpecialMode.mode == AIHints.SpecialMode.Freezing && autorot.Hints.ImminentSpecialMode.activation <= WorldState.FutureTime(2.1d))
        {
            var randomO1 = random.NextSingle() * 2f - 1f;
            var randomO2 = random.NextSingle() * 2f - 1f;
            autorot.Hints.ForcedMovement = new WPos(player.Position.X * randomO1, player.Position.Z * randomO2).ToVec3();
            return new() { LeewaySeconds = float.MaxValue };
        }

        Actor? forceDestination = null;
        var interactTarget = autorot.Hints.InteractWithTarget;
        if (interactTarget != null)
            forceDestination = interactTarget;
        else if (_followMaster)
        {
            forceDestination = master;
        }

        // 🔴 `master != player` 這個條件不能漏。初次判定（Execute 裡的 `_followMaster = master != player`）
        //    有它，但這裡重算時原本沒有 —— 於是 solo 時（master 就是自己）只要在戰鬥中移動超過 10y，
        //    這裡就會把 _followMaster 重新設成 true，接著下面的分支拿「master」當跟隨目標，
        //    等於在自己腳下種一個權重 1.0 的目標點（GoalSingleTarget(master, ...)）。
        //    它會壓過所有小權重的走位提示（例如風箏的 0.05），表現成「AI 一直想站回原地」而不報錯。
        _followMaster = master != player && interactTarget == null && (_config.FollowDuringCombat || !master.InCombat || (_masterPrevPos - _masterMovementStart).LengthSq() > 100f) && (_config.FollowDuringActiveBossModule || autorot.Bossmods.ActiveModule?.StateMachine.ActiveState == null) && (_config.FollowOutOfCombat || master.InCombat);

        var forbiddenZoneCushion = _config.PreferredDistance + (IsCasualContent() ? _config.CasualSafetyMargin : 0f);

        // while actually dodging something, prefer moving behind the target over its front/flanks, and avoid simply backing away from it;
        // this is only a tie-breaker among otherwise equally-safe cells, so it never overrides actual AOE safety
        // 🔑 有模組正在風箏時不要加「別後退」的懲罰：那一項每遠離 1y 約 −0.5，
        //    而風箏的目標區只有 0.05，兩者放在同一個權重場裡風箏必然被碾平，
        //    而且是靜默的（使用者只看到「開了風箏但角色不退」）。
        //    只拿掉這個偏好項，「躲到目標背後」與所有實際閃避判定都不受影響。
        if (autorot.Hints.ForbiddenZones.Count != 0 && targeting.Target != null)
            autorot.Hints.GoalZones.Add(autorot.Hints.GoalDodgeDirection(targeting.Target.Actor, player.Position, penalizeRetreat: !autorot.Hints.WantKiting));

        if (_followMaster)
        {
            if (forceDestination != null && forceDestination.OID != master.OID && autorot.Hints.PathfindMapBounds.Contains(forceDestination.Position - autorot.Hints.PathfindMapCenter))
            {
                autorot.Hints.GoalZones.Add(autorot.Hints.GoalProximity(forceDestination, 3.5f, 100f));
            }
            var target = autorot.WorldState.Actors.Find(player.TargetID);
            var masterInBounds = !_config.StayWithinArenaBounds || autorot.Hints.PathfindMapBounds.Contains(master.Position - autorot.Hints.PathfindMapCenter);
            if (!_config.FollowTarget || _config.FollowTarget && target == null)
            {
                if (masterInBounds)
                    autorot.Hints.GoalZones.Add(autorot.Hints.GoalSingleTarget(master, Positional.Any, _config.FollowTarget && player.InCombat ? _config.MaxDistanceToTarget : _config.MaxDistanceToSlot));
            }
            else if (_config.FollowTarget && target != null && AIPreset == null)
            {
                var positional = _config.DesiredPositional;
                var mindist = _config.MinDistance;
                var maxdist = _config.MaxDistanceToTarget;
                if (positional is Positional.Rear or Positional.Flank && (target.CastInfo == null && target.NameID != 541u && target.TargetID == player.InstanceID || target.Omnidirectional)) // if player is target, rear/flank is usually impossible unless target is casting
                    positional = Positional.Any;
                if (masterInBounds)
                    autorot.Hints.GoalZones.Add(autorot.Hints.GoalSingleTarget(master, positional, positional != Positional.Any ? 2.6f : maxdist));

                if (mindist != default && target.InstanceID != player.InstanceID && interactTarget == null)
                {
                    var hitboxradius = target.HitboxRadius;
                    var maxAdj = hitboxradius + maxdist;
                    var min = hitboxradius + mindist;
                    var max = maxAdj > min ? maxAdj : min + 1f;
                    autorot.Hints.GoalZones.Add(autorot.Hints.GoalDonut(target.Position, min, max, 2f));
                }
            }
            if (_preDodgeAnchor != null && !_wasForcedDodging)
                autorot.Hints.GoalZones.Add(autorot.Hints.GoalProximity(_preDodgeAnchor.Value, 3f, 1.5f));
            return await Task.Run(() => NavigationDecision.Build(_naviCtx, WorldState, autorot.Hints, player, autorot.Bossmods.WorldState.Client.MoveSpeed, forbiddenZoneCushion: forbiddenZoneCushion, avoidFutureAOEs: _config.AvoidFutureAOEs, activationTimeCushion: _config.ActivationTimeCushion)).ConfigureAwait(false);
        }

        // TODO: remove this once all rotation modules are fixed
        if (autorot.Hints.GoalZones.Count == 0 && targeting.Target != null)
            autorot.Hints.GoalZones.Add(autorot.Hints.GoalSingleTarget(targeting.Target.Actor, targeting.PreferredPosition, targeting.PreferredRange));
        if (_preDodgeAnchor != null && !_wasForcedDodging)
            autorot.Hints.GoalZones.Add(autorot.Hints.GoalProximity(_preDodgeAnchor.Value, 3f, 1.5f));
        return await Task.Run(() => NavigationDecision.Build(_naviCtx, WorldState, autorot.Hints, player, autorot.Bossmods.WorldState.Client.MoveSpeed, forbiddenZoneCushion, avoidFutureAOEs: _config.AvoidFutureAOEs, activationTimeCushion: _config.ActivationTimeCushion)).ConfigureAwait(false);
    }

    private void FocusMaster(Actor master)
    {
        var masterChanged = Service.TargetManager.FocusTarget?.EntityId != master.InstanceID;
        if (masterChanged)
        {
            ctrl.SetFocusTarget(master);
            _masterPrevPos = _masterMovementStart = master.Position;
            _masterLastMoved = WorldState.CurrentTime.AddSeconds(-1d);
        }
    }

    private bool TrackMasterMovement(Actor master)
    {
        // keep track of master movement
        // idea is that if master is moving forward (e.g. running in outdoor or pulling trashpacks in dungeon), we want to closely follow and not stop to cast
        var masterIsMoving = true;
        if (master.Position != _masterPrevPos)
        {
            _masterLastMoved = WorldState.CurrentTime;
            _masterPrevPos = master.Position;
        }
        else if ((WorldState.CurrentTime - _masterLastMoved).TotalSeconds > 0.5d)
        {
            // master has stopped, consider previous movement finished
            _masterMovementStart = _masterPrevPos;
            masterIsMoving = false;
        }
        // else: don't consider master to have stopped moving unless he's standing still for some small time

        return masterIsMoving;
    }

    private void UpdateMovement(Actor player, Actor master, Targeting target, bool gazeOrPyreticImminent, Angle misdirectionAngle, ActionQueue? queueForSprint)
    {
        // 🔴 讓路：預設集裡的「自動移動」模組正在負責移動時，這裡完全不做自己的移動決策。
        //    兩邊各自算目的地、逐幀交替接管，使用者看到的就是角色抖動（見 NormalMovement.OwnsMovement）。
        //    ⚠️ 只讓出「移動」這一項：目標選擇、InteractWithTarget、凝視／Pyretic 的中斷詠唱、
        //    衝刺推送全部照舊，所以這裡不是提早 return，而是只把 NaviTargetPos 壓成 null。
        //    讓出後由 NormalMovement 單獨寫 Hints.ForcedMovement ⇒ 移動只有一個擁有者。
        var yieldMovement = Autorotation.MiscAI.NormalMovement.OwnsMovement;

        if (gazeOrPyreticImminent)
        {
            // gaze or pyretic imminent, drop any movement - we should have moved to safe zone already...
            ctrl.NaviTargetPos = null;
            ctrl.NaviTargetVertical = null;
            ctrl.ForceCancelCast = true;
        }
        else if (misdirectionAngle != default && _naviDecision.Destination is WPos destination)
        {
            ctrl.AllowInterruptingCastByMovement = true;
            var dir = destination - player.Position;
            var distSq = dir.LengthSq();
            var threshold = 45f.Degrees();
            var forceddir = WorldState.Client.ForcedMovementDirection;
            var allowMovement = forceddir.AlmostEqual(Angle.FromDirection(dir), threshold.Rad);
            if (allowMovement)
                allowMovement = CalculateUnobstructedPathLength(forceddir) >= Math.Min(3f, distSq);
            ctrl.NaviTargetPos = !yieldMovement && allowMovement && distSq >= 0.01f ? destination : null;

            float CalculateUnobstructedPathLength(Angle dir)
            {
                var start = _naviCtx.Map.WorldToGrid(player.Position);
                var startx = start.x;
                var starty = start.y;
                if (!_naviCtx.Map.InBounds(startx, starty))
                    return 0f;

                var end = _naviCtx.Map.WorldToGrid(player.Position + 100f * dir.ToDirection());
                var startG = _naviCtx.Map.PixelMaxG[_naviCtx.Map.GridToIndex(startx, starty)];
                var pixels = _naviCtx.Map.EnumeratePixelsInLine(startx, starty, end.x, end.y);
                var len = pixels.Length;
                for (var i = 0; i < len; ++i)
                {
                    ref readonly var p = ref pixels[i];
                    var px = p.x;
                    var py = p.y;
                    if (!_naviCtx.Map.InBounds(px, py) || _naviCtx.Map.PixelMaxG[_naviCtx.Map.GridToIndex(px, py)] < startG)
                    {
                        var dest = _naviCtx.Map.GridToWorld(px, py, 0.5f, 0.5f);
                        return (dest - player.Position).LengthSq();
                    }
                }
                return float.MaxValue;
            }

            // debug
            //void drawLine(WPos from, WPos to, uint color) => Camera.Instance!.DrawWorldLine(new(from.X, player.PosRot.Y, from.Z), new(to.X, player.PosRot.Y, to.Z), color);
            //var toDest = _naviDecision.Destination.Value - player.Position;
            //drawLine(player.Position, _naviDecision.Destination.Value, Colors.Safe);
            //drawLine(_naviDecision.Destination.Value, _naviDecision.Destination.Value + toDest.Normalized().OrthoL(), Colors.Safe);
            //drawLine(player.Position, ctrl.NaviTargetPos.Value, Colors.Danger);
        }
        else
        {
            var toDest = _naviDecision.Destination != null ? _naviDecision.Destination.Value - player.Position : default;
            var distSq = toDest.LengthSq();

            // avoid relocating the instant a marginally "safer" spot appears when there's no real urgency yet (see MovementUrgencyThreshold);
            // still move immediately if that's actually needed to stay in range of whatever we're following
            var mustMoveNow = _config.MovementUrgencyThreshold <= 0f || ForceMovementIn <= _config.MovementUrgencyThreshold;
            if (!mustMoveNow)
            {
                var followActor = target.Target?.Actor ?? master;
                var maxRange = target.Target != null ? _config.MaxDistanceToTarget : _config.MaxDistanceToSlot;
                mustMoveNow = followActor != player && (followActor.Position - player.Position).LengthSq() > maxRange * maxRange;
            }

            ctrl.NaviTargetPos = !yieldMovement && WorldState.CurrentTime >= _navStartTime && mustMoveNow ? _naviDecision.Destination : null;
            ctrl.NaviTargetVertical = master != player ? master.PosRot.Y : null;
            ctrl.AllowInterruptingCastByMovement = player.CastInfo != null && _naviDecision.LeewaySeconds <= player.CastInfo.RemainingTime - 0.5d;
            ctrl.ForceCancelCast = false;

            //var cameraFacing = _ctrl.CameraFacing;
            //var dot = cameraFacing.Dot(_ctrl.TargetRot.Value);
            //if (dot < -0.707107f)
            //    _ctrl.TargetRot = -_ctrl.TargetRot.Value;
            //else if (dot < 0.707107f)
            //    _ctrl.TargetRot = cameraFacing.OrthoL().Dot(_ctrl.TargetRot.Value) > 0 ? _ctrl.TargetRot.Value.OrthoR() : _ctrl.TargetRot.Value.OrthoL();

            // sprint, if not in combat and far enough away from destination
            if (player.InCombat ? _naviDecision.LeewaySeconds <= 0f && distSq > 25f : player != master && distSq > 400f)
            {
                queueForSprint?.Push(ActionDefinitions.IDSprint, player, ActionQueue.Priority.Minimal + 100f);
            }
        }
    }

    private bool TargetIsForbidden(ulong actorId) => autorot.Hints.ForbiddenTargets.Any(e => e.Actor.InstanceID == actorId);
}
