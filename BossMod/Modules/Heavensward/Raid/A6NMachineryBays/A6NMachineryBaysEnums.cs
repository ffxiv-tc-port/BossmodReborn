namespace BossMod.Heavensward.Raid.A6NMachineryBays;

// 首領 OID 沒有錄影：四隻機工兵在 Garland 各有兩列屬於這一章（另外兩列屬於律動之章4），分不出一般／零式，全部註冊。
// 契約：列舉＝模組知道的技能；列舉以外的敵方讀條由 Components.UnclaimedCastAOEs 照沒有模組時的猜法畫。
public enum OID : uint
{
    BlasterA = 0x15CB,
    BlasterB = 0x1511,
    BrawlerA = 0x15CE,
    BrawlerB = 0x1516,
    SwindlerA = 0x15CF,
    SwindlerB = 0x151B,
    VortexerA = 0x15D0,
    VortexerB = 0x1521,
}

public enum AID : uint
{
    Enumeration = 5918, // Swindler->self, no cast, range 5 counted stack on each marked player
    BioArithmeticks = 5919, // Swindler->self, 3.0s cast, range 80 circle, raidwide
    SuperCyclone = 5928, // Vortexer->self, 3.0s cast, range 80 circle, raidwide + knockback (distance unknown)
}

public enum IconID : uint
{
    Enumeration2 = 64, // player, exactly 2 players inside
    Enumeration3 = 65, // player, exactly 3 players inside
    Enumeration4 = 66, // player, exactly 4 players inside
}
