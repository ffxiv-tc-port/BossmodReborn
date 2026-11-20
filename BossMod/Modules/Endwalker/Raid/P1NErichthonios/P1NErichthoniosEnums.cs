namespace BossMod.Endwalker.Raid.P1NErichthonios;

// 只列錄影裡出現過的技能：列舉＝模組認領，列進來卻沒有元件的技能就連後備猜測都沒有。
public enum OID : uint
{
    Boss = 0x3521, // R9.975
    Helper = 0x233C, // R0.500
}

public enum AID : uint
{
    AutoAttack = 872, // Boss->player, no cast, single-target

    GaolerFlailVisualR = 26073, // Boss->self, 7.7s cast, visual only (effect 27, no damage)
    GaolerFlailVisualL = 26074, // Boss->self, 7.7s cast, visual only (effect 27, no damage)
    GaolerFlailR = 28066, // Helper->self, 8.4s cast, range 60 180-degree cone
    GaolerFlailL = 28067, // Helper->self, 8.4s cast, range 60 180-degree cone

    WarderWrath = 26100, // Boss->self, 4.7s cast, range 60 circle, raidwide

    PitilessFlailMarker = 27925, // Helper->player, no cast, single-target, marks the line target
    PitilessFlail = 26085, // Boss->player, 4.7s cast, range 60 width 6 rect, hits only the target (+knockback)

    TrueHolyVisual = 26086, // Boss->self, no cast, single-target
    TrueHoly = 26087, // Helper->player, no cast, range 6 circle stack

    HeavyHand = 26099, // Boss->player, 4.7s cast, single-target, tankbuster
}

public enum IconID : uint
{
    TrueHoly = 62, // player
}
