namespace BossMod.Stormblood.Raid.O11NOmega;

// 首領 OID 沒有錄影：歐米茄（BNpcName 7635）同模型兩列（0x247C／0x247E），分不出一般／零式，兩個都註冊。
// 契約：列舉＝模組知道的技能（有元件處理，或刻意不處理）；列舉以外的敵方讀條由 Components.UnclaimedCastAOEs 照沒有模組時的猜法畫。
public enum OID : uint
{
    BossCandidateA = 0x247C,
    BossCandidateB = 0x247E,
}

public enum AID : uint
{
    PeripheralSynthesis = 12912, // Boss->self, 3.0s cast, single-target
    DeltaAttack = 12923, // Boss->self, 30.0s cast, range 100 width 100 rect, duty action check
    FlamethrowerVisual = 12924, // Boss->self, 4.0s cast, single-target
    Flamethrower = 12925, // ?->self, 4.0s cast, range 60 30-degree cone
    BallisticMissile = 12926, // Boss->self, no cast, single-target
    BallisticImpact = 12927, // ?->player, no cast, range 6 circle (icon 0x65)
    StarboardCannon1 = 12929, // Boss->self, 5.0s cast, range 60 210-degree cone
    StarboardCannon2 = 12930, // Boss->self, 3.0s cast, range 60 210-degree cone
    LarboardCannon1 = 12931, // Boss->self, 5.0s cast, range 60 210-degree cone
    LarboardCannon2 = 12932, // Boss->self, 3.0s cast, range 60 210-degree cone
    ElectricSlide = 12933, // Boss->player, 5.0s cast, range 6 circle stack
    AtomicRay = 12934, // Boss->self, 4.0s cast, range 80 circle
    MustardBomb = 12935, // Boss->player, 5.0s cast, range 5 circle tankbuster
    Rush = 13724, // RocketPunch->self, 7.0s cast, range 49 width 15 rect
    Executable = 13736, // LevelChecker->self, 5.0s cast, single-target
    Reformat = 13737, // LevelChecker->self, 3.0s cast, range 10-20 donut
    Reset = 13738, // LevelChecker->self, 3.0s cast, range 12 circle
}

public enum IconID : uint
{
    BallisticImpact = 0x65, // player
}
