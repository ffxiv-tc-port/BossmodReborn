namespace BossMod.Stormblood.Raid.O7NGuardian;

// 首領 OID 沒有錄影：守護者（BNpcName 7107）在 BNpcBase 有兩列同模型，分不出一般／零式，兩個都註冊。
// 契約：列舉＝模組知道的技能；列舉以外的敵方讀條由 Components.UnclaimedCastAOEs 照沒有模組時的猜法畫。
public enum OID : uint
{
    BossCandidateA = 0x20CB,
    BossCandidateB = 0x20D1
}

public enum AID : uint
{
    Shockwave = 10086, // Boss->self, 2.0s cast, range 60 circle, knockback
    MagitekRay = 10091, // Boss->self, 3.0s cast, range 40 width 6 rect
    ArmAndHammer = 10092, // Boss->player, 4.0s cast, single-target
    DiffractivePlasma = 10094, // Boss->self, 4.0s cast, range 60 circle
    MainCannon = 10097 // Boss->location, 3.0s cast, range 60 circle, damage falloff
}
