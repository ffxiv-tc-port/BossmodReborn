namespace BossMod.Stormblood.Raid.O6NDemonChadarnook;

// 首領 OID 沒有錄影：惡魔查達奴克（BNpcName 7097）在 BNpcBase 有兩列同模型，分不出一般／零式，兩個都註冊。
// 契約：列舉＝模組知道的技能；列舉以外的敵方讀條由 Components.UnclaimedCastAOEs 照沒有模組時的猜法畫。
public enum OID : uint
{
    BossCandidateA = 0x210B,
    BossCandidateB = 0x21D1
}

public enum AID : uint
{
    DemonicShear = 10282, // Boss->player, 3.0s cast, single-target
    DemonicHowl = 10284, // Boss->self, 3.0s cast, range 60 circle
    DemonicStone = 10311 // ?->location, 5.0s cast, range 3 circle, first hit of a chasing AOE
}
