namespace BossMod.Endwalker.Raid.P12NAthena;

sealed class OnTheSoul(BossModule module) : Components.RaidwideCast(module, (uint)AID.OnTheSoul);
sealed class TheosUltima(BossModule module) : Components.RaidwideCast(module, (uint)AID.TheosUltima);
sealed class RayOfLight(BossModule module) : Components.SimpleAOEGroups(module, [(uint)AID.RayOfLight1, (uint)AID.RayOfLight2], new AOEShapeRect(60f, 5f));
sealed class SuperchainBurst(BossModule module) : Components.SimpleAOEs(module, (uint)AID.SuperchainBurst, 7f);
sealed class SuperchainCoil(BossModule module) : Components.SimpleAOEs(module, (uint)AID.SuperchainCoil, new AOEShapeDonut(6f, 70f));
sealed class Glaukopis(BossModule module) : Components.BaitAwayCast(module, (uint)AID.Glaukopis, new AOEShapeRect(60f, 2.5f), tankbuster: true);
sealed class DialogosStack(BossModule module) : Components.StackWithCastTargets(module, (uint)AID.DialogosStack, 6f, 8, 8);
sealed class DialogosSpread(BossModule module) : Components.SpreadFromCastTargets(module, (uint)AID.DialogosSpread, 6f);
sealed class Parthenos(BossModule module) : Components.SimpleAOEs(module, (uint)AID.Parthenos, new AOEShapeRect(120f, 8f));
sealed class PalladionPlatforms(BossModule module) : Components.SimpleAOEs(module, (uint)AID.PalladionPlatforms, new AOEShapeRect(20f, 20f));

// 三魂一體：第一下有讀條（方向＝讀條方向），第二、三下由王身上的翅膀標記決定是王的左半或右半，各隔約 2.6 秒。
// 只畫下一下；一般難度沒有零式的反向順序。
sealed class TrinityOfSouls(BossModule module) : Components.GenericAOEs(module)
{
    private readonly List<AOEInstance> _aoes = [];
    private static readonly AOEShapeCone _shape = new(60f, 90f.Degrees());

    public override ReadOnlySpan<AOEInstance> ActiveAOEs(int slot, Actor actor) => _aoes.Count != 0 ? CollectionsMarshal.AsSpan(_aoes)[..1] : [];

    public override void OnCastStarted(Actor caster, ActorCastInfo spell)
    {
        if (spell.Action.ID is (uint)AID.TrinityOfSoulsTR or (uint)AID.TrinityOfSoulsTL)
        {
            _aoes.Clear();
            _aoes.Add(new(_shape, spell.LocXZ, spell.Rotation, Module.CastFinishAt(spell)));
        }
    }

    public override void OnEventIcon(Actor actor, uint iconID, ulong targetID)
    {
        if (_aoes.Count == 0)
            return;
        var offset = iconID switch
        {
            (uint)IconID.WingML or (uint)IconID.WingBL => 90f,
            (uint)IconID.WingMR or (uint)IconID.WingBR => -90f,
            _ => 0f
        };
        if (offset != 0f)
            _aoes.Add(new(_shape, Module.PrimaryActor.Position, Module.PrimaryActor.Rotation + offset.Degrees(), _aoes[^1].Activation.AddSeconds(2.6d)));
    }

    public override void OnEventCast(Actor caster, ActorCastEvent spell)
    {
        switch (spell.Action.ID)
        {
            case (uint)AID.TrinityOfSoulsTR:
            case (uint)AID.TrinityOfSoulsTL:
            case (uint)AID.TrinityOfSoulsMR:
            case (uint)AID.TrinityOfSoulsML:
            case (uint)AID.TrinityOfSoulsBR:
            case (uint)AID.TrinityOfSoulsBL:
                if (_aoes.Count != 0)
                    _aoes.RemoveAt(0);
                ++NumCasts;
                break;
        }
    }
}

sealed class P12NAthenaStates : StateMachineBuilder
{
    public P12NAthenaStates(BossModule module) : base(module)
    {
        TrivialPhase()
            .ActivateOnEnter<OnTheSoul>()
            .ActivateOnEnter<TheosUltima>()
            .ActivateOnEnter<TrinityOfSouls>()
            .ActivateOnEnter<RayOfLight>()
            .ActivateOnEnter<SuperchainBurst>()
            .ActivateOnEnter<SuperchainCoil>()
            .ActivateOnEnter<Glaukopis>()
            .ActivateOnEnter<DialogosStack>()
            .ActivateOnEnter<DialogosSpread>()
            .ActivateOnEnter<Parthenos>()
            .ActivateOnEnter<PalladionPlatforms>()
            .ActivateOnEnter<Components.UnclaimedCastAOEs>();
    }
}

[ModuleInfo(BossModuleInfo.Maturity.WIP, Contributors = "ffxiv-tc-port", GroupType = BossModuleInfo.GroupType.CFC, GroupID = 942, NameID = 12377)]
public sealed class P12NAthena(WorldState ws, Actor primary) : BossModule(ws, primary, new(100f, 100f), new ArenaBoundsSquare(20f));
