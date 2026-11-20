namespace BossMod.Endwalker.Raid.P5NProtoCarbuncle;

// 只列有證據（台服 Action 表的目標種類＋機制攻略）的技能：列舉＝模組認領，列進來卻沒有元件的技能就連後備猜測都沒有。
// 沒有錄影：施放者（王或 Helper）未證實，以「?」標示。
public enum OID : uint
{
    Boss = 0x39E6, // same ModelChara as savage 0x39EA
    Helper = 0x233C,
}

public enum AID : uint
{
    RubyGlow = 30420, // ?->self, 5.0s cast, single-target, visual for the raidwide
    SonicHowl = 30450, // ?->self, 5.0s cast, range 60 circle, raidwide

    Crunch = 30448, // ?->player, 5.0s cast, single-target, tankbuster
    ToxicCrunch = 30449, // ?->player, 5.0s cast, single-target, tankbuster

    VenomRain = 30445, // ?->player, 5.0s cast, range 5 circle, spread
    VenomPool = 30447, // ?->player, 8.0s cast, range 6 circle, stack on one player
}
