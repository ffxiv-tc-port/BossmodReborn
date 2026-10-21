namespace BossMod.Global.GoldSaucer.SliceIsRight;

public enum OID : uint
{
    Boss = 0x25AB, //R=1.8
    Daigoro = 0x25AC, //R=2.5
    Bamboo = 0x25AD, //R=0.5
    Helper = 0x1EAEDF,
    VisualOutsideArena = 0x2BC8,
    HelperCupPhase1 = 0x1EAEB7,
    HelperCupPhase2 = 0x1EAEB6,
    HelperCupPhase3 = 0x1EAE9D,
    HelperSingleRect = 0x1EAE99,
    HelperDoubleRect = 0x1EAE9A,
    HelperCircle = 0x1EAE9B,
    Pileofgold = 0x1EAE9C
}

public enum AID : uint
{
    YojimboVisual1 = 19070, // Boss->self, no cast, single-target
    YojimboVisual2 = 18331, // Boss->self, no cast, single-tat
    YojimboVisual3 = 18329, // Boss->self, no cast, single-target
    YojimboVisual4 = 18332, // Boss->self, no cast, single-target
    YojimboVisual5 = 18328, // Boss->location, no cast, single-target
    YojimboVisual6 = 18326, // Boss->self, 3.0s cast, single-target
    YojimboVisual7 = 18339, // Boss->self, 3.0s cast, single-target
    YojimboVisual8 = 18340, // Boss->self, no cast, single-target
    YojimboVisual9 = 19026, // Boss->self, no cast, single-target
    VisualOutsideArena = 18338, // VisualOutsideArena->self, no cast, single-target

    BambooSplit = 18333, // Bamboo->self, 0.7s cast, range 28 width 5 rect
    BambooCircleFall = 18334, // Bamboo->self, 0.7s cast, range 11 circle 
    BambooSpawn = 18327, // Bamboo->self, no cast, range 3 circle
    FirstGilJump = 18335, // Daigoro->location, 2.5s cast, width 7 rect charge
    NextGilJump = 18336, // Daigoro->location, 1.5s cast, width 7 rect charge
    BadCup = 18337 // Daigoro->self, 1.0s cast, range 15+R 120-degree cone
}

/// <summary>本模組專用：危險區出現後多久才交給 AI 閃避。純計算、不讀遊戲狀態。</summary>
/// <remarks>
/// 規則：交給 AI 的時刻＝min(出現＋等待, 生效－最壞情況要提早的秒數)，且不早於出現。
/// 提早秒數＝最壞逃離距離÷名目跑速＋AI 的生效緩衝（至少 1 秒）＋反應與延遲 1 秒；預告太短時等待自動縮短到這條線。
/// </remarks>
public static class SliceDodgeTiming
{
    public const float MaxDelaySeconds = 5f;
    public const float ReactionSeconds = 1f;

    public static double RequiredLeadSeconds(float escapeDistance, float activationCushion)
        => escapeDistance / Pathfinding.NavigationDecision.NominalPlayerSpeed
        + Math.Max(activationCushion, Pathfinding.NavigationDecision.ActivationTimeCushion) + ReactionSeconds;

    public static DateTime RevealAt(DateTime appear, DateTime activation, float delaySeconds, double requiredLeadSeconds)
    {
        if (activation == default)
            return appear;
        var delay = float.IsFinite(delaySeconds) ? Math.Clamp(delaySeconds, 0f, MaxDelaySeconds) : 0f;
        var wanted = appear.AddSeconds(delay);
        var latest = activation.AddSeconds(-requiredLeadSeconds);
        var reveal = wanted < latest ? wanted : latest;
        return reveal < appear ? appear : reveal;
    }
}

