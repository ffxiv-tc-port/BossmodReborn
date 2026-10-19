namespace BossMod.Heavensward.Trial.T07Zurvan;

public enum OID : uint
{
    Boss = 0x1996, // 第一階段，R7.44
    BossP2 = 0x1997, // 第二階段，R5.27；開場就在場上、不可選取
}

// 只管場地：沒有 AID 列舉＝模組不認領任何技能，敵方讀條全部由 UnclaimedCastAOEs 照沒有模組時的猜法處理。
class T07ZurvanStates : StateMachineBuilder
{
    public T07ZurvanStates(BossModule module) : base(module)
    {
        TrivialPhase()
            .ActivateOnEnter<ZurvanFloor>()
            .ActivateOnEnter<Components.UnclaimedCastAOEs>();
    }
}

[ModuleInfo(BossModuleInfo.Maturity.Contributed, Contributors = "ffxiv-tc-port", GroupType = BossModuleInfo.GroupType.CFC, GroupID = 223, NameID = 5567)]
public class T07Zurvan(WorldState ws, Actor primary) : ZurvanModule(ws, primary, (uint)OID.BossP2);
