namespace BossMod.Heavensward.Raid.A3NLivingLiquid;

sealed class Cascade(BossModule module) : Components.RaidwideCast(module, (uint)AID.Cascade);
// 標記到判定的秒數沒有錄影，5 秒只影響 AI 開始散開的時機。
sealed class Sluice(BossModule module) : Components.SpreadFromIcon(module, (uint)IconID.Sluice, (uint)AID.Sluice, 5f, 5d);
sealed class Drainage(BossModule module) : Components.BaitAwayTethers(module, 6f, (uint)TetherID.Drainage, (uint)AID.Drainage);

sealed class A3NLivingLiquidStates : StateMachineBuilder
{
    public A3NLivingLiquidStates(BossModule module) : base(module)
    {
        TrivialPhase()
            .ActivateOnEnter<Cascade>()
            .ActivateOnEnter<Sluice>()
            .ActivateOnEnter<Drainage>()
            .ActivateOnEnter<Components.UnclaimedCastAOEs>();
    }
}

// 場地＝台服碰撞在 (58,-63) 那一塊圓形地板放得下的最大圓（避開邊緣的三角凸塊）；邊緣通電帶的寬度沒有證據，不另外畫。
public abstract class A3NLivingLiquid(WorldState ws, Actor primary) : UnverifiedOidTrialModule(ws, primary, new(58f, -63f), new ArenaBoundsCircle(29f));

[ModuleInfo(BossModuleInfo.Maturity.WIP, StatesType = typeof(A3NLivingLiquidStates), PrimaryActorOID = (uint)OID.BossCandidateA, Contributors = "ffxiv-tc-port", Expansion = BossModuleInfo.Expansion.Heavensward, Category = BossModuleInfo.Category.Raid, GroupType = BossModuleInfo.GroupType.CFC, GroupID = 114u, NameID = 3765u, SortOrder = 1)]
public sealed class A3NLivingLiquidA(WorldState ws, Actor primary) : A3NLivingLiquid(ws, primary);

[ModuleInfo(BossModuleInfo.Maturity.WIP, StatesType = typeof(A3NLivingLiquidStates), PrimaryActorOID = (uint)OID.BossCandidateB, Contributors = "ffxiv-tc-port", Expansion = BossModuleInfo.Expansion.Heavensward, Category = BossModuleInfo.Category.Raid, GroupType = BossModuleInfo.GroupType.CFC, GroupID = 114u, NameID = 3765u, SortOrder = 1)]
public sealed class A3NLivingLiquidB(WorldState ws, Actor primary) : A3NLivingLiquid(ws, primary);