sealed class BambooSplits(BossModule module) : Components.GenericAOEs(module)
{
    private readonly List<AOEInstance> _aoes = new(8);
    private readonly List<DateTime> _revealAt = new(8); // 與 _aoes 同索引、同步增刪
    private readonly TheSliceIsRightConfig _config = Service.Config.Get<TheSliceIsRightConfig>();
    private readonly AI.AIConfig _aiConfig = Service.Config.Get<AI.AIConfig>();
    private static readonly AOEShapeRect rect = new(28f, 2.5f);
    private static readonly AOEShapeCircle circle = new(11f);
    private static readonly AOEShapeCircle bamboospawn = new(3f);
    // 最壞情況要跑多遠才出得去（含半格尋路格）；圓靠舞台邊緣時得沿邊繞出去，比半徑長約 1 碼。
    private const float RectEscape = 3f, CircleEscape = 12.5f, SpawnEscape = 3.5f;

    public override ReadOnlySpan<AOEInstance> ActiveAOEs(int slot, Actor actor)
    {
        var aoes = CollectionsMarshal.AsSpan(_aoes);
        var now = WorldState.CurrentTime;
        for (var i = 0; i < aoes.Length; ++i)
            aoes[i].Risky = now >= _revealAt[i];
        return aoes;
    }

    private void AddAOE(AOEShape shape, WPos origin, Angle rotation, double seconds, float escape)
    {
        var activation = WorldState.FutureTime(seconds);
        _aoes.Add(new(shape, origin, rotation, activation));
        _revealAt.Add(SliceDodgeTiming.RevealAt(WorldState.CurrentTime, activation, _config.DodgeDelaySeconds, SliceDodgeTiming.RequiredLeadSeconds(escape, _aiConfig.ActivationTimeCushion)));
    }

    public override void OnActorCreated(Actor actor)
    {
        if (actor.OID is (uint)OID.HelperCircle or (uint)OID.HelperDoubleRect or (uint)OID.HelperSingleRect)
        {
            AddAOE(bamboospawn, actor.Position.Quantized(), default, 2.7d, SpawnEscape);
        }
    }

    public override void OnActorEAnim(Actor actor, uint state)
    {
        if (state == 0x00010002)
        {
            var pos = actor.Position.Quantized();
            switch (actor.OID)
            {
                case (uint)OID.HelperCircle:
                    AddAOE(circle, pos, default, 7d, CircleEscape);
                    break;
                case (uint)OID.HelperSingleRect:
                    AddAOE(rect, pos, actor.Rotation + 90.Degrees(), 7d, RectEscape);
                    break;
                case (uint)OID.HelperDoubleRect:
                    AddAOE(rect, pos, actor.Rotation + 90.Degrees(), 7d, RectEscape);
                    AddAOE(rect, pos, actor.Rotation - 90.Degrees(), 7d, RectEscape);
                    break;
            }
        }
    }

    public override void OnEventCast(Actor caster, ActorCastEvent spell)
    {
        var count = _aoes.Count;
        if (count != 0)
        {
            void RemoveAOE(AOEShape shape)
            {
                var pos = caster.Position.Quantized();
                for (var i = 0; i < count; ++i)
                {
                    var aoe = _aoes[i];
                    if (aoe.Origin == pos && aoe.Shape == shape)
                    {
                        _aoes.RemoveAt(i);
                        _revealAt.RemoveAt(i);
                        return;
                    }
                }
            }
            switch (spell.Action.ID)
            {
                case (uint)AID.BambooSpawn:
                    RemoveAOE(bamboospawn);
                    break;
                case (uint)AID.BambooSplit:
                    RemoveAOE(rect);
                    break;
                case (uint)AID.BambooCircleFall:
                    RemoveAOE(circle);
                    break;
            }
        }
    }
}

sealed class DaigoroGilJump(BossModule module) : Components.SimpleChargeAOEGroups(module, [(uint)AID.FirstGilJump, (uint)AID.NextGilJump], HalfWidth, extraLengthFront: 5f)
{
    private const float HalfWidth = 3.5f;
    private readonly Dictionary<ulong, DateTime> _revealAt = [];
    private readonly TheSliceIsRightConfig _config = Service.Config.Get<TheSliceIsRightConfig>();
    private readonly AI.AIConfig _aiConfig = Service.Config.Get<AI.AIConfig>();

