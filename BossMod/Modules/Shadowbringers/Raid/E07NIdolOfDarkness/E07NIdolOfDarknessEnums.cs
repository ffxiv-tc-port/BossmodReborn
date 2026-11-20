namespace BossMod.Shadowbringers.Raid.E07NIdolOfDarkness;

// 首領 OID 沒有錄影：暗黑心象（BNpcName 9298）同模型 2880 的只有這兩列，分不出一般／零式，兩個都註冊。
// 契約：列舉＝模組知道的技能；列舉以外的敵方讀條由 Components.UnclaimedCastAOEs 照沒有模組時的猜法畫。
public enum OID : uint
{
    BossCandidateA = 0x2D2D,
    BossCandidateB = 0x2D32,
    Helper = 0x233C,
}

public enum AID : uint
{
    EmptyWave = 19538, // Boss->self, 4.0s cast, range 100 circle, raidwide
    StygianStake = 19507, // Boss->player, no cast, range 100 width 6 rect, tethered tank
    SilverSledge = 19540, // Boss->player, 5.0s cast, range 6 circle stack
    SilverShot = 20093, // Boss->player, 8.0s cast, range 6 circle spread
    StrengthInNumbersDonut = 19532, // Idolatry->self, 11.0s cast, range 8-20 donut
    LightsCourseShort = 19490, // UnforgivenIdolatry->self, 2.0s cast, range 50 width 10 rect
    LightsCourse = 19516, // UnforgivenIdolatry->self, 7.0s cast, range 50 width 10 rect
    LightsCourseAlt = 19518, // UnforgivenIdolatry->self, 7.0s cast, range 50 width 10 rect
    LightsCourseToPortal = 20067, // UnforgivenIdolatry->self, 7.0s cast, range 25 width 10 rect
    DarksCourseShort = 19491, // UnforgivenIdolatry->self, 2.0s cast, range 50 width 10 rect
    DarksCourse = 19517, // UnforgivenIdolatry->self, 7.0s cast, range 50 width 10 rect
    DarksCourseAlt = 19521, // UnforgivenIdolatry->self, 7.0s cast, range 50 width 10 rect
}

public enum SID : uint
{
    AstralEffect = 2238, // none->player, 偏屬性：光
    UmbralEffect = 2239, // none->player, 偏屬性：暗
}

public enum TetherID : uint
{
    UnshadowedStake = 0x25, // Boss->player
}
