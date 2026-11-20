namespace BossMod.Endwalker.Raid.P7NAgdistis;

// 只列有證據的技能：列舉＝模組認領，列進來卻沒有元件的技能就連後備猜測都沒有。
public enum OID : uint
{
    Boss = 0x3A28,
    Helper = 0x233C,
}

public enum AID : uint
{
    SparkOfLife = 30731, // Boss->self, 5.0s cast, range 60 circle, raidwide

    BoughOfAttisOutAOE = 30714, // ?->self, 8.0s cast, range 19 circle (front platforms)
    BoughOfAttisSideAOE = 30717, // ?->self, 5.0s cast, range 50 width 25 rect (hand sweep)
    BoughOfAttisInAOE = 30719, // ?->self, 8.0s cast, range 25 circle (back platform)

    HemitheosHoly = 30728, // ?->player, 6.0s cast, range 6 circle, spread
    HemitheosAeroII = 30730, // ?->player, 5.4s cast, range 8 circle, tankbuster on both tanks

    StaticMoon = 30722, // ImmatureIo->self, 5.0s cast, range 10 circle
    StymphalianStrike = 30723, // ImmatureStymphalide->self, 5.0s cast, range 60 width 8 rect

    BladesOfAttisFirst = 30725, // ?->self, 5.0s cast, range 7 circle (first hit of the moving line)
}
