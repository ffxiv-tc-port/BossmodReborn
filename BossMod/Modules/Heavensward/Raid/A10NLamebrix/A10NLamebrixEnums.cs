namespace BossMod.Heavensward.Raid.A10NLamebrix;

// 首領 OID 沒有錄影：傭兵雷姆普里克斯（BNpcName 5180）在本副本地名下有兩列、同模型，分不出一般／零式，兩個都註冊。
// 契約：列舉＝模組知道的技能；列舉以外的敵方讀條由 Components.UnclaimedCastAOEs 照沒有模組時的猜法畫。
public enum OID : uint
{
    BossCandidateA = 0x17E3,
    BossCandidateB = 0x1802,
}

public enum AID : uint
{
    GobrushRushgob = 6863, // Boss->player, 5.0s cast, single-target tankbuster
    BombToss = 6862, // Boss->player, 5.0s cast, range 6 circle stack (icon 0x3E)
    CriticalWrath = 6861, // Boss->player, no cast, range 4 circle spread (icon 0x19)
    GobslashSlicetops = 6865, // Boss->self, no cast, range 50 width 4 rect toward the prey target (icon 0x17)
    SingleChargeOut = 6841, // Boss->self, no cast, telegraph for GobspinWhooshdrops ("get out")
    SingleChargeIn = 6840, // Boss->self, no cast, telegraph for GobswipeConklops ("get in")
    GobspinWhooshdrops = 6860, // Boss->self, no cast, range 8 circle
    GobswipeConklops = 6859, // Boss->self, no cast, range 50 circle in the sheet, donut per guide (inner radius unknown)
}

public enum IconID : uint
{
    CriticalWrath = 0x19, // player
    GobslashSlicetops = 0x17, // player
}
