namespace BossMod.Stormblood.Alliance.A14Argath;

// 只列有證據的技能：列舉＝模組認領，列進來卻沒有元件的技能就連後備猜測都沒有。
public enum OID : uint
{
    Boss = 0x1F97, // R5.000
    Helper = 0x18D6, // R0.500
    Heartless = 0x1F98, // R1.000
}

public enum AID : uint
{
    FireIV = 9774, // Boss->self, 4.0s cast, range 80+R circle, raidwide
    DarkUltima = 9760, // Boss->self, 2.5s cast, range 80+R circle, raidwide
    CripplingBlow = 9773, // Boss->player, 3.0s cast, single-target, tankbuster

    CrushWeapon = 10003, // Helper->location, 3.5s cast, range 6 circle
    Soulfix = 9770, // Boss->self, 4.0s cast, range 12 circle
    Heartless = 9778, // Heartless->self, 4.0s cast, range 60+R width 11 rect

    MaskOfLies = 9753, // Boss->self, no cast, single-target
    MaskOfTruth = 9754, // Boss->self, no cast, single-target
    TheWord = 9376, // Helper->self, no cast, range 100 circle, resolves divine commandments
}

public enum IconID : uint
{
    CommandFlee = 123, // player
    CommandTurn = 124, // player
}
