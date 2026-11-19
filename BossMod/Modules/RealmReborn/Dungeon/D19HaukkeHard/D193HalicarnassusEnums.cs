namespace BossMod.RealmReborn.Dungeon.D19HaukkeHard.D193Halicarnassus;

public enum OID : uint
{
    Boss = 0x973, // R2.000
    LadyAmandine = 0x974, // R1.800, spawns below 40%
    ManorMaidservant = 0x975, // R1.000, x2, spawns below 60%
    ManorSentry = 0x976, // R1.350, spawns below 80%
}

public enum AID : uint
{
    AutoAttack = 870, // Boss/LadyAmandine/ManorMaidservant/ManorSentry->player, no cast
    BloodSword = 1610, // Boss->player, no cast, single-target

    VoidFire2 = 1714, // Boss->location, 2.5s cast, range 5 circle
    VoidFire2Alt = 1723, // Boss->location, 2.5s cast, range 5 circle, not seen in replays
    VoidThunder2 = 1715, // Boss->player, 2.5s cast, single-target
    DarkMist = 1716, // Boss->self, 3.0s cast, range 8+R circle
    VoidFire3 = 1717, // Boss->location, 3.5s cast, range 10 circle
    VoidThunder3 = 1718, // Boss->player, 3.5s cast, single-target
    BeguilingMist = 1719, // Boss->self, 3.2s cast, range 50+R circle, charm (walk towards boss)
    Seduction = 1762, // Boss->add, 2.0s cast, absorbs the add; next Blood Rain scales with the add's remaining HP
    BloodRain1 = 1720, // Boss->location, 2.5s cast, range 100 circle, add absorbed near 0% HP
    BloodRain2 = 1721, // Boss->location, 2.5s cast, range 100 circle, not seen in replays
    BloodRain3 = 1722, // Boss->location, 2.5s cast, range 100 circle, add absorbed near full HP (lethal)

    DarkMistAmandine = 1724, // LadyAmandine->self, 3.0s cast, range 8+R circle
    VoidThunder3Amandine = 1842, // LadyAmandine->player, 4.0s cast, single-target, interruptible
    Fire = 966, // LadyAmandine/ManorMaidservant->player, 1.0s cast, single-target

    VoidFire2Maidservant = 854, // ManorMaidservant->player, 3.0s cast, range 5 circle

    TerrifyingEye = 513, // ManorSentry->self, 3.0s cast, range 6+R 90-degree cone
    Stoneskin = 1008, // ManorSentry->Boss, 2.5s cast, buff
    Stone = 970, // ManorSentry->player, 1.0s cast, single-target
}
