namespace BossMod.Shadowbringers.Raid.E09NCloudOfDarkness;

// 首領 OID 沒有錄影：黑暗之雲（BNpcName 9764）同模型 3022 的只有這兩列，分不出一般／零式，兩個都註冊。
// 契約：列舉＝模組知道的技能；列舉以外的敵方讀條由 Components.UnclaimedCastAOEs 照沒有模組時的猜法畫。
public enum OID : uint
{
    BossCandidateA = 0x3056,
    BossCandidateB = 0x305B,
    Helper = 0x233C,
}

public enum AID : uint
{
    GroundRazingParticleBeam = 21997, // Boss->self, 5.0s cast, single-target, raidwide follows
    EmptyPlane = 20422, // Boss->self, 5.0s cast, range 60 circle, raidwide
    ObscureWoods = 20386, // Boss->self, 5.0s cast, range 60 circle, raidwide
    DelugeOfDarkness = 20821, // Boss->self, 5.0s cast, single-target, raidwide follows
}
