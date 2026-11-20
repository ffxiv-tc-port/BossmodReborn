namespace BossMod.Heavensward.Raid.A9NRefurbisher;

// 首領 OID 沒有錄影：廢品翻新裝置（BNpcName 5170）在 BNpcBase 只有這兩列、同模型，分不出一般／零式，兩個都註冊。
// 一般難度的技能都有讀條預兆，後備猜法畫得對，列舉刻意留空；點名分攤、遮蔽、熔岩區的 ID 與時機沒有一般難度的證據。
// 契約：列舉＝模組知道的技能；列舉以外的敵方讀條由 Components.UnclaimedCastAOEs 照沒有模組時的猜法畫。
public enum OID : uint
{
    BossCandidateA = 0x17D9,
    BossCandidateB = 0x18CD,
}

public enum AID : uint
{
}
