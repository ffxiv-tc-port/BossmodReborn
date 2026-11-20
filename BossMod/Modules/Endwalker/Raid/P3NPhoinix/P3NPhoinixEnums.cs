namespace BossMod.Endwalker.Raid.P3NPhoinix;

// 只列有證據（台服 Action 表的目標種類＋機制攻略）的技能：列舉＝模組認領，列進來卻沒有元件的技能就連後備猜測都沒有。
// 沒有錄影：施放者（王或 Helper）未證實，以「?」標示。
public enum OID : uint
{
    Boss = 0x353B, // same ModelChara as savage 0x353F
    Helper = 0x233C,
}

public enum AID : uint
{
    DeadRebirth = 26281, // ?->self, 10.0s cast, range 60 circle, raidwide
    ScorchedExaltation = 26296, // ?->self, 5.0s cast, range 60 circle, raidwide

    HeatOfCondemnation = 26291, // ?->player, 6.0s cast, range 6 circle, one on each tank

    Charplume = 26269, // ?->player, 5.0s cast, range 6 circle, spread
    FlareOfCondemnation = 26288, // ?->player, 5.0s cast, range 6 circle, spread
}
