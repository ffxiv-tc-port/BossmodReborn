namespace BossMod.Shadowbringers.Alliance.A14WalkingFortress;

// 只列有證據的技能：列舉＝模組認領，列進來卻沒有元件的技能就連後備猜測都沒有。
public enum OID : uint
{
    Boss = 0x2C74, // R6.500
    Helper = 0x233C, // R0.500
    GoliathTank = 0x2C77, // R9.600, spawn during fight
    Marx = 0x2C78, // R12.000, spawn during fight
    SerialJointedServiceModel = 0x2C76, // R3.360, spawn during fight
}

public enum AID : uint
{
    Neutralization = 18677, // Boss->player, 4.0s cast, single-target, tankbuster
    LaserSaturation = 18678, // Boss->self, 4.0s cast, range 85 circle, raidwide
    LaserTurret = 19060, // Helper->self, 4.3s cast, range 90 width 8 rect

    GroundToGroundMissile = 18804, // Helper->location, 3.5s cast, range 6 circle
    BallisticImpact = 18681, // Helper->players, 5.0s cast, range 6 circle, spread

    LaserSuppression = 18656, // Helper->self, 5.0s cast, range 60 cone (Fore-Hind / Dual-Flank Cannons)
    MarxImpact = 18644, // Marx->self, 5.0s cast, range 22 circle

    DivebombLane1 = 18649, // Helper->self, 1.0s cast, range 60 width 20 rect
    DivebombLane2 = 18650, // Helper->self, 3.5s cast, range 60 width 20 rect
    DivebombLane3 = 18651, // Helper->self, 6.0s cast, range 60 width 20 rect

    ConvenientSelfDestructionLarge = 18664, // GoliathTank->self, 10.0s cast, range 85 circle, line of sight
    ConvenientSelfDestructionSmall = 18665, // GoliathTank->self, 8.0s cast, range 22 circle

    ClangingBlow = 18672, // SerialJointedServiceModel->player, 4.0s cast, single-target, tankbuster
    ShrapnelImpact = 18675, // Helper->players, 5.0s cast, range 6 circle, stack
    CentrifugalSpin = 19077, // Helper->self, 6.3s cast, range 30 width 8 rect
}

public enum SID : uint
{
    Invincibility = 775 // none->Boss, extra=0x0, while Goliath Tanks are tethered to it
}
