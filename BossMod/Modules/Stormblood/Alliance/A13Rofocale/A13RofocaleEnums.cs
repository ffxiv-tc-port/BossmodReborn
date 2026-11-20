namespace BossMod.Stormblood.Alliance.A13Rofocale;

// 只列有證據的技能：列舉＝模組認領，列進來卻沒有元件的技能就連後備猜測都沒有。
public enum OID : uint
{
    Boss = 0x1FAF, // R5.850
    Helper = 0x18D6, // R0.500
    RofocaleClone = 0x2026, // R5.850, casts Maverick
    Archaeodemon = 0x1FC3, // R1.950
}

public enum AID : uint
{
    Chariot = 9844, // Boss->players, 3.0s cast, width 10 rect charge
    CryOfVictory1 = 9845, // Boss->self, 3.0s cast, range 60 180-degree cone
    CryOfVictory2 = 10060, // Boss->self, 3.0s cast, range 60 180-degree cone
    CrushWeapon = 9860, // Helper->location, 3.5s cast, range 6 circle

    Maverick1 = 9865, // RofocaleClone/Boss->location, 6.0s cast, range 60 width 16 rect
    Maverick2 = 10022, // RofocaleClone/Boss->location, 6.0s cast, range 60 width 16 rect
    Maverick3 = 10036, // RofocaleClone/Boss->location, 1.0s cast, range 60 width 16 rect

    Karma = 9842, // Archaeodemon->self, 3.0s cast, range 30 90-degree cone
    UnholyDarkness = 9843, // Archaeodemon->location, 3.5s cast, range 8 circle
}
