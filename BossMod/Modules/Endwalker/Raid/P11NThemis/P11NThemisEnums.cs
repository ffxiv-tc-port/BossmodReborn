namespace BossMod.Endwalker.Raid.P11NThemis;

public enum OID : uint
{
    Boss = 0x3EEC, // R11.000
    Helper = 0x233C, // R0.500
    IllusoryThemis = 0x3EED, // R11.000, clones
}

public enum AID : uint
{
    AutoAttack = 33331, // Boss->player, no cast
    Unknown33333 = 33333, // Boss->self, no cast, 不處理：無讀條、不打人

    Eunomia = 33250, // Boss->self, 4.7s cast, range 50 circle, raidwide + bleed
    Dike = 33251, // Boss->self, 4.7s cast, single-target, visual (tankbusters)
    DikeAOE1 = 33252, // Helper->player, 4.9s cast, single-target
    DikeAOE2 = 33253, // Helper->player, 4.9s cast, single-target

    DivisiveOverrulingLight = 33203, // Boss->self, 4.0s cast, single-target, visual (out + out)
    DivisiveOverrulingDark = 33204, // Boss->self, 4.0s cast, single-target, visual (out + in)
    DivisiveOverrulingAOE = 33205, // Helper->self, 5.9s cast, range 46 width 16 rect
    DivineRuination = 33206, // Helper->self, 8.5s cast, range 46 width 26 rect
    RipplesOfGloom1 = 33207, // Helper->self, 8.5s cast, range 46 width 16 rect
    RipplesOfGloom2 = 33208, // Helper->self, 8.5s cast, range 46 width 16 rect

    ShadowedMessengers = 33236, // Boss->self, 3.7s cast, single-target, visual (clones)
    DivisiveRulingLight = 33237, // IllusoryThemis->self, 7.4s cast, single-target, visual
    DivisiveRulingDark = 33238, // IllusoryThemis->self, 7.4s cast, single-target, visual
    DivisiveRulingAOE = 33239, // Helper->self, 7.9s cast, range 46 width 16 rect
    DivineRuinationClone = 33240, // Helper->self, 11.1s cast, range 46 width 26 rect
    RipplesOfGloomClone1 = 33241, // Helper->self, 11.1s cast, range 46 width 16 rect
    RipplesOfGloomClone2 = 33242, // Helper->self, 11.1s cast, range 46 width 16 rect

    UpheldOverrulingLight = 34766, // Boss->self, 5.5s cast, single-target, visual (party stack on tethered + out)
    UpheldOverrulingDark = 34767, // Boss->self, 5.5s cast, single-target, visual (aoe tankbuster on tethered + in)
    UpheldOverrulingAOELight = 33209, // Boss->players, no cast, range 6 circle stack
    UpheldOverrulingAOEDark = 33210, // Boss->player, no cast, range 13 circle tankbuster
    Lightburst = 33213, // Helper->self, 4.7s cast, range 13 circle
    DarkPerimeter = 33214, // Helper->self, 5.7s cast, range 8-50 donut

    DismissalOverrulingLight = 33272, // Boss->self, 5.2s cast, single-target, visual (knockback + out)
    DismissalOverrulingDark = 33273, // Boss->self, 5.2s cast, single-target, visual (knockback + in)
    DismissalOverrulingLightAOE = 33274, // Helper->self, 5.7s cast, range 40 circle, knockback 11
    DismissalOverrulingDarkAOE = 33275, // Helper->self, 5.7s cast, range 40 circle, knockback 11
    InnerLight = 34547, // Helper->self, 9.2s cast, range 13 circle
    OuterDark = 34548, // Helper->self, 9.2s cast, range 8-50 donut

    DarkAndLight = 33219, // Boss->self, 3.7s cast, single-target, visual (light/dark marks)
    DiscordantRuling = 33220, // Boss->self, 3.7s cast, single-target, visual (marks applied)
    KatakrisisLight = 33221, // Helper->player, no cast, single-target, light mark on the wrong side
    KatakrisisDark = 33222, // Helper->player, no cast, single-target, dark mark on the wrong side
    EmissarysWill = 33223, // Boss->self, 3.7s cast, range 50 circle, 不處理：結算，標記都對時不造成傷害

    Styx = 33243, // Boss->player, 4.7s cast, range 6 circle stack
    StyxAOE = 33244, // Boss->player, no cast, range 6 circle stack (follow-up hits)

    Vortex = 34597, // Boss->self, 2.7s cast, single-target, visual
    VortexAOE = 34598, // Helper->location, 3.7s cast, range 4 circle
}

public enum SID : uint
{
    LightHarmony = 3553, // 光之判紋：調和
    DarkHarmony = 3554, // 暗之判紋：調和
    LightDiscord = 3555, // 光之判紋：失調
    DarkDiscord = 3556, // 暗之判紋：失調
}

public enum TetherID : uint
{
    UpheldOverrulingDark = 192, // Boss->player
    UpheldOverrulingLight = 193, // Boss->player
}
