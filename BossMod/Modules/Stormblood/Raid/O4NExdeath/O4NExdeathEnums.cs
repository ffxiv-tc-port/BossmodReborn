namespace BossMod.Stormblood.Raid.O4NExdeath;

// 首領 OID 沒有錄影：艾克斯迪司（BNpcName 6052）同模型 326、Rank 2 的只有這兩列，分不出一般／零式，兩個都註冊。
// 同名的 0x1D20／0x1E78（模型 1888、Rank 0）不當首領。
// 契約：列舉＝模組知道的技能；列舉以外的敵方讀條由 Components.UnclaimedCastAOEs 照沒有模組時的猜法畫。
public enum OID : uint
{
    BossCandidateA = 0x1D1F,
    BossCandidateB = 0x1E77,
}

public enum AID : uint
{
    Meteor = 9414, // Boss->self, 7.0s cast, range 80 circle, raidwide with proximity falloff
    ThunderIII = 9405, // Boss->self, 5.0s cast, single-target, tankbuster follows
    FireIIISpread = 9402, // Boss->player, 4.0s cast, range 4 circle spread
    EmpoweredFire = 9407, // Boss->self, 4.0s cast, range 80 circle, applies Pyretic
    EmpoweredBlizzard = 9408, // Boss->self, 4.0s cast, range 80 circle, freezes players standing still
    Holy = 9413, // Boss->player, 5.0s cast in sheet, range 6 circle stack
}

public enum SID : uint
{
    Doom = 910, // Boss->player
    Pyretic = 960, // Boss->player
}

public enum IconID : uint
{
    Holy = 0x3E, // player
}
