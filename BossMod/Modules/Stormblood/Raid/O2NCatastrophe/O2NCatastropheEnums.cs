namespace BossMod.Stormblood.Raid.O2NCatastrophe;

// 首領 OID 沒有錄影：災變者（BNpcName 5631）同模型 1755、Rank 2 的這個版本只有這兩列，分不出一般／零式，兩個都註冊。
// 契約：列舉＝模組知道的技能；列舉以外的敵方讀條由 Components.UnclaimedCastAOEs 照沒有模組時的猜法畫。
public enum OID : uint
{
    BossCandidateA = 0x1C89,
    BossCandidateB = 0x1E5D,
}

public enum AID : uint
{
    GravitationalWave = 9488, // Boss->self, 3.0s cast, range 50 circle, raidwide
    Evilsphere = 9487, // Boss->player, 5.0s cast, single-target tankbuster
    DemonEye = 9485, // Boss->self, 5.0s cast, range 50 circle gaze
    Earthquake = 9490, // Boss->self, 5.0s cast, single-target, hits everyone not levitating
    GravitationalExplosion = 9478, // Boss->player, no cast, range 7 circle stack
    GravitationalExplosionAlt = 9479, // Boss->player, no cast, range 7 circle stack
}

public enum IconID : uint
{
    GravitationalExplosion = 0x71, // player
}
