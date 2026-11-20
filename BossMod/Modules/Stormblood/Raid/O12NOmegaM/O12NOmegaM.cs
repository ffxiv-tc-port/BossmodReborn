namespace BossMod.Stormblood.Raid.O12NOmegaM;

sealed class EfficientBladework(BossModule module) : Components.SimpleAOEs(module, (uint)AID.EfficientBladework, 10f);
sealed class BeyondStrength(BossModule module) : Components.SimpleAOEs(module, (uint)AID.BeyondStrength, new AOEShapeDonut(10f, 40f));
sealed class OptimizedBlizzard(BossModule module) : Components.SimpleAOEs(module, (uint)AID.OptimizedBlizzard, new AOEShapeCross(40f, 5f));
sealed class SuperliminalSteel(BossModule module) : Components.SimpleAOEGroups(module, [(uint)AID.SuperliminalSteel1, (uint)AID.SuperliminalSteel2], new AOEShapeRect(80f, 18f));
sealed class Floodlight(BossModule module) : Components.SimpleAOEs(module, (uint)AID.Floodlight, 6f);
sealed class Spotlight(BossModule module) : Components.StackWithCastTargets(module, (uint)AID.Spotlight, 6f);
sealed class OptimizedFire(BossModule module) : Components.SpreadFromCastTargets(module, (uint)AID.OptimizedFire, 5f);
sealed class SolarRay1(BossModule module) : Components.SingleTargetCast(module, (uint)AID.SolarRay1);
sealed class SolarRay2(BossModule module) : Components.SingleTargetCast(module, (uint)AID.SolarRay2);
sealed class OptimizedBladeDance1(BossModule module) : Components.SingleTargetCast(module, (uint)AID.OptimizedBladeDance1);
sealed class OptimizedBladeDance2(BossModule module) : Components.SingleTargetCast(module, (uint)AID.OptimizedBladeDance2);
sealed class LaserShower1(BossModule module) : Components.RaidwideCast(module, (uint)AID.LaserShower1);
sealed class LaserShower2(BossModule module) : Components.RaidwideCast(module, (uint)AID.LaserShower2);
sealed class CosmoMemory1(BossModule module) : Components.RaidwideCast(module, (uint)AID.CosmoMemory1);
sealed class CosmoMemory2(BossModule module) : Components.RaidwideCast(module, (uint)AID.CosmoMemory2);
sealed class OpticalLaser(BossModule module) : Components.SimpleAOEs(module, (uint)AID.OpticalLaser, new AOEShapeRect(100f, 8f));
sealed class OptimizedSagittariusArrow(BossModule module) : Components.SimpleAOEs(module, (uint)AID.OptimizedSagittariusArrow, new AOEShapeRect(100f, 5f));

// 變形M 之後約 12.1 秒有一發沒有讀條的劍擊（半徑 10）：以變形者當下位置預告，判定或逾時 3 秒後清除。
sealed class EfficientBladeworkHidden(BossModule module) : Components.GenericAOEs(module, (uint)AID.EfficientBladeworkHidden)
{
    private static readonly AOEShapeCircle circle = new(10f);
    private readonly AOEInstance[] _aoe = new AOEInstance[1];
    private ulong _casterID;
    private DateTime _activation;

    public override ReadOnlySpan<AOEInstance> ActiveAOEs(int slot, Actor actor) => _casterID != default ? _aoe : [];

    public override void OnEventCast(Actor caster, ActorCastEvent spell)
    {
        if (spell.Action.ID == (uint)AID.SubjectSimulationM)
        {
            _casterID = caster.InstanceID;
            _activation = WorldState.FutureTime(12.1d);
            Update();
        }
        else if (spell.Action.ID == WatchedAction)
        {
            _casterID = default;
        }
    }

    public override void Update()
    {
        if (_casterID == default)
            return;
        if (WorldState.CurrentTime > _activation.AddSeconds(3d) || WorldState.Actors.Find(_casterID) is not Actor caster)
        {
            _casterID = default;
            return;
        }
        _aoe[0] = new(circle, caster.Position.Quantized(), default, _activation);
    }
}

sealed class O12NOmegaMStates : StateMachineBuilder
{
    public O12NOmegaMStates(BossModule module) : base(module)
    {
        TrivialPhase()
            .ActivateOnEnter<EfficientBladework>()
            .ActivateOnEnter<EfficientBladeworkHidden>()
            .ActivateOnEnter<BeyondStrength>()
            .ActivateOnEnter<OptimizedBlizzard>()
            .ActivateOnEnter<SuperliminalSteel>()
            .ActivateOnEnter<Floodlight>()
            .ActivateOnEnter<Spotlight>()
            .ActivateOnEnter<OptimizedFire>()
            .ActivateOnEnter<SolarRay1>()
            .ActivateOnEnter<SolarRay2>()
            .ActivateOnEnter<OptimizedBladeDance1>()
            .ActivateOnEnter<OptimizedBladeDance2>()
            .ActivateOnEnter<LaserShower1>()
            .ActivateOnEnter<LaserShower2>()
            .ActivateOnEnter<CosmoMemory1>()
            .ActivateOnEnter<CosmoMemory2>()
            .ActivateOnEnter<OpticalLaser>()
            .ActivateOnEnter<OptimizedSagittariusArrow>()
            .ActivateOnEnter<Components.UnclaimedCastAOEs>();
    }
}

// 場地＝台服碰撞在 (100,100) 的圓形地板（半徑約 20），攻略說是圓形平台、會掉下去；半徑 20 的圓內每格都有地板。
public abstract class O12NOmegaM(WorldState ws, Actor primary) : UnverifiedOidTrialModule(ws, primary, new(100f, 100f), new ArenaBoundsCircle(20f));

[ModuleInfo(BossModuleInfo.Maturity.WIP, StatesType = typeof(O12NOmegaMStates), PrimaryActorOID = (uint)OID.BossCandidateA, Contributors = "ffxiv-tc-port", Expansion = BossModuleInfo.Expansion.Stormblood, Category = BossModuleInfo.Category.Raid, GroupType = BossModuleInfo.GroupType.CFC, GroupID = 590u, NameID = 7633u, SortOrder = 1)]
public sealed class O12NOmegaMA(WorldState ws, Actor primary) : O12NOmegaM(ws, primary);

[ModuleInfo(BossModuleInfo.Maturity.WIP, StatesType = typeof(O12NOmegaMStates), PrimaryActorOID = (uint)OID.BossCandidateB, Contributors = "ffxiv-tc-port", Expansion = BossModuleInfo.Expansion.Stormblood, Category = BossModuleInfo.Category.Raid, GroupType = BossModuleInfo.GroupType.CFC, GroupID = 590u, NameID = 7633u, SortOrder = 1)]
public sealed class O12NOmegaMB(WorldState ws, Actor primary) : O12NOmegaM(ws, primary);
