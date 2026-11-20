namespace BossMod.Heavensward.Raid.A11NCruiseChaser;

// 首領 OID 沒有錄影：巡航驅逐者（BNpcName 5186）在本副本地名下有兩列、同模型，分不出一般／零式，兩個都註冊。
// 一般難度的讀條（0x1A4D～0x1A64）都有讀條預兆或矩形，後備猜法畫得對，列舉刻意留空；
// 螺旋槳強風要躲在場上的圓形物件後（遮蔽物位置沒有證據）、百式聚能砲是距離衰減（沒有固定半徑），都不畫。
// 契約：列舉＝模組知道的技能；列舉以外的敵方讀條由 Components.UnclaimedCastAOEs 照沒有模組時的猜法畫。
public enum OID : uint
{
    BossCandidateA = 0x17A5,
    BossCandidateB = 0x17EA,
}

public enum AID : uint
{
}
