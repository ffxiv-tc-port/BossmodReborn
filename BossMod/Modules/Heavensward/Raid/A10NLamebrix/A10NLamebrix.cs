namespace BossMod.Heavensward.Raid.A10NLamebrix;

sealed class GobrushRushgob(BossModule module) : Components.SingleTargetCast(module, (uint)AID.GobrushRushgob);
sealed class BombToss(BossModule module) : Components.StackWithCastTargets(module, (uint)AID.BombToss, 6f);
// 標記到判定的秒數沒有錄影：時間軸上前置動作到判定 5 秒，只影響 AI 開始散開的時機。
sealed class CriticalWrath(BossModule module) : Components.SpreadFromIcon(module, (uint)IconID.CriticalWrath, (uint)AID.CriticalWrath, 4f, 5d);
sealed class GobslashSlicetops(BossModule module) : Components.BaitAwayIcon(module, new AOEShapeRect(50f, 2f), (uint)IconID.GobslashSlicetops, (uint)AID.GobslashSlicetops);

// 紅箭頭（SingleChargeOut）約 4 秒後首領身邊半徑 8 的迴旋斬。
// 刻意不畫綠圈（SingleChargeIn）接的甜甜圈：內徑沒有證據，猜大了 AI 會站進範圍裡。
sealed class GobspinWhooshdrops(BossModule module) : Components.GenericAOEs(module, (uint)AID.GobspinWhooshdrops)
{
    private static readonly AOEShapeCircle circle = new(8f);
    private AOEInstance[] _aoe = [];

    public override ReadOnlySpan<AOEInstance> ActiveAOEs(int slot, Actor actor) => _aoe;

    public override void OnEventCast(Actor caster, ActorCastEvent spell)
    {
        var id = spell.Action.ID;
        if (id == (uint)AID.SingleChargeOut)
            _aoe = [new(circle, caster.Position.Quantized(), default, WorldState.FutureTime(4.1d))];
        else if (id == WatchedAction)
            _aoe = [];
    }

    // 判定事件沒來（例：首領死了）也不讓危險區一直留著。
    public override void Update()
    {
        if (_aoe.Length != 0 && WorldState.CurrentTime > _aoe[0].Activation.AddSeconds(3d))
            _aoe = [];
    }
}

sealed class A10NLamebrixStates : StateMachineBuilder
{
    public A10NLamebrixStates(BossModule module) : base(module)
    {
        TrivialPhase()
            .ActivateOnEnter<GobrushRushgob>()
            .ActivateOnEnter<BombToss>()
            .ActivateOnEnter<CriticalWrath>()
            .ActivateOnEnter<GobslashSlicetops>()
            .ActivateOnEnter<GobspinWhooshdrops>()
            .ActivateOnEnter<Components.UnclaimedCastAOEs>();
    }
}

// 場地＝台服碰撞在 (0,0) 中央那條 20×80 的走道（按鈕所在）內縮；兩側同高的地板是攻略說的通電區，不算場地。
public abstract class A10NLamebrix(WorldState ws, Actor primary) : UnverifiedOidTrialModule(ws, primary, default, new ArenaBoundsRect(9.5f, 39.5f));

[ModuleInfo(BossModuleInfo.Maturity.WIP, StatesType = typeof(A10NLamebrixStates), PrimaryActorOID = (uint)OID.BossCandidateA, Contributors = "ffxiv-tc-port", Expansion = BossModuleInfo.Expansion.Heavensward, Category = BossModuleInfo.Category.Raid, GroupType = BossModuleInfo.GroupType.CFC, GroupID = 187u, NameID = 5180u, SortOrder = 1)]
public sealed class A10NLamebrixA(WorldState ws, Actor primary) : A10NLamebrix(ws, primary);

[ModuleInfo(BossModuleInfo.Maturity.WIP, StatesType = typeof(A10NLamebrixStates), PrimaryActorOID = (uint)OID.BossCandidateB, Contributors = "ffxiv-tc-port", Expansion = BossModuleInfo.Expansion.Heavensward, Category = BossModuleInfo.Category.Raid, GroupType = BossModuleInfo.GroupType.CFC, GroupID = 187u, NameID = 5180u, SortOrder = 1)]
public sealed class A10NLamebrixB(WorldState ws, Actor primary) : A10NLamebrix(ws, primary);
