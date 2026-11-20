namespace BossMod.Endwalker.Raid.P6NHegemone;

// 只列有證據的技能：列舉＝模組認領，列進來卻沒有元件的技能就連後備猜測都沒有。
public enum OID : uint
{
    Boss = 0x3A35,
    Helper = 0x233C,
}

public enum AID : uint
{
    HemitheosDark = 30798, // Boss->self, 5.0s cast, range 60 circle, raidwide

    ChorosIxouAOE1 = 30809, // ?->self, 5.0s cast, range 40 90-degree cone
    ChorosIxouAOE2 = 30810, // ?->self, 5.0s cast, range 40 90-degree cone

    SynergyAOE1 = 30812, // ?->player, 5.0s cast, range 5 circle, tankbuster
    SynergyAOE2 = 30813, // ?->player, 5.0s cast, range 5 circle, tankbuster

    DarkAshesAOE = 30815, // ?->player, 8.0s cast, range 6 circle, spread

    ReekHavoc = 31213, // Parasitos->self, 3.4s cast, range 60 30-degree cone (from infected players)

    StropheIxouAOE1 = 31252, // ?->self, 5.0s cast, range 40 45-degree cone (first hit of the rotation)
    StropheIxouAOE2 = 31253, // ?->self, 5.0s cast, range 40 45-degree cone (first hit of the rotation)
}
