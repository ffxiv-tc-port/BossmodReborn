namespace BossMod.Heavensward.Raid.A11NCruiseChaser;

sealed class A11NCruiseChaserStates : StateMachineBuilder
{
    public A11NCruiseChaserStates(BossModule module) : base(module)
    {
        TrivialPhase()
            .ActivateOnEnter<Components.UnclaimedCastAOEs>();
    }
}

// 場地＝台服碰撞在 (0,52) 的圓形平台（半徑約 21.1）放得下的最大圓，取 0.5 碼整數；平台外沒有地板。
public abstract class A11NCruiseChaser(WorldState ws, Actor primary) : UnverifiedOidTrialModule(ws, primary, new(0f, 52f), new ArenaBoundsCircle(20.5f));

[ModuleInfo(BossModuleInfo.Maturity.WIP, StatesType = typeof(A11NCruiseChaserStates), PrimaryActorOID = (uint)OID.BossCandidateA, Contributors = "ffxiv-tc-port", Expansion = BossModuleInfo.Expansion.Heavensward, Category = BossModuleInfo.Category.Raid, GroupType = BossModuleInfo.GroupType.CFC, GroupID = 188u, NameID = 5186u, SortOrder = 1)]
public sealed class A11NCruiseChaserA(WorldState ws, Actor primary) : A11NCruiseChaser(ws, primary);

[ModuleInfo(BossModuleInfo.Maturity.WIP, StatesType = typeof(A11NCruiseChaserStates), PrimaryActorOID = (uint)OID.BossCandidateB, Contributors = "ffxiv-tc-port", Expansion = BossModuleInfo.Expansion.Heavensward, Category = BossModuleInfo.Category.Raid, GroupType = BossModuleInfo.GroupType.CFC, GroupID = 188u, NameID = 5186u, SortOrder = 1)]
public sealed class A11NCruiseChaserB(WorldState ws, Actor primary) : A11NCruiseChaser(ws, primary);
