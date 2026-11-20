namespace BossMod.Heavensward.Raid.A4NManipulator;

// 5095 在 Action 表是半徑 10 的圓，後備猜法會照畫；零式時間軸與攻略都說是全體傷害，所以自己認領。
sealed class MortalRevolution1(BossModule module) : Components.RaidwideCast(module, (uint)AID.MortalRevolution1);
sealed class MortalRevolution2(BossModule module) : Components.RaidwideCast(module, (uint)AID.MortalRevolution2);

sealed class A4NManipulatorStates : StateMachineBuilder
{
    public A4NManipulatorStates(BossModule module) : base(module)
    {
        TrivialPhase()
            .ActivateOnEnter<MortalRevolution1>()
            .ActivateOnEnter<MortalRevolution2>()
            .ActivateOnEnter<Components.UnclaimedCastAOEs>();
    }
}

// 場地＝台服碰撞在 (0,0) 的圓形平台放得下的最大圓，與既有障礙物圖 445.115 的可走圓一致。
public abstract class A4NManipulator(WorldState ws, Actor primary) : UnverifiedOidTrialModule(ws, primary, default, new ArenaBoundsCircle(24f));

[ModuleInfo(BossModuleInfo.Maturity.WIP, StatesType = typeof(A4NManipulatorStates), PrimaryActorOID = (uint)OID.BossCandidateA, Contributors = "ffxiv-tc-port", Expansion = BossModuleInfo.Expansion.Heavensward, Category = BossModuleInfo.Category.Raid, GroupType = BossModuleInfo.GroupType.CFC, GroupID = 115u, NameID = 3772u, SortOrder = 1)]
public sealed class A4NManipulatorA(WorldState ws, Actor primary) : A4NManipulator(ws, primary);

[ModuleInfo(BossModuleInfo.Maturity.WIP, StatesType = typeof(A4NManipulatorStates), PrimaryActorOID = (uint)OID.BossCandidateB, Contributors = "ffxiv-tc-port", Expansion = BossModuleInfo.Expansion.Heavensward, Category = BossModuleInfo.Category.Raid, GroupType = BossModuleInfo.GroupType.CFC, GroupID = 115u, NameID = 3772u, SortOrder = 1)]
public sealed class A4NManipulatorB(WorldState ws, Actor primary) : A4NManipulator(ws, primary);
