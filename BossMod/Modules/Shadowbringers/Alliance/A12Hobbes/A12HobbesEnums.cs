namespace BossMod.Shadowbringers.Alliance.A12Hobbes;

// 只列有證據的技能：列舉＝模組認領，列進來卻沒有元件的技能就連後備猜測都沒有。
public enum OID : uint
{
    Boss = 0x2C2B, // R25.000
    Helper = 0x233C, // R0.500
}

public enum AID : uint
{
    LaserResistanceTest = 18437, // Boss->self, 4.0s cast, range 50 circle, raidwide

    RingLaserOuter = 18434, // Helper->self, 4.0s cast, range 15-20 donut

    ShockingDischarge = 18443, // Helper->self, 2.0s cast, range 5 circle

    VariableCombatTestCircleFirst = 18886, // Helper->self, 5.7s cast, range 2 circle
    VariableCombatTestCircle = 18448, // Helper->self, 2.0s cast, range 2 circle
    VariableCombatTestDonutFirst = 18887, // Helper->self, 5.7s cast, range 7-19 donut
    VariableCombatTestDonut = 18449, // Helper->self, 2.0s cast, range 7-19 donut
    VariableCombatTestConeFirst = 18885, // Helper->self, 5.7s cast, range 20 60-degree cone
    VariableCombatTestCone = 18447, // Helper->self, 2.0s cast, range 20 60-degree cone

    Impact = 18450, // Helper->self, 7.7s cast, range 20 circle, damage falls off with distance
    Collapse = 18451, // Helper->self, no cast, range 20 width 8 rect, 4 lines from the platform center ~2.4s after Impact

    ShortRangeMissile = 18453, // Helper->players, 8.0s cast, range 8 circle, spread

    LaserLineStackVisual = 18439, // Boss->self, 7.7s cast, single-target
    LaserLineStackMarker = 18440, // Helper->player, no cast, one marked player per platform at the visual's cast start
    LaserLineStack = 18441, // Helper->self, no cast, range 65 width 8 rect, from the hub toward the marked player ~8.1s after the marker

    FireResistanceTestCone = 18454, // Helper->self, no cast, 5 ticks from the next platform's outer wall toward the hub
    FireResistanceTestBand = 18455, // Helper->self, no cast, range 12 width 38 rect, 5 ticks, two walls burning 8.5-20.5y from the platform center
    FireResistanceTestHalf = 18456, // Helper->self, no cast, range 22 width 42 rect, 5 ticks, front half of the platform
}
