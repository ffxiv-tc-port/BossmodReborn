namespace BossMod.Shadowbringers.Raid.E12NEdensPromise;

// 首領 OID 沒有錄影：伊甸之約（BNpcName 9813）同模型 2967、Rank 2 的只有這兩列，分不出一般／零式，兩個都註冊。
// 同名的 0x308E（模型 3150、Rank 0、不顯示目標線）不當首領。
// 契約：列舉＝模組知道的技能；列舉以外的敵方讀條由 Components.UnclaimedCastAOEs 照沒有模組時的猜法畫。
public enum OID : uint
{
    BossCandidateA = 0x308D,
    BossCandidateB = 0x3099,
    Helper = 0x233C,
}

public enum AID : uint
{
    Maleficium = 22642, // Boss->self, 4.0s cast, range 40 circle, raidwide
    FormlessJudgment = 22643, // Boss->self, 5.0s cast, single-target, tankbusters follow
    DiamondDust = 22628, // Boss->self, 5.0s cast, single-target, raidwide follows
    EarthenFury = 22634, // Boss->self, 5.0s cast, single-target, raidwide follows
    FrigidStone = 22631, // Boss->player, 5.0s cast in sheet, cast visibility unknown, range 5 circle spread
    FrigidStoneReach = 22633, // Boss->player, 5.0s cast in sheet, cast visibility unknown, range 5 circle spread
    IceFloe = 22632, // Boss->player, 5.0s cast in sheet, cast visibility unknown, range 6 circle stack
    UnderTheWeight = 22640, // Boss->player, 5.0s cast in sheet, cast visibility unknown, range 6 circle stack
    ClassicalSculpture = 22662, // ChiseledSculpture->self, no cast, range 6 circle stack
    Obliteration = 22660, // Helper->player, 8.0s cast, range 40 width 6 rect line stack
    PalmOfTemperance = 22665, // Boss->self, 5.5s cast, range 30 width 30 rect knockback
}

public enum IconID : uint
{
    Stack = 0x3E, // player
    FrigidStone = 0x60, // player
}
