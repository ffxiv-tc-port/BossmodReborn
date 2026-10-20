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

sealed class TheSliceIsRightStates : StateMachineBuilder
{
    public TheSliceIsRightStates(BossModule module) : base(module)
    {
        TrivialPhase()
            .ActivateOnEnter<BambooSplits>()
            .ActivateOnEnter<DaigoroGilJump>()
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
    public const float ParticipationRadius = 15.5f;
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
