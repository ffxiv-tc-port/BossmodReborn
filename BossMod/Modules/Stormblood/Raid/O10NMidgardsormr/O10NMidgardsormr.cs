namespace BossMod.Stormblood.Raid.O10NMidgardsormr;

sealed class TailEnd(BossModule module) : Components.SingleTargetCast(module, (uint)AID.TailEnd);
sealed class AkhMorn(BossModule module) : Components.StackWithCastTargets(module, (uint)AID.AkhMorn, 6f);
// 點名到判定的秒數沒有錄影：取讀條長度 5 秒。
sealed class Thunderstorm(BossModule module) : Components.SpreadFromIcon(module, (uint)IconID.Thunderstorm, (uint)AID.Thunderstorm, 5f, 5d);
sealed class HorridRoar(BossModule module) : Components.SimpleAOEs(module, (uint)AID.HorridRoar, 6f);
sealed class Cauterize(BossModule module) : Components.SimpleAOEs(module, (uint)AID.Cauterize, new AOEShapeRect(40f, 10f));
sealed class DryIce1(BossModule module) : Components.SimpleAOEs(module, (uint)AID.DryIce1, 9f);
sealed class DryIce2(BossModule module) : Components.SimpleAOEs(module, (uint)AID.DryIce2, 12f);
sealed class RimeWreath(BossModule module) : Components.RaidwideCast(module, (uint)AID.RimeWreath);
sealed class NorthernCross(BossModule module) : Components.RaidwideCast(module, (uint)AID.NorthernCross);

// 兩次旋轉決定結果：橫＋橫＝鋼鐵、橫＋豎＝月環、豎＋豎＝十字（一條沿面向、一條垂直）；豎＋橫沒有資料，不預測。
// 第二次旋轉後先以首領當下位置與面向預測，真讀條開始後改用讀條本身（讀條只有 1～2 秒）。
sealed class AmbientAether(BossModule module) : Components.GenericAOEs(module)
{
    private static readonly AOEShapeCircle circle = new(14f);
    private static readonly AOEShapeDonut donut = new(10f, 40f);
    private static readonly AOEShapeCross cross = new(30f, 9f);
    private bool? _firstHorizontal;
    private AOEShape? _predicted;
    private DateTime _predictedAt;
    private AOEInstance[] _cast = [];
    private readonly AOEInstance[] _prediction = new AOEInstance[1];

    public override ReadOnlySpan<AOEInstance> ActiveAOEs(int slot, Actor actor) => _cast.Length != 0 ? _cast : _predicted != null ? _prediction : [];

    public override void OnEventCast(Actor caster, ActorCastEvent spell)
    {
        switch (spell.Action.ID)
        {
            case (uint)AID.SpinHorizontal1:
                _firstHorizontal = true;
                break;
            case (uint)AID.SpinVertical1:
                _firstHorizontal = false;
                break;
            case (uint)AID.SpinHorizontal2:
                _predicted = _firstHorizontal == true ? circle : null;
                _predictedAt = WorldState.FutureTime(3.7d);
                RefreshPrediction();
                break;
            case (uint)AID.SpinVertical2:
                _predicted = _firstHorizontal switch { true => donut, false => cross, _ => null };
                _predictedAt = WorldState.FutureTime(3.7d);
                RefreshPrediction();
                break;
            case (uint)AID.AetherOut or (uint)AID.AetherIn or (uint)AID.AetherCorners or (uint)AID.AetherCrossAlt:
                _firstHorizontal = null;
                _predicted = null;
                _cast = [];
                break;
        }
    }

    public override void OnCastStarted(Actor caster, ActorCastInfo spell)
    {
        AOEShape? shape = spell.Action.ID switch
        {
            (uint)AID.AetherOut => circle,
            (uint)AID.AetherIn => donut,
            (uint)AID.AetherCorners or (uint)AID.AetherCrossAlt => cross,
            _ => null
        };
        if (shape != null)
            _cast = [new(shape, spell.LocXZ, spell.Rotation, Module.CastFinishAt(spell))];
    }

    public override void Update()
    {
        if (_predicted != null && WorldState.CurrentTime > _predictedAt.AddSeconds(5d))
            _predicted = null;
        RefreshPrediction();
        if (_cast.Length != 0 && WorldState.CurrentTime > _cast[0].Activation.AddSeconds(3d))
            _cast = [];
    }

    private void RefreshPrediction()
    {
        if (_predicted != null)
        {
            var boss = Module.PrimaryActor;
            _prediction[0] = new(_predicted, boss.Position.Quantized(), boss.Rotation, _predictedAt);
        }
    }
}

sealed class O10NMidgardsormrStates : StateMachineBuilder
{
    public O10NMidgardsormrStates(BossModule module) : base(module)
    {
        TrivialPhase()
            .ActivateOnEnter<TailEnd>()
            .ActivateOnEnter<AkhMorn>()
            .ActivateOnEnter<Thunderstorm>()
            .ActivateOnEnter<HorridRoar>()
            .ActivateOnEnter<Cauterize>()
            .ActivateOnEnter<DryIce1>()
            .ActivateOnEnter<DryIce2>()
            .ActivateOnEnter<RimeWreath>()
            .ActivateOnEnter<NorthernCross>()
            .ActivateOnEnter<AmbientAether>()
            .ActivateOnEnter<Components.UnclaimedCastAOEs>();
    }
}

// 場地＝台服碰撞在 (100,100) 的方形地板（x/z 80.25～120）放得下的方形，取 0.5 碼整數；外圈是較高的另一塊地面。
public abstract class O10NMidgardsormr(WorldState ws, Actor primary) : UnverifiedOidTrialModule(ws, primary, new(100f, 100f), new ArenaBoundsSquare(19.5f));

[ModuleInfo(BossModuleInfo.Maturity.WIP, StatesType = typeof(O10NMidgardsormrStates), PrimaryActorOID = (uint)OID.BossCandidateA, Contributors = "ffxiv-tc-port", Expansion = BossModuleInfo.Expansion.Stormblood, Category = BossModuleInfo.Category.Raid, GroupType = BossModuleInfo.GroupType.CFC, GroupID = 588u, NameID = 7641u, SortOrder = 1)]
public sealed class O10NMidgardsormrA(WorldState ws, Actor primary) : O10NMidgardsormr(ws, primary);

[ModuleInfo(BossModuleInfo.Maturity.WIP, StatesType = typeof(O10NMidgardsormrStates), PrimaryActorOID = (uint)OID.BossCandidateB, Contributors = "ffxiv-tc-port", Expansion = BossModuleInfo.Expansion.Stormblood, Category = BossModuleInfo.Category.Raid, GroupType = BossModuleInfo.GroupType.CFC, GroupID = 588u, NameID = 7641u, SortOrder = 1)]
public sealed class O10NMidgardsormrB(WorldState ws, Actor primary) : O10NMidgardsormr(ws, primary);
