namespace BossMod.Shadowbringers.Raid.E03NLeviathan;

// 首領 OID 沒有錄影：利維坦（BNpcName 8486）同模型 2527 的只有這兩列，分不出一般／零式，兩個都註冊。
// 契約：列舉＝模組知道的技能；列舉以外的敵方讀條由 Components.UnclaimedCastAOEs 照沒有模組時的猜法畫。
public enum OID : uint
{
    BossCandidateA = 0x296B,
    BossCandidateB = 0x296C,
    Helper = 0x233C,
}

public enum AID : uint
{
    TidalRoar = 16324, // Boss->self, 4.0s cast, single-target, raidwide follows
    Tsunami = 16340, // Boss->self, 6.0s cast, single-target, raidwide follows
    RipCurrent = 16327, // Boss->player, no cast, range 50 width 6 rect tankbuster
    FreakWave = 16331, // ?->player, 6.0s cast, range 5 circle spread
    KillerWave = 16332, // ?->player, 6.0s cast, range 6 circle stack
    TidalWave = 16339, // ?->self, 10.0s cast, range 60 width 60 rect knockback
}

public enum IconID : uint
{
    RipCurrent = 0x17, // player
}
