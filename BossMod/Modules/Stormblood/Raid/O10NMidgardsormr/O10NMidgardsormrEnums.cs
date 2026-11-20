namespace BossMod.Stormblood.Raid.O10NMidgardsormr;

// 首領 OID 沒有錄影：幻龍米德加爾特（BNpcName 7641）同模型兩列（0x2448／0x244E），比照阿爾法幻境1 一般在前、零式在後，兩個都註冊。
// 契約：列舉＝模組知道的技能（有元件處理，或刻意不處理）；列舉以外的敵方讀條由 Components.UnclaimedCastAOEs 照沒有模組時的猜法畫。
public enum OID : uint
{
    BossCandidateA = 0x2448,
    BossCandidateB = 0x244E,
}

public enum AID : uint
{
    TailEnd = 12741, // Boss->player, 4.0s cast, single-target tankbuster
    AkhMorn = 12742, // Boss->player, 5.0s cast, range 6 circle stack, multi-hit
    SpinHorizontal1 = 12743, // Boss->self, no cast, single-target
    SpinVertical1 = 12744, // Boss->self, no cast, single-target
    SpinHorizontal2 = 12745, // Boss->self, no cast, single-target
    SpinVertical2 = 12747, // Boss->self, no cast, single-target
    AetherOut = 12749, // Boss->self, 1.0s cast, range 14 circle (horizontal + horizontal)
    AetherCrossAlt = 12750, // Boss->self, 1.5s cast, range 30 width 18 cross
    AetherIn = 12751, // Boss->self, 1.0s cast, range 10-40 donut (horizontal + vertical)
    AetherCorners = 12752, // Boss->self, 2.0s cast, range 30 width 18 cross (vertical + vertical)
    EarthShaker = 12753, // Boss->self, no cast, line towards marked players
    Thunderstorm = 12754, // Boss->player, 5.0s cast, range 5 circle spread (icon 0xA0)
    HorridRoar = 12755, // ?->location, 3.5s cast, range 6 circle
    Cauterize = 12865, // Boss->self, 4.5s cast, range 40 width 20 rect
    RimeWreath = 13295, // AncientDragon->self, 2.0s cast, range 100 circle
    DryIce1 = 13755, // ?->self, 2.0s cast, range 9 circle
    DryIce2 = 13756, // ?->self, 2.0s cast, range 12 circle
    NorthernCross = 13865, // Boss->self, 3.0s cast, range 60 circle
}

public enum IconID : uint
{
    Thunderstorm = 0xA0, // player
}