    public override ReadOnlySpan<AOEInstance> ActiveAOEs(int slot, Actor actor)
    {
        var aoes = base.ActiveAOEs(slot, actor);
        var span = CollectionsMarshal.AsSpan(Casters)[..aoes.Length];
        var now = WorldState.CurrentTime;
        for (var i = 0; i < span.Length; ++i)
        {
            ref var aoe = ref span[i];
            if (_revealAt.TryGetValue(aoe.ActorID, out var at) && now < at)
                aoe.Risky = false;
        }
        return span;
    }

    public override void OnCastStarted(Actor caster, ActorCastInfo spell)
    {
        base.OnCastStarted(caster, spell);
        if (spell.Action.ID is (uint)AID.FirstGilJump or (uint)AID.NextGilJump)
            _revealAt[caster.InstanceID] = SliceDodgeTiming.RevealAt(WorldState.CurrentTime, Module.CastFinishAt(spell), _config.DodgeDelaySeconds, SliceDodgeTiming.RequiredLeadSeconds(HalfWidth + 0.5f, _aiConfig.ActivationTimeCushion));
    }

    public override void OnCastFinished(Actor caster, ActorCastInfo spell)
    {
        base.OnCastFinished(caster, spell);
        _revealAt.Remove(caster.InstanceID);
    }
}

// 開盅時大五郎對盅前方放的扇形（15＋大五郎半徑 2.5）。讀條一開始就交給 AI，不吃「開始閃避前等待」：那個設定只管斬擊類。
sealed class DaigoroBadCup(BossModule module) : Components.SimpleAOEs(module, (uint)AID.BadCup, new AOEShapeCone(BadCupRange, BadCupHalfAngle))
{
    public const float BadCupRange = 17.5f;
    public static readonly Angle BadCupHalfAngle = 60f.Degrees();
}

/// <summary>三個盅階段：讓「不知道大五郎在哪個盅」在畫面上看得見，並寫 Information 診斷；危險區只有 <see cref="DaigoroBadCup"/>。</summary>
sealed class CupGame(BossModule module) : BossComponent(module)
{
    // 刻意不預測大五郎在哪個盅、也不替 AI 選盅：沒有重播能證明大五郎的位置或盅的動畫對得上開盅結果，
    // 猜錯＝AI 主動站進出局位置。判斷不了時只畫開盅讀條那一刻已確定的扇形；下面的診斷是為了日後用實機 log 定案。
    private const ushort YellFirst = 9291, YellLast = 9297; // 「來試一試運氣」～倒數「三！」
    private const double TimeoutSeconds = 60d, MoveLogSeconds = 0.5d;
    private const float MoveLogDistance = 1f;
    private static readonly uint[] _tracked = [(uint)OID.Daigoro, (uint)OID.HelperCupPhase1, (uint)OID.HelperCupPhase2, (uint)OID.HelperCupPhase3, (uint)OID.Pileofgold];
    private static readonly uint[] _cups = [(uint)OID.HelperCupPhase1, (uint)OID.HelperCupPhase2, (uint)OID.HelperCupPhase3];
    private readonly Dictionary<ulong, WPos> _lastLogged = [];
    private DateTime _start, _nextMoveLog;
    private bool _revealing;

    public bool Active => _start != default;

    private static bool IsTracked(uint oid) => Array.IndexOf(_tracked, oid) >= 0;

    private static void Log(string msg) => Service.Logger.Information($"[SliceIsRight] 盅：{msg}");

    private static string Describe(Actor a) => $"{a.OID:X}#{a.InstanceID & 0xFFFF:X}@({a.Position.X:f2},{a.Position.Z:f2}) 朝{a.Rotation.Deg:f0} 顯示={a.Renderflags == 0}";

