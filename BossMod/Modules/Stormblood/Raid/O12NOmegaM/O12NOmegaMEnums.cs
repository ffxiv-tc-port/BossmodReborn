namespace BossMod.Stormblood.Raid.O12NOmegaM;

// 首領 OID 沒有錄影：歐米茄M（BNpcName 7633）同模型兩列（0x247B／0x247D），分不出一般／零式，兩個都註冊。
// 歐米茄F 的 OID 沒有來源，不註冊；元件不分施放者，F 放的技能照樣處理。
// 契約：列舉＝模組知道的技能（有元件處理，或刻意不處理）；列舉以外的敵方讀條由 Components.UnclaimedCastAOEs 照沒有模組時的猜法畫。
public enum OID : uint
{
    BossCandidateA = 0x247B,
    BossCandidateB = 0x247D,
}

public enum AID : uint
{
    SubjectSimulationF = 13041, // Boss->self, 2.0s cast, single-target
    EfficientBladeworkHidden = 13043, // Boss->self, no cast, range 10 circle, after Subject Simulation M
    SubjectSimulationM = 13044, // Boss->self, 2.0s cast, single-target
    SyntheticShield = 13053, // Boss->self, 2.0s cast, single-target
    EfficientBladework = 13055, // Boss->self, 4.0s cast, range 10 circle
    BeyondStrength = 13056, // Boss->self, 4.0s cast, range 10-40 donut
    SyntheticBlades = 13057, // Boss->self, 2.0s cast, single-target
    OptimizedBlizzard = 13059, // ?->self, 3.0s cast, range 40 width 10 cross
    SuperliminalSteelVisual = 13061, // ?->self, 3.0s cast, single-target
    SuperliminalSteel1 = 13062, // ?->self, 3.0s cast, range 80 width 36 rect
    SuperliminalSteel2 = 13063, // ?->self, 3.0s cast, range 80 width 36 rect
    ProgramAlpha = 13064, // Boss->self, 3.0s cast, single-target
    Floodlight = 13065, // ?->location, 3.0s cast, range 6 circle
    Spotlight = 13066, // ?->player, 5.0s cast, range 6 circle stack
    OptimizedFireVisual = 13069, // ?->self, 3.0s cast, single-target
    OptimizedFire = 13070, // ?->player, 5.0s cast, range 5 circle spread
    SolarRay1 = 13071, // ?->player, 5.0s cast, single-target tankbuster
    SolarRay2 = 13072, // ?->player, 5.0s cast, single-target tankbuster
    LaserShower1 = 13073, // ?->self, 4.0s cast, range 100 circle
    LaserShower2 = 13074, // ?->self, 4.0s cast, range 100 circle
    CosmoMemory1 = 13083, // ?->self, 5.0s cast, single-target
    CosmoMemory2 = 13084, // ?->self, 5.0s cast, single-target
    OpticalLaser = 13088, // OpticalUnit->self, 5.0s cast, range 100 width 16 rect
    OptimizedBladeDance1 = 13089, // ?->player, 5.0s cast, single-target tankbuster
    OptimizedBladeDance2 = 13090, // ?->player, 5.0s cast, single-target tankbuster
    OptimizedSagittariusArrow = 13091, // ?->self, 5.0s cast, range 100 width 10 rect
}
