namespace BossMod.Shadowbringers.Raid.E06NGarudaIfrit;

// 首領 OID 沒有錄影：迦樓羅、伊弗利特、赤翼羅羯坨博叉在 BNpcBase 各有同模型兩列，分不出一般／零式，全部註冊。
// 契約：列舉＝模組知道的技能；列舉以外的敵方讀條由 Components.UnclaimedCastAOEs 照沒有模組時的猜法畫。
public enum OID : uint
{
    GarudaA = 0x2D0E,
    IfritA = 0x2D0F,
    RaktapaksaA = 0x2D10,
    GarudaB = 0x2D13,
    IfritB = 0x2D14,
    RaktapaksaB = 0x2D15,
    Helper = 0x233C,
}

public enum AID : uint
{
    Superstorm = 19415, // Garuda->self, 5.0s cast, range 40 circle, raidwide
    InfernoHowl = 19441, // Ifrit/Raktapaksa->self, 5.0s cast, range 40 circle, raidwide
    Touchdown = 19432, // Ifrit->location, 5.0s cast, range 40 circle, proximity raidwide
    InstantIncineration = 19437, // Ifrit/Raktapaksa->player, 5.0s cast, range 4 circle tankbuster
    AirBump = 19417, // ?->player, 5.5s cast, range 4 circle stack
}
