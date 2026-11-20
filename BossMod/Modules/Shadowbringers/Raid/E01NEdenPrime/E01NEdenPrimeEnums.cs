namespace BossMod.Shadowbringers.Raid.E01NEdenPrime;

// 首領 OID 沒有錄影：至尊伊甸（BNpcName 8345）在 BNpcBase 只有這兩列、同模型，分不出一般／零式，兩個都註冊。
// 契約：列舉＝模組知道的技能；列舉以外的敵方讀條由 Components.UnclaimedCastAOEs 照沒有模組時的猜法畫。
public enum OID : uint
{
    BossCandidateA = 0x2495,
    BossCandidateB = 0x2499,
    Helper = 0x233C,
}

public enum AID : uint
{
    EdensGravity = 15764, // Boss->self, 4.0s cast, range 60 circle, raidwide
    DimensionalShift = 15772, // Boss->self, 4.0s cast, range 60 circle, raidwide
    FragorMaximus = 15780, // Boss->self, 5.0s cast, range 60 circle, raidwide
    ManaBurst = 15783, // GuardianOfParadise->self, 4.0s cast, range 60 circle, raidwide
    EdensFlare = 15767, // Boss->self, 4.0s cast, range 5-60 donut
    SpearOfParadise = 15777, // Boss->player, 5.0s cast, single-target tankbuster
    EdensFireIII = 17643, // ?->player, 5.0s cast, range 5 circle spread
    ViceOfVanity = 17639, // Boss->players, no cast, range 100 width 6 rect, tethered tanks
}

public enum TetherID : uint
{
    ViceOfVanity = 0x11, // player->Boss
}
