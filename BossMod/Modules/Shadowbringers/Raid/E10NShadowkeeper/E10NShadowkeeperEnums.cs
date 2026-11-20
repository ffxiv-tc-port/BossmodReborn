namespace BossMod.Shadowbringers.Raid.E10NShadowkeeper;

// 首領 OID 沒有錄影：影之王（BNpcName 9769）同模型 3119 的只有這兩列，分不出一般／零式，兩個都註冊。
// 契約：列舉＝模組知道的技能；列舉以外的敵方讀條由 Components.UnclaimedCastAOEs 照沒有模組時的猜法畫。
public enum OID : uint
{
    BossCandidateA = 0x3061,
    BossCandidateB = 0x3068,
    Helper = 0x233C,
}

public enum AID : uint
{
    DeepshadowNova = 22245, // Boss->self, 5.0s cast, single-target, raidwide follows
    VoidPulse = 22238, // Boss->self, 4.0s cast, range 100 circle, raidwide
}
