namespace BossMod.Endwalker.Raid.P4NHesperos;

// 只列有證據（台服 Action 表的目標種類＋機制攻略）的技能：列舉＝模組認領，列進來卻沒有元件的技能就連後備猜測都沒有。
// 沒有錄影：施放者（王或 Helper）未證實，以「?」標示。
public enum OID : uint
{
    Boss = 0x3602, // same ModelChara as savage 0x35FD
    Helper = 0x233C,
}

public enum AID : uint
{
    Bloodrake = 27200, // ?->self, 5.0s cast, range 40 circle, raidwide
    Decollation = 27217, // ?->self, 5.0s cast, range 40 circle, raidwide

    ElegantEvisceration = 27216, // ?->player, 5.0s cast, range 5 circle, tankbuster cleave

    AcidMekhane = 27192, // ?->player, 5.0s cast, range 5 circle, spread (Acid Pinax)
    LavaMekhane = 27193, // ?->player, 5.0s cast, range 6 circle, stack on one player (Lava Pinax)
}
