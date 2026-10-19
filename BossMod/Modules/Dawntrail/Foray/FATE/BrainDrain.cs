namespace BossMod.Dawntrail.Foray.FATE.BrainDrain;

public enum OID : uint
{
    Boss = 0x4737,
    Helper = 0x4738
}

public enum AID : uint
{
    AutoAttack = 42005, // Boss->player, no cast, single-target
    Teleport = 41995, // Boss->location, no cast, single-target

    ZombieScalesVisual1 = 41998, // Boss->self, 8.0s cast, single-target
    ZombieScalesVisual2 = 41997, // Boss->self, 8.0s cast, single-target
    ZombieScalesVisual3 = 41999, // Boss->self, 8.0s cast, single-target
    ZombieScalesVisual4 = 41996, // Boss->self, 8.0s cast, single-target
    ZombieScales1 = 42000, // Helper->location, 8.0s cast, range 40 45-degree cone
    ZombieScales2 = 42001, // Helper->location, 8.0s cast, range 40 45-degree cone

    AeroIIVisual = 42017, // Boss->self, 3.0s cast, single-target
    AeroII = 42018, // Helper->location, 3.0s cast, range 4 circle

    ZombieBreath = 42004, // Boss->self, 5.0s cast, range 40 180-degree cone
    TripleFlight = 42012, // Boss->self, 4.0s cast, range 10-20 donut
    CyclonicRing = 42013, // Boss->self, no cast, range 10-20 donut
    CycloneVisual = 42010, // Boss->self, no cast, single-target
    Cyclone1 = 42011, // Helper->location, no cast, range 10 circle
    Cyclone2 = 42009, // Helper->location, 4.0s cast, range 10 circle
    FlashFoehn = 42014, // Boss->self, no cast, range 80 width 10 rect

    QuarryLakeVisual = 42002, // Boss->self, 5.0s cast, single-target, gaze
    QuarryLake = 42003, // Helper->location, 5.0s cast, range 40 circle

    BreathWingVisual1 = 42006, // Boss->self, 5.0s cast, single-target
    BreathWingVisual2 = 42008, // Boss->self, 4.0s cast, single-target
    BreathWing = 42007 // Helper->location, 5.0s cast, range 30 circle, raidwide
}

sealed class ZombieScales : Components.SimpleAOEGroups
{
    public ZombieScales(BossModule module) : base(module, [(uint)AID.ZombieScales1, (uint)AID.ZombieScales2], new AOEShapeCone(40f, 22.5f.Degrees()), 6)
    {
        MaxDangerColor = 2;
    }
}
sealed class AeroII(BossModule module) : Components.SimpleAOEs(module, (uint)AID.AeroII, 4f);
sealed class ZombieBreath(BossModule module) : Components.SimpleAOEs(module, (uint)AID.ZombieBreath, new AOEShapeCone(40f, 90f.Degrees()));
sealed class QuarryLake(BossModule module) : Components.CastGaze(module, (uint)AID.QuarryLake, range: 40f);
sealed class BreathWing(BossModule module) : Components.RaidwideCast(module, (uint)AID.BreathWing);

sealed class TripleFlightCyclone(BossModule module) : Components.GenericAOEs(module)
{
    private static readonly AOEShapeDonut donut = new(10f, 20f);
    private static readonly AOEShapeCircle circle = new(10f);
    private static readonly AOEShapeRect rect = new(40f, 5f, 40f);
    private const float FlashFoehnDelay = 4.2f; // 第三段實際約在讀條結束後 4.3 秒放出；取略早，寧可早躲
    private readonly List<AOEInstance> _aoes = new(3);

    public override ReadOnlySpan<AOEInstance> ActiveAOEs(int slot, Actor actor)
    {
        var count = _aoes.Count;
        if (count == 0)
            return [];
        var max = count > 1 ? 2 : 1;
        var aoes = CollectionsMarshal.AsSpan(_aoes)[..max];
        aoes[0].Risky = true;
        if (count > 1)
        {
            aoes[0].Color = Colors.Danger;
        }
        return aoes;
    }

    public override void OnCastStarted(Actor caster, ActorCastInfo spell)
    {
        var id = spell.Action.ID;
        if (id == (uint)AID.TripleFlight)
        {
            AddAOE(donut);
            AddAOE(circle, 2.1f);
            AddAOE(rect, FlashFoehnDelay);
        }
        else if (id == (uint)AID.Cyclone2)
        {
            AddAOE(circle);
            AddAOE(donut, 2.1f);
            // Cyclone2 是 helper 讀條（朝向固定 −π）；FlashFoehn 沿王在讀條開始時的朝向
            AddAOE(rect, FlashFoehnDelay, Module.PrimaryActor.Rotation);
        }
        void AddAOE(AOEShape shape, float delay = default, Angle? rotation = null) => _aoes.Add(new(shape, spell.LocXZ, rotation ?? spell.Rotation, Module.CastFinishAt(spell, delay), risky: false));
    }

    public override void AddAIHints(int slot, Actor actor, PartyRolesConfig.Assignment assignment, AIHints hints)
    {
        base.AddAIHints(slot, actor, assignment, hints);
        if (!AIHints.LookaheadEnabled)
            return;
        // 第一段已由基底登記成禁區；後面幾段交給 AI 的閃避預判
        var count = _aoes.Count;
        for (var i = 1; i < count; ++i)
        {
            var aoe = _aoes[i];
            hints.AddFutureForbiddenZone(aoe.Shape, aoe.Origin, aoe.Rotation, aoe.Activation);
        }
    }

    public override void OnEventCast(Actor caster, ActorCastEvent spell)
    {
        if (_aoes.Count != 0 && spell.Action.ID is (uint)AID.TripleFlight or (uint)AID.Cyclone1 or (uint)AID.Cyclone2 or (uint)AID.CyclonicRing or (uint)AID.FlashFoehn)
            _aoes.RemoveAt(0);
    }
}

sealed class BrainDrainStates : StateMachineBuilder
{
    public BrainDrainStates(BossModule module) : base(module)
    {
        TrivialPhase()
            .ActivateOnEnter<ZombieScales>()
            .ActivateOnEnter<AeroII>()
            .ActivateOnEnter<ZombieBreath>()
            .ActivateOnEnter<QuarryLake>()
            .ActivateOnEnter<BreathWing>()
            .ActivateOnEnter<TripleFlightCyclone>();
    }
}

[ModuleInfo(BossModuleInfo.Maturity.Verified, Contributors = "The Combat Reborn Team (Malediktus)", GroupType = BossModuleInfo.GroupType.ForayFATE, GroupID = 1018, NameID = 1967)]
public sealed class BrainDrain(WorldState ws, Actor primary) : SimpleBossModule(ws, primary);
