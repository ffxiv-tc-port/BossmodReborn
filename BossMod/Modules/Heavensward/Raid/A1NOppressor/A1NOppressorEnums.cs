namespace BossMod.Heavensward.Raid.A1NOppressor;

// 首領 OID 沒有錄影：壓迫者（BNpcName 3747）／壓迫者零號（3748）在 BNpcBase 各有兩列同模型，分不出一般／零式，兩組都註冊。
// 契約：列舉＝模組知道的技能；列舉以外的敵方讀條由 Components.UnclaimedCastAOEs 照沒有模組時的猜法畫。
public enum OID : uint
{
    BossCandidateA = 0x1162,
    BossCandidateB = 0xEC0,
    OppressorZeroFiveA = 0x1163,
    OppressorZeroFiveB = 0xEC1,
    FaustA = 0x1160,
    FaustB = 0xEBE,
}

public enum AID : uint
{
    ThreeThousandTonzeMissile = 4384, // Boss->self, 3.0s cast, raidwide (damage lands with 4385, range 80 circle, no cast)
}
