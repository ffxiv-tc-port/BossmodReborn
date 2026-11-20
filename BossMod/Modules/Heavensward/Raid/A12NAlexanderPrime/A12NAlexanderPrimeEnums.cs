namespace BossMod.Heavensward.Raid.A12NAlexanderPrime;

// 首領 OID 沒有錄影：至尊亞歷山大（BNpcName 5193）在本副本地名下有三列、同模型，分不出一般／零式，三個都註冊。
// 契約：列舉＝模組知道的技能；列舉以外的敵方讀條由 Components.UnclaimedCastAOEs 照沒有模組時的猜法畫。
public enum OID : uint
{
    BossCandidateA = 0x17BC,
    BossCandidateB = 0x17C1,
    BossCandidateC = 0x180E,
}

public enum AID : uint
{
    MegaHoly = 6887, // Boss->self, 4.0s cast, range 80 circle, raidwide
    PunishingHeat = 6884, // Boss->player, 4.0s cast, range 5 circle tankbuster
    BlazingScourgeVisual = 6888, // Boss->self, 3.0s cast, single-target
    BlazingScourge = 6889, // Boss->player, no cast, range 4 circle spread (icon 0x1E)
    Sacrament = 6885, // Boss->self, 5.0s cast, single-target, followed by SacramentAOE
    SacramentAOE = 6886, // Boss->self, no cast, range 60 width 16 rect, cross on the boss per guide
    IncineratingHeat = 6899, // Boss->player, 5.0s cast, range 5 circle stack
}

public enum IconID : uint
{
    BlazingScourge = 0x1E, // player
}
