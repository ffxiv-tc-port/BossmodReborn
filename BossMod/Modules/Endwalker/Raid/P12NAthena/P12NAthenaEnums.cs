namespace BossMod.Endwalker.Raid.P12NAthena;

// 列舉＝模組認領：列進來的讀條不再交給後備猜法。橫斬 33491（Anthropos 小怪 270° 扇形）刻意不列，交給後備猜法照 EXD 畫。
public enum OID : uint
{
    Boss = 0x3F23, // R10.000
    Helper = 0x233C, // R0.500
    Anthropos = 0x3F24, // R2.000, untargetable (Paradeigma: ray of light / white flame)
    LogouIdea = 0x3F25, // R1.000, untargetable (Paradeigma visual)
    ThymouIdea = 0x3F26, // R1.000, untargetable (Paradeigma visual)
    AnthroposAdd = 0x3F27, // R2.000, targetable adds during Ultima Blade
    SuperchainOrigin = 0x3F28, // R0.500
    SuperchainCircle = 0x3F29, // R0.600, moves to its tethered origin
    SuperchainDonut = 0x3F2A, // R0.600, moves to its tethered origin
    Wing = 0x3F65, // R1.000, trinity of souls wing markers / platform anchors
}

public enum AID : uint
{
    AutoAttack = 34425, // Boss->player, no cast, single-target
    AutoAttackAdd = 34721, // AnthroposAdd->player, no cast, single-target
    TeleportAdd = 33556, // LogouIdea/ThymouIdea->location, no cast, single-target
    TeleportBoss = 33557, // Boss->location, no cast, single-target

    OnTheSoul = 33497, // Boss->self, 5.0s cast, range 60 circle, raidwide
    TheosUltima = 33490, // Boss->self, 7.0s cast, range 60 circle, raidwide

    TrinityOfSoulsTR = 33473, // Boss->self, 10.0s cast, range 60 180-degree cone (first hit, boss's right)
    TrinityOfSoulsTL = 33474, // Boss->self, 10.0s cast, range 60 180-degree cone (first hit, boss's left)
    TrinityOfSoulsMR = 33475, // Boss->self, no cast, range 60 180-degree cone (second hit, right)
    TrinityOfSoulsML = 33476, // Boss->self, no cast, range 60 180-degree cone (second hit, left)
    TrinityOfSoulsBR = 33477, // Boss->self, no cast, range 60 180-degree cone (last hit, right)
    TrinityOfSoulsBL = 33478, // Boss->self, no cast, range 60 180-degree cone (last hit, left)

    Paradeigma = 33479, // Boss->self, 3.0s cast, single-target, visual (spawn ideas)
    RayOfLight1 = 33480, // Anthropos->self, 5.0s cast, range 60 width 10 rect
    RayOfLight2 = 33492, // AnthroposAdd->self, 4.0s cast, range 60 width 10 rect
    WhiteFlameVisual = 33481, // Anthropos->self, no cast, single-target
    WhiteFlame = 33482, // Helper->self, no cast, range 100 width 4 rect, towards tethered player

    SuperchainTheory = 33468, // Boss->self, 5.0s cast, single-target, visual (spawn chains)
    SuperchainBurst = 33469, // Helper->self, 1.0s cast, range 7 circle
    SuperchainCoil = 33470, // Helper->self, 1.0s cast, range 6-70 donut

    UnnaturalEnchainment = 33471, // Boss->self, 7.0s cast, single-target, visual (chain platforms)
    Sample = 33472, // Helper->self, no cast, range 10 width 20 rect, chained platform breaks (instant death)

    Glaukopis = 33493, // Boss->player, 5.0s cast, range 60 width 5 rect, tankbuster
    DialogosStack = 33494, // Boss->player, 5.0s cast, range 6 circle stack
    DialogosSpread = 33495, // Helper->player, 5.0s cast, range 6 circle spread
    Parthenos = 33496, // Boss->self, 7.0s cast, range 120 width 16 rect, knockback 10

    UltimaBladeVisual = 33483, // Boss->self, no cast, single-target
    UltimaBlade = 33484, // Helper->self, no cast, range 60 circle, raidwide ~7.5s after visual
    PalladionVisual = 33485, // Boss->self, 8.0s cast, single-target (start jumps)
    PalladionJump = 33486, // Boss->location, no cast, range 6 circle on marked player
    PalladionShockwave = 33487, // Helper->players, no cast, width 4 rect from boss to jump target
    PalladionEnd = 33488, // Boss->self, no cast, single-target
    PalladionPlatforms = 33489, // Helper->self, 5.8s cast, range 20 width 40 rect (breaks middle four platforms)
}

public enum IconID : uint
{
    WingTLFirst = 421, // Wing
    WingTRFirst = 422, // Wing
    WingML = 423, // Wing
    WingMR = 424, // Wing
    WingBL = 433, // Wing
    WingBR = 434, // Wing
    Glaukopis = 471, // player
    Spread = 139, // player (Palladion jump target, Dialogos spread)
    Stack = 62, // player (Dialogos stack)
}

public enum TetherID : uint
{
    WhiteFlame = 17, // Anthropos->player
    SuperchainCircle = 228, // SuperchainCircle->SuperchainOrigin
    SuperchainDonut = 229, // SuperchainDonut->SuperchainOrigin
    UnnaturalEnchainment = 232, // Wing->Boss (chained platform)
}
