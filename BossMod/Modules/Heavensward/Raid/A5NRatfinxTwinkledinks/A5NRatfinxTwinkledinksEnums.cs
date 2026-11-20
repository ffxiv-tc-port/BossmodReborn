namespace BossMod.Heavensward.Raid.A5NRatfinxTwinkledinks;

// 首領 OID 沒有錄影：奇才 拉特芬克斯（BNpcName 4687）在 BNpcBase 有兩列，分不出一般／零式，兩個都註冊。
// 契約：列舉＝模組知道的技能；列舉以外的敵方讀條由 Components.UnclaimedCastAOEs 照沒有模組時的猜法畫。
public enum OID : uint
{
    BossCandidateA = 0x1596,
    BossCandidateB = 0x14E9,
}

public enum AID : uint
{
    Glupgloop = 5534, // Boss->location, 3.0s cast, range 8 circle (ground-targeted on the marked player)
}

public enum IconID : uint
{
    Glupgloop = 23, // player, orange spread marker before the puddle
}
