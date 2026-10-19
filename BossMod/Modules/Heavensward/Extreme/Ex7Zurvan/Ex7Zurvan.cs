namespace BossMod.Heavensward.Extreme.Ex7Zurvan;

public enum OID : uint
{
    Boss = 0x1930, // 第一階段，R7.44
    BossP2 = 0x1931, // 第二階段，R5.27；開場就在場上、不可選取
}

// 只管場地（與普通版共用 ZurvanFloor）：沒有 AID 列舉＝模組不認領任何技能，敵方讀條全部由 UnclaimedCastAOEs 照沒有模組時的猜法處理。
class Ex7ZurvanStates : StateMachineBuilder
{
    public Ex7ZurvanStates(BossModule module) : base(module)
    {
        TrivialPhase()
            .ActivateOnEnter<Trial.T07Zurvan.ZurvanFloor>()
            .ActivateOnEnter<Components.UnclaimedCastAOEs>();
    }
}

[ModuleInfo(BossModuleInfo.Maturity.Contributed, Contributors = "ffxiv-tc-port", GroupType = BossModuleInfo.GroupType.CFC, GroupID = 224, NameID = 5567)]
public class Ex7Zurvan(WorldState ws, Actor primary) : Trial.T07Zurvan.ZurvanModule(ws, primary, (uint)OID.BossP2);
