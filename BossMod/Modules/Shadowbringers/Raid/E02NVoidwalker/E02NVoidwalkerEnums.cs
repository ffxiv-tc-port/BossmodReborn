namespace BossMod.Shadowbringers.Raid.E02NVoidwalker;

// 首領 OID 沒有錄影：虛無行者（BNpcName 8382）同模型 2627 的只有這兩列，分不出一般／零式，兩個都註冊。
// 同名的 0x2912／0x2913 是另一個模型、體型也不同，不當首領。
// 契約：列舉＝模組知道的技能；列舉以外的敵方讀條由 Components.UnclaimedCastAOEs 照沒有模組時的猜法畫。
public enum OID : uint
{
    BossCandidateA = 0x290C,
    BossCandidateB = 0x290F,
    Helper = 0x233C,
}

public enum AID : uint
{
    Entropy = 15982, // ?->self, 6.5s cast, range 50 circle, raidwide
    Shadowflame = 15949, // Boss->player, 4.0s cast, single-target tankbuster
    EmptyHate = 15942, // TheHandOfErebos->self, 6.0s cast, range 50 width 40 rect knockback
    DarkFireIII = 15939, // Boss->player, no cast, range 8 circle spread
    UnholyDarkness = 15936, // Boss->player, no cast, range 6 circle stack
}

public enum IconID : uint
{
    DarkFireIII = 0x4C, // player
    UnholyDarkness = 0x3E, // player
}
