namespace BossMod.Endwalker.Raid.P12NAthena;

// 只列有證據的技能：列舉＝模組認領，列進來卻沒有元件的技能就連後備猜測都沒有。
public enum OID : uint
{
    Boss = 0x3F23,
    Helper = 0x233C,
}

public enum AID : uint
{
    OnTheSoul = 33497, // Boss->self, 5.0s cast, range 60 circle, raidwide
    TheosUltima = 33490, // Boss->self, 7.0s cast, range 60 circle, raidwide

    TrinityOfSoulsTR = 33473, // Boss->self, 10.0s cast, range 60 180-degree cone (first hit, right wing)
    TrinityOfSoulsTL = 33474, // Boss->self, 10.0s cast, range 60 180-degree cone (first hit, left wing)
    TrinityOfSoulsMR = 33475, // Boss->self, no cast, range 60 180-degree cone (second hit)
    TrinityOfSoulsML = 33476, // Boss->self, no cast, range 60 180-degree cone (second hit)
    TrinityOfSoulsBR = 33477, // Boss->self, no cast, range 60 180-degree cone (last hit)
    TrinityOfSoulsBL = 33478, // Boss->self, no cast, range 60 180-degree cone (last hit)

    RayOfLight1 = 33480, // Anthropos->self, 5.0s cast, range 60 width 10 rect
    RayOfLight2 = 33492, // Anthropos->self, 4.0s cast, range 60 width 10 rect

    SuperchainBurst = 33469, // ?->self, 1.0s cast, range 7 circle
    SuperchainCoil = 33470, // ?->self, 1.0s cast, range 6-70 donut

    Glaukopis = 33493, // Boss->player, 5.0s cast, range 60 width 5 rect, tankbuster
    DialogosStack = 33494, // Boss->player, 5.0s cast, range 6 circle stack
    DialogosSpread = 33495, // ?->player, 5.0s cast, range 6 circle spread
    Parthenos = 33496, // Boss->self, 7.0s cast, range 120 width 16 rect
    PalladionPlatforms = 33489, // ?->self, 5.8s cast, range 20 width 40 rect
}

public enum IconID : uint
{
    WingTLFirst = 421, // Boss
    WingTRFirst = 422, // Boss
    WingML = 423, // Boss
    WingMR = 424, // Boss
    WingBL = 433, // Boss
    WingBR = 434, // Boss
}
