namespace BossMod.Shadowbringers.Raid.E04NTitan;

// 契約：列舉＝模組知道的技能；列舉以外的敵方讀條由 Components.UnclaimedCastAOEs 照沒有模組時的猜法畫。
public enum OID : uint
{
    Boss = 0x298C, // R5.000
    MassiveBoulder = 0x298E, // R2.210
    Helper = 0x233C,
}

public enum AID : uint
{
    VoiceOfTheLand = 16631, // Boss->self, 4.0s cast, range 100 circle, raidwide
    EarthenFury = 16632, // Boss->self, 6.0s cast, single-target, raidwide + bleed follows
    Stonecrusher = 16633, // Boss->player, 5.0s cast, range 5 circle tankbuster
    FaultZone = 16643, // ?->self, 3.0s cast, range 42 width 52 rect knockback
}