    private string Snapshot()
    {
        var sb = new StringBuilder();
        foreach (var oid in _tracked)
            foreach (var a in Module.Enemies(oid))
                sb.Append(Describe(a)).Append("; ");
        if (Module.Raid.Player() is { } p)
            sb.Append($"玩家@({p.Position.X:f2},{p.Position.Z:f2})");
        return sb.ToString();
    }

    private void Begin(string why)
    {
        if (Active)
            return;
        _start = WorldState.CurrentTime;
        _nextMoveLog = default;
        _lastLogged.Clear();
        Log($"階段開始（{why}）；{Snapshot()}");
    }

    private void End(string why)
    {
        if (!Active)
            return;
        Log($"階段結束（{why}，經過 {(WorldState.CurrentTime - _start).TotalSeconds:f1} 秒）；{Snapshot()}");
        _start = default;
        _revealing = false;
    }

    public override void Update()
    {
        if (!Active)
            return;
        var now = WorldState.CurrentTime;
        if ((now - _start).TotalSeconds > TimeoutSeconds)
        {
            End("逾時");
            return;
        }
        if (now < _nextMoveLog)
            return;
        _nextMoveLog = now.AddSeconds(MoveLogSeconds);
        foreach (var oid in _tracked)
        {
            foreach (var a in Module.Enemies(oid))
            {
                var pos = a.Position;
                if (_lastLogged.TryGetValue(a.InstanceID, out var last) && (pos - last).LengthSq() < MoveLogDistance * MoveLogDistance)
                    continue;
                _lastLogged[a.InstanceID] = pos;
                Log($"位置 +{(now - _start).TotalSeconds:f1}s {Describe(a)}");
            }
        }
    }

    public override void AddGlobalHints(GlobalHints hints)
    {
        if (Active && !_revealing)
            hints.Add(Loc.T("Cups: BMR can't tell which cup hides Daigoro, so pick one yourself; the AI won't choose for you. Daigoro's cone is shown only when the cups open."));
    }

    public override void OnActorNpcYell(Actor actor, ushort id)
    {
        if (id is >= YellFirst and <= YellLast)
        {
            Begin($"台詞 {id}");
            Log($"台詞 {id} +{(WorldState.CurrentTime - _start).TotalSeconds:f1}s；{Snapshot()}");
        }
    }

    public override void OnActorCreated(Actor actor)
    {
        if (!IsTracked(actor.OID))
            return;
        if (Array.IndexOf(_cups, actor.OID) >= 0)
            Begin($"盅物件 {actor.OID:X} 出現");
        Log($"出現 {Describe(actor)}");
    }

    public override void OnActorDestroyed(Actor actor)
    {
        if (IsTracked(actor.OID))
            Log($"消失 {Describe(actor)}");
    }

    public override void OnActorEAnim(Actor actor, uint state)
    {
        if (IsTracked(actor.OID))
            Log($"動畫 0x{state:X8} {Describe(actor)}");
    }

    public override void OnActorRenderflags(Actor actor, int renderflags)
    {
        if (Active && IsTracked(actor.OID))
            Log($"顯示旗標 {renderflags} {Describe(actor)}");
    }

    public override void OnActorPlayActionTimelineEvent(Actor actor, ushort id)
    {
        if (Active && IsTracked(actor.OID))
            Log($"動作時間軸 {id} {Describe(actor)}");
    }

    public override void OnCastStarted(Actor caster, ActorCastInfo spell)
    {
        if (spell.Action.ID != (uint)AID.BadCup)
            return;
        Begin("開盅讀條");
        _revealing = true;
        Actor? nearest = null;
        var best = float.MaxValue;
        foreach (var oid in _cups)
        {
            foreach (var c in Module.Enemies(oid))
            {
                var d = (c.Position - caster.Position).LengthSq();
                if (d < best)
                {
                    best = d;
                    nearest = c;
                }
            }
        }
        var inCone = Module.Raid.Player() is { } p && p.Position.InCircle(caster.Position, DaigoroBadCup.BadCupRange) && p.Position.InCone(caster.Position, spell.Rotation, DaigoroBadCup.BadCupHalfAngle);
        var near = nearest != null ? $"{Describe(nearest)} 距 {MathF.Sqrt(best):f2}" : "無";
        Log($"開盅讀條 {Describe(caster)} 扇形朝{spell.Rotation.Deg:f0}；最近的盅物件 {near}；玩家在扇形內={inCone}；{Snapshot()}");
    }

