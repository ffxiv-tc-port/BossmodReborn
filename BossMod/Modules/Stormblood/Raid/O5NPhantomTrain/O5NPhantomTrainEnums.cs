namespace BossMod.Stormblood.Raid.O5NPhantomTrain;

// 契約：列舉＝模組知道的技能；列舉以外的敵方讀條由 Components.UnclaimedCastAOEs 照沒有模組時的猜法畫。
public enum OID : uint
{
    Boss = 0x213E, // R15.750
    WrothGhost = 0x21AB, // R1.800
    PutridPassenger = 0x2140, // R0.500
    Helper = 0x18D6
}

public enum AID : uint
{
    AutoAttack = 10402, // Boss->player, no cast, single-target
    DoomStrike = 10403, // Boss->player, 3.5s cast, single-target
    HeadOn = 10404, // Boss->self, 7.0s cast, single-target
    HeadOnHit = 10415, // Helper->location, 7.8s cast, range 50 circle, damage falloff + knockback
    DiabolicHeadlamp = 10406, // Boss->self, 5.0s cast, single-target
    DiabolicHeadlampHit = 10416, // Boss->self, no cast, range 50 width 6 rect, line stack
    SaintlyBeam = 10410, // Helper->location, no cast, range 6 circle
    SaintlyBeamVisual = 10431, // Boss->self, no cast, single-target
    Encumber = 10414, // WrothGhost->self, 4.0s cast, range 8 width 10 rect
    AcidRain = 10427, // Boss->self, 3.0s cast, single-target
    AcidRainHit = 10411, // Helper->players, no cast, range 50 circle
    DiabolicWind = 10425 // Helper->player, no cast, range 5 circle
}

public enum IconID : uint
{
    DiabolicWind = 70 // player->self
}
