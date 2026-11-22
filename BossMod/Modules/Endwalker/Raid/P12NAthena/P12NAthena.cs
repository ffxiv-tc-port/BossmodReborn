namespace BossMod.Endwalker.Raid.P12NAthena;

sealed class OnTheSoul(BossModule module) : Components.RaidwideCast(module, (uint)AID.OnTheSoul);
sealed class TheosUltima(BossModule module) : Components.RaidwideCast(module, (uint)AID.TheosUltima);
sealed class RayOfLight(BossModule module) : Components.SimpleAOEGroups(module, [(uint)AID.RayOfLight1, (uint)AID.RayOfLight2], new AOEShapeRect(60f, 5f));
sealed class SuperchainBurst(BossModule module) : Components.SimpleAOEs(module, (uint)AID.SuperchainBurst, 7f);
sealed class SuperchainCoil(BossModule module) : Components.SimpleAOEs(module, (uint)AID.SuperchainCoil, new AOEShapeDonut(6f, 70f));
sealed class Glaukopis(BossModule module) : Components.BaitAwayCast(module, (uint)AID.Glaukopis, new AOEShapeRect(60f, 2.5f), tankbuster: true, damageType: AIHints.PredictedDamageType.Tankbuster);
sealed class DialogosStack(BossModule module) : Components.StackWithCastTargets(module, (uint)AID.DialogosStack, 6f, 8, 8);
sealed class DialogosSpread(BossModule module) : Components.SpreadFromCastTargets(module, (uint)AID.DialogosSpread, 6f);
sealed class Parthenos(BossModule module) : Components.SimpleAOEs(module, (uint)AID.Parthenos, new AOEShapeRect(120f, 8f));
sealed class PalladionPlatforms(BossModule module) : Components.SimpleAOEs(module, (uint)AID.PalladionPlatforms, new AOEShapeRect(20f, 20f));

// 連線在白火判定前約 0.5 秒就斷了，誘導留到判定（或逾時）才清；打到誰不是死刑，不標成坦克傷害。
sealed class WhiteFlame : Components.BaitAwayTethers
{
    public WhiteFlame(BossModule module) : base(module, new AOEShapeRect(100f, 2f), (uint)TetherID.WhiteFlame, (uint)AID.WhiteFlame, (uint)OID.Anthropos, 5.9d)
    {
        DamageType = AIHints.PredictedDamageType.None;
    }

    public override void OnUntethered(Actor source, ActorTetherInfo tether) { }

    public override void OnEventCast(Actor caster, ActorCastEvent spell)
    {
        if (spell.Action.ID == WatchedAction)
        {
            var targets = spell.Targets;
            for (var i = CurrentBaits.Count - 1; i >= 0; --i)
            {
                var id = CurrentBaits[i].Target.InstanceID;
                for (var j = 0; j < targets.Count; ++j)
                {
                    if (targets[j].ID == id)
                    {
                        CurrentBaits.RemoveAt(i);
                        break;
                    }
                }
            }
        }
        base.OnEventCast(caster, spell); // 先移除再呼叫：清空後 base 才會重設下一輪的生效時間
    }

    public override void Update()
    {
        var expire = WorldState.CurrentTime.AddSeconds(-1.5d);
        for (var i = CurrentBaits.Count - 1; i >= 0; --i)
        {
            if (CurrentBaits[i].Activation < expire)
                CurrentBaits.RemoveAt(i);
        }
        if (CurrentBaits.Count == 0)
            activation = default;
    }
}

// 究極之刃：王的無讀條動作後約 7.5 秒全場傷害。
sealed class UltimaBlade(BossModule module) : Components.RaidwideInstant(module, (uint)AID.UltimaBlade, 7.5d)
{
    protected override uint PredictedSourceAction => WatchedAction;

    public override void OnEventCast(Actor caster, ActorCastEvent spell)
    {
        if (spell.Action.ID == (uint)AID.UltimaBladeVisual)
            Activation = WorldState.FutureTime(Delay);
        else
            base.OnEventCast(caster, spell);
    }
}

// 究極之刃後中央八角形以外是即死地面，直到小怪全滅（場地效果 10 復原，或女神的護佑橫斬開始讀條）。
// 八角形半徑與方向照零式（同場地）：頂點在正南北東西與四個斜角、外接半徑 14。
sealed class UltimaBladePuddle(BossModule module) : Components.GenericAOEs(module)
{
    private static readonly WPos _center = new(100f, 100f);
    private static readonly AOEShapeCustom _shape = new([new Square(_center, 20f)], [new Polygon(_center, 14f, 8)]);
    private AOEInstance[] _aoe = [];

    public override ReadOnlySpan<AOEInstance> ActiveAOEs(int slot, Actor actor) => _aoe;

    public override void OnEventCast(Actor caster, ActorCastEvent spell)
    {
        if (spell.Action.ID == (uint)AID.UltimaBladeVisual)
            _aoe = [new(_shape, _center, default, WorldState.FutureTime(7.5d))];
    }

    public override void OnCastStarted(Actor caster, ActorCastInfo spell)
    {
        if (spell.Action.ID == (uint)AID.PalladionPlatforms)
            _aoe = [];
    }

    public override void OnEventEnvControl(byte index, uint state)
    {
        if (index == 0x0A && state == 0x00080004u)
            _aoe = [];
    }
}

// 三魂一體：第一下有讀條（方向＝讀條方向），第二、三下由翅膀（Wing）身上的標記決定是王的左半或右半，各隔約 2.6 秒。
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
            .ActivateOnEnter<Platforms>()
            .ActivateOnEnter<SuperchainTheory>()
            .ActivateOnEnter<WhiteFlame>()
            .ActivateOnEnter<UltimaBlade>()
            .ActivateOnEnter<UltimaBladePuddle>()
            .ActivateOnEnter<PalladionJump>()
            .ActivateOnEnter<PalladionShockwave>()
            .ActivateOnEnter<Components.UnclaimedCastAOEs>();
    }
}

[ModuleInfo(BossModuleInfo.Maturity.Contributed, Contributors = "ffxiv-tc-port", GroupType = BossModuleInfo.GroupType.CFC, GroupID = 942, NameID = 12377)]
public sealed class P12NAthena(WorldState ws, Actor primary) : BossModule(ws, primary, new(100f, 100f), new ArenaBoundsSquare(20f));
