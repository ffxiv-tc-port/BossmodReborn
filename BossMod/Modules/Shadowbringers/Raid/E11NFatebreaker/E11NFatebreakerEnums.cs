namespace BossMod.Shadowbringers.Raid.E11NFatebreaker;

// 首領 OID 沒有錄影：絕命戰士（BNpcName 9707）同模型 3021 的本體只有這兩列，分不出一般／零式，兩個都註冊。
// 契約：列舉＝模組知道的技能；列舉以外的敵方讀條由 Components.UnclaimedCastAOEs 照沒有模組時的猜法畫。
public enum OID : uint
{
    BossCandidateA = 0x3070,
    BossCandidateB = 0x3077,
    Helper = 0x233C,
}

public enum AID : uint
{
    BurnishedGlory = 22096, // Boss->self, 5.0s cast, range 40 circle, raidwide
    PowderMark = 22094, // Boss->player, 5.0s cast, single-target tankbuster, applies Powder Mark
    BurnMark = 22095, // Boss->self, no cast, range 10 circle around the Powder Mark holder
    Blastburn = 22061, // Boss->self, 10.0s cast, range 80 width 50 rect knockback, no omen
    BlastburnImage = 22084, // FatebreakersImage->self, 10.0s cast, range 80 width 50 rect knockback, no omen
    BoundOfFaithFire = 19224, // Boss->self, 8.0s cast, single-target, tethered player gets a stack
    BoundOfFaithHoly = 19227, // Boss->self, 8.0s cast, single-target, tethered player gets a spread
    Sinsmoke = 22067, // Boss->player, no cast, range 6 circle stack
    Sinsight = 22071, // Boss->player, no cast, range 6 circle spread
}

public enum SID : uint
{
    PowderMark = 2451, // Boss->player, explodes on expiry
}

public enum TetherID : uint
{
    BoundOfFaithA = 0x2, // Boss->player
    BoundOfFaithB = 0x5, // Boss->player
    BoundOfFaithC = 0x6, // Boss->player
}
