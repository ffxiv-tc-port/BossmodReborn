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

    Impact = 18450, // Helper->self, 7.7s cast, range 20 circle

    ShortRangeMissile = 18453, // Helper->players, 8.0s cast, range 8 circle, spread
}
