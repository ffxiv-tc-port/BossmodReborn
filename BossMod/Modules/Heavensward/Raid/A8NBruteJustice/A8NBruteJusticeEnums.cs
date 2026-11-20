namespace BossMod.Heavensward.Raid.A8NBruteJustice;

// 首領 OID 沒有錄影：每隻在 Garland 區域 1731（這一章）各有兩列，分不出一般／零式，全部註冊。
// 突擊者打完會離場、機工兵兩兩登場、最後合體成殘暴正義號：每一隻都能當主角，哪一隻先開打就由它帶整場。
// 契約：列舉＝模組知道的技能；列舉以外的敵方讀條由 Components.UnclaimedCastAOEs 照沒有模組時的猜法畫。
public enum OID : uint
{
    OnslaughterA = 0x15D4,
    OnslaughterB = 0x1525,
    BruteJusticeA = 0x15DD,
    BruteJusticeB = 0x1531,
    BlasterA = 0x15D8,
    BlasterB = 0x1529,
    BrawlerA = 0x15DC,
    BrawlerB = 0x152D,
    SwindlerA = 0x15DB,
    SwindlerB = 0x152C,
    VortexerA = 0x15DA,
    VortexerB = 0x152B,
}

public enum AID : uint
{
    PerpetualRay = 5936, // Onslaughter->player, 4.0s cast, single-target tankbuster
    Enumeration = 5954, // Swindler->self, no cast, range 5 counted stack on each marked player
    SuperCyclone = 5959, // Vortexer->self, 3.0s cast, range 80 circle, raidwide + knockback (distance unknown)
    DoubleRocketPunch = 5966, // BruteJustice->player, 4.0s cast, range 3 circle, shared by both tanks
    SuperJump = 5968, // BruteJustice->player, 4.0s cast, range 10 circle on the target
    ApocalypticRay = 5969, // BruteJustice->self, 3.0s cast, starts the persistent frontal ray
    ApocalypticRayHit = 5970, // BruteJustice->self, no cast, range 25 cone (no omen), repeated
    LongNeedleStack = 5973, // BruteJustice->player, no cast, range 6 circle stack
}

public enum IconID : uint
{
    Enumeration2 = 64, // player, exactly 2 players inside
    Enumeration3 = 65, // player, exactly 3 players inside
    Enumeration4 = 66, // player, exactly 4 players inside
    LongNeedle = 62, // player, stack marker
}
