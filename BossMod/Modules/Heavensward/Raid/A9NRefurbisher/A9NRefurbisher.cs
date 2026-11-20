namespace BossMod.Heavensward.Raid.A9NRefurbisher;

sealed class A9NRefurbisherStates : StateMachineBuilder
{
    public A9NRefurbisherStates(BossModule module) : base(module)
    {
        TrivialPhase()
            .ActivateOnEnter<Components.UnclaimedCastAOEs>();
    }
}

// 場地＝台服碰撞在 (0,-250) 的 40×40 方形地板內縮 0.5 碼；外圈高 1.5 碼的看台不算場地。
public abstract class A9NRefurbisher(WorldState ws, Actor primary) : UnverifiedOidTrialModule(ws, primary, new(0f, -250f), new ArenaBoundsSquare(19.5f));

[ModuleInfo(BossModuleInfo.Maturity.WIP, StatesType = typeof(A9NRefurbisherStates), PrimaryActorOID = (uint)OID.BossCandidateA, Contributors = "ffxiv-tc-port", Expansion = BossModuleInfo.Expansion.Heavensward, Category = BossModuleInfo.Category.Raid, GroupType = BossModuleInfo.GroupType.CFC, GroupID = 186u, NameID = 5170u, SortOrder = 1)]
public sealed class A9NRefurbisherA(WorldState ws, Actor primary) : A9NRefurbisher(ws, primary);

[ModuleInfo(BossModuleInfo.Maturity.WIP, StatesType = typeof(A9NRefurbisherStates), PrimaryActorOID = (uint)OID.BossCandidateB, Contributors = "ffxiv-tc-port", Expansion = BossModuleInfo.Expansion.Heavensward, Category = BossModuleInfo.Category.Raid, GroupType = BossModuleInfo.GroupType.CFC, GroupID = 186u, NameID = 5170u, SortOrder = 1)]
public sealed class A9NRefurbisherB(WorldState ws, Actor primary) : A9NRefurbisher(ws, primary);