    public override void OnEventCast(Actor caster, ActorCastEvent spell)
    {
        if (spell.Action.ID != (uint)AID.BadCup)
            return;
        var playerId = Module.Raid.Player()?.InstanceID ?? 0;
        var hit = false;
        foreach (var t in spell.Targets)
            hit |= t.ID == playerId;
        Log($"開盅命中 {spell.Targets.Count} 人，玩家被命中={hit}");
        End("開盅結束");
    }
}

sealed class TheSliceIsRightStates : StateMachineBuilder
{
    public TheSliceIsRightStates(BossModule module) : base(module)
    {
        TrivialPhase()
            .ActivateOnEnter<BambooSplits>()
            .ActivateOnEnter<DaigoroGilJump>()
            .ActivateOnEnter<DaigoroBadCup>()
            .ActivateOnEnter<CupGame>()
            .Raw.Update = () => module.PrimaryActor.IsDeadOrDestroyed || module.Raid.Player() is { } p && !TheSliceIsRight.StillParticipating(p.Position);
    }
}

[ModuleInfo(BossModuleInfo.Maturity.Verified, Contributors = "Malediktus", GroupType = BossModuleInfo.GroupType.GoldSaucer, GroupID = 181, NameID = 9066)]
public sealed class TheSliceIsRight(WorldState ws, Actor primary) : BossModule(ws, primary, arena.Center, arena)
{
    // 舞台中心取 EventRange 的位置（碰撞資料量到同一點），舞台邊緣半徑約 15.2～15.3。
    public static readonly WPos StageCenter = new(70.563f, -35.955f);
    // AI 能站的範圍比舞台邊緣往內留約 0.7 碼：走下舞台就出局，移動慢一拍或伺服器位置落後都會越線。
    public const float SafeRadius = 14.5f;
    // 判「還在參加」看舞台本身，不看縮小的 AI 範圍：站在外圈不能讓模組提早結束、AI 從此失去場地邊界。
    // 取量到的舞台邊緣上限 15.3、不再往外加：多出來的那圈只會收進剛出局的人與舞台旁的旁觀者。
    // 刻意不加高度判準：舞台下地面的高度沒有離線量過，猜錯會讓台上的人被判離場。
    public const float ParticipationRadius = 15.3f;
    private static readonly ArenaBoundsComplex arena = new([new Polygon(StageCenter, SafeRadius * CosPI.Pi28th, 28, 1.3f.Degrees())]);

    public static bool IsParticipating(WPos pos) => (pos - StageCenter).LengthSq() <= ParticipationRadius * ParticipationRadius;

    internal static bool StillParticipating(WPos pos)
    {
        if (IsParticipating(pos))
            return true;
        Service.Logger.Information($"[SliceIsRight] 玩家離舞台中心 {(pos - StageCenter).Length():f2} 碼，超過 {ParticipationRadius} 碼 ⇒ 視為已離場，模組結束");
        return false;
    }

    protected override bool CheckPull()
    {
        if (Raid.Player() is not { } p || !IsParticipating(p.Position)) // only activate module if player is taking part in the event
            return false;
        Service.Logger.Information($"[SliceIsRight] 開始：玩家離舞台中心 {(p.Position - StageCenter).Length():f2} 碼；AI 站位半徑 {SafeRadius}、閃避前等待 {Service.Config.Get<TheSliceIsRightConfig>().DodgeDelaySeconds:f1} 秒");
        return true;
    }
}
