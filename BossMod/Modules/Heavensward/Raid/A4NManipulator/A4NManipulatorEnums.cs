namespace BossMod.Heavensward.Raid.A4NManipulator;

// 首領 OID 沒有錄影：操縱者（BNpcName 3772）在 BNpcBase 只有這兩列、同模型，分不出一般／零式，兩個都註冊。
// 一般難度的技能 ID 沒有直接證據：列舉只放「致命進化」兩個讀條 6 秒的列，一般難度沒用到時元件就不會觸發。
// 契約：列舉＝模組知道的技能；列舉以外的敵方讀條由 Components.UnclaimedCastAOEs 照沒有模組時的猜法畫。
public enum OID : uint
{
    BossCandidateA = 0xF34,
    BossCandidateB = 0xF3E,
}

public enum AID : uint
{
    MortalRevolution1 = 5095, // Boss->self, 6.0s cast, range 10 circle in the sheet, raidwide per savage timeline
    MortalRevolution2 = 3932, // Boss->self, 6.0s cast, CastType 6 range 50, raidwide
}
