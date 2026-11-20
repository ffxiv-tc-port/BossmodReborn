namespace BossMod.Stormblood.Raid.O9NChaos;

// 契約：列舉＝模組知道的技能（有元件處理，或刻意不處理）；列舉以外的敵方讀條由 Components.UnclaimedCastAOEs 照沒有模組時的猜法畫。
public enum OID : uint
{
    Boss = 0x2435, // x1
    Helper = 0x233C,
    Chaosphere = 0x2437,
}

public enum AID : uint
{
    AutoAttack = 12622, // Boss->player, no cast, single-target
    ChaoticDispersion = 12623, // Boss->player, 5.0s cast, single-target tankbuster
    DamningEdict = 12624, // Boss->self, 5.0s cast, range 60 width 60 rect
    LongitudinalImplosion = 12625, // Boss->self, 4.0s cast, single-target, visual
    LatitudinalImplosion = 12626, // Boss->self, 4.0s cast, single-target, visual
    Shockwave = 12627, // Helper->self, 4.7s cast, range 60 90-degree cone, two pairs staggered by 2s
    ChaosphereVisual1 = 12629, // Boss->self, no cast, single-target
    ChaosphereVisual2 = 12630, // Boss->self, no cast, single-target
    KnockVisual1 = 12631, // Boss->self, 5.0s cast, single-target
    KnockVisual2 = 12632, // Boss->self, 5.0s cast, single-target
    BigBangVisual1 = 12633, // Boss->self, 3.0s cast, single-target
    BigBangVisual2 = 12634, // Boss->self, 5.0s cast, single-target
    FiendishOrbsVisual1 = 12635, // Boss->self, 2.3s cast, single-target
    FiendishOrbsVisual2 = 12636, // Boss->self, 5.0s cast, single-target
    KnockStack = 12637, // Helper->self, no cast, range 6 circle stack (icon 0x3E)
    KnockProximity = 12638, // Helper->self, no cast, range 60 circle, proximity marker (icon 0x57)
    BigBangSmall = 12639, // Helper->self, 3.7s cast, range 6 circle
    BigBangLarge = 12640, // Helper->self, 5.7s cast, range 15 circle
    FiendishOrbsLine = 12641, // Helper->location, 3.0s cast, width 3 rect charge
    FiendishOrbsChase1 = 12642, // Chaosphere->location, no cast, width 3 rect charge
    FiendishOrbsChase2 = 12643, // Chaosphere->location, no cast, width 3 rect charge
    FiendishOrbsTether = 12644, // Chaosphere->player, no cast, single-target
    Blaze = 12645, // Boss->self, 5.0s cast, single-target, raidwide via BlazeRaidwide
    Tsunami = 12646, // Boss->self, 5.0s cast, single-target, raidwide via TsunamiRaidwide
    Cyclone = 12647, // Boss->self, 5.0s cast, single-target, raidwide via CycloneRaidwide
    Earthquake = 12648, // Boss->self, 5.0s cast, single-target, raidwide via EarthquakeRaidwide
    BowelsOfAgony = 12649, // Boss->self, 5.0s cast, range 60 circle
    SoulOfChaos = 12650, // Boss->self, no cast, range 100 circle
    ChaoticFlames = 12651, // Helper->self, no cast, range 5 circle, Entropy expiry
    ChaoticWater = 12652, // Helper->self, no cast, donut, Dynamic Fluid expiry
    CycloneKnockback = 12653, // Helper->self, 5.0s cast, range 30 circle knockback
    EarthquakeDrawIn = 12654, // Helper->self, 5.0s cast, range 40 width 40 rect
    FireTerrain = 12698, // Helper->self, 5.0s cast, range 10-30 donut
    WaterTerrain = 12699, // Helper->self, 5.0s cast, range 15 width 40 rect
    WindTerrain = 12700, // Helper->self, 5.0s cast, range 10 circle
    EarthTerrain = 12701, // Helper->self, 5.0s cast, range 20 width 40 rect
    FireTerrain2 = 12702, // Helper->self, 5.0s cast, range 10-30 donut
    BlazeRaidwide = 12802, // Helper->self, no cast, range 60 circle
    TsunamiRaidwide = 12803, // Helper->self, no cast, range 60 circle
    CycloneRaidwide = 12804, // Helper->self, no cast, range 60 circle
    EarthquakeRaidwide = 12805, // Helper->self, no cast, range 60 circle
    EarthquakeKnockback = 13336, // Helper->self, 5.0s cast, range 40 width 40 rect
}

public enum SID : uint
{
    Entropy = 1600, // none->player, extra=0x0
    DynamicFluid = 1601, // none->player, extra=0x0
}

public enum IconID : uint
{
    KnockStack = 0x3E, // player
    KnockProximity = 0x57, // player
}
