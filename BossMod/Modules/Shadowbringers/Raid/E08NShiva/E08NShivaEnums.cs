namespace BossMod.Shadowbringers.Raid.E08NShiva;

// 契約：列舉＝模組知道的技能；列舉以外的敵方讀條由 Components.UnclaimedCastAOEs 照沒有模組時的猜法畫。
public enum OID : uint
{
    Boss = 0x2D73, // R4.130
    FrozenMirror = 0x2DA0, // R1.000
    ChargingAdd = 0x2E13, // 轉場平台上會連線衝撞的小怪
    PlatformHeart = 0x2D86, // 轉場平台中央，每 14 秒放一次心碎（平台全體）
    Helper = 0x233C,
}

public enum AID : uint
{
    AbsoluteZero = 19927, // Boss->self, 5.0s cast, range 40 circle, raidwide
    HeavenlyStrike = 19928, // Boss->self, 5.0s cast, range 40 circle, raidwide + knockback
    DiamondFrost = 19937, // Boss->self, 5.0s cast, range 40 circle, raidwide
    LightRampant = 19944, // Boss->self, 5.0s cast, range 40 circle, raidwide
    ShatteredWorld = 19945, // Boss->self, 10.0s cast, range 40 circle, raidwide
    DoubleSlap = 19930, // Boss->player, 5.0s cast, single-target tankbuster
    ScytheKick = 19939, // Boss->self, 5.0s cast, range 4-20 donut
    EmbitteredDance = 19941, // Boss->self, 5.0s cast, range 4-20 donut, circle follows
    ReflectedScytheKick = 19969, // FrozenMirror->self, 5.0s cast, range 4-20 donut
    ReflectedScytheKickSlow = 19975, // FrozenMirror->self, 10.0s cast, range 4-20 donut
    RedressShining = 20303, // Boss->self, 4.0s cast, single-target, gaze follows
    ShiningArmor = 19953, // Helper->self, no cast, range 40 circle gaze
    FrigidStone = 20070, // Helper->location, no cast, range 5 circle spread
    LightWaveVisual = 19929, // Boss->self, 5.0s cast, single-target
    LightWave = 19979, // Helper->self, no cast, range 40 cone toward each icon 0x17 target
    BrightHunger = 19980, // Helper->location, no cast, range 4 circle tower; 不處理：要站進去的塔，位置由場地狀態決定、只有一場錄影對不出索引
    HeartAsunder = 19986, // PlatformHeart->self, no cast, range 20 circle, whole platform; 不處理：沒有讀條也閃不掉
    ChargeVisual = 20424, // ChargingAdd->self, 5.0s cast, single-target
    Charge = 20425, // ChargingAdd->player, no cast, range 4 circle on the tethered player
}

public enum IconID : uint
{
    FrigidStone = 0x60, // player
    LightWave = 0x17, // player
}

public enum TetherID : uint
{
    Charge = 84, // ChargingAdd->player/PlatformHeart
}
