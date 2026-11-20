namespace BossMod.Heavensward.Raid.A3NLivingLiquid;

// 首領 OID 沒有錄影：有生命活水（BNpcName 3765）在本副本地名下有兩列，分不出一般／零式，兩個都註冊。
// 契約：列舉＝模組知道的技能；列舉以外的敵方讀條由 Components.UnclaimedCastAOEs 照沒有模組時的猜法畫。
public enum OID : uint
{
    BossCandidateA = 0x132D,
    BossCandidateB = 0xF5E,
}

public enum AID : uint
{
    Cascade = 4855, // Boss->self, 4.0s cast, range 80 circle, raidwide
    Sluice = 4851, // Boss->player, no cast, range 5 circle, spread on icon 001A
    Drainage = 4868, // Boss->player, no cast, range 6 circle, on the tethered player
}

public enum IconID : uint
{
    Sluice = 0x1A, // player
}

public enum TetherID : uint
{
    Drainage = 0x5, // player->Boss
}
