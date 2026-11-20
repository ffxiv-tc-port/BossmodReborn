namespace BossMod.Shadowbringers.Alliance.A11CommandModel;

// 只列有證據的技能：列舉＝模組認領，列進來卻沒有元件的技能就連後備猜測都沒有。
public enum OID : uint
{
    Boss = 0x2C61, // R5.600
    Helper = 0x233C, // R0.500
}

public enum AID : uint
{
    ClangingBlow = 18638, // Boss->player, 4.0s cast, single-target, tankbuster
    ForcefulImpact = 18639, // Boss->self, 4.0s cast, range 100 circle, raidwide

    EnergyBombardment = 18616, // Helper->location, 3.0s cast, range 4 circle

    EnergyAssault = 18613, // Boss->self, 5.0s cast, single-target (lingering frontal cone follows)
    EnergyAssaultEnd = 18960, // Boss->self, no cast, single-target (end of the lingering cone)

    EnergyRingCircle = 18620, // Helper->self, 4.7s cast, range 12 circle
    EnergyRingDonut1 = 18622, // Helper->self, 6.7s cast, range 12-24 donut
    EnergyRingDonut2 = 18624, // Helper->self, 8.7s cast, range 24-36 donut
    EnergyRingDonut3 = 18626, // Helper->self, 10.7s cast, range 36-48 donut

    SidestrikingSpinL = 18635, // Helper->self, 6.3s cast, range 30 width 12 rect
    SidestrikingSpinR = 18636, // Helper->self, 6.3s cast, range 30 width 12 rect
    CentrifugalSpin = 18633, // Helper->self, 6.3s cast, range 30 width 8 rect

    HighCaliberLaser = 18682, // Helper->self, 7.0s cast, range 70 width 24 rect
}
