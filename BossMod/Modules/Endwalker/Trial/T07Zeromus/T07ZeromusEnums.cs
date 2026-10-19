namespace BossMod.Endwalker.Trial.T07Zeromus;

public enum OID : uint
{
    Boss = 0x404E, // R20.000, x1
    Comet = 0x40AB, // R2.250, x22
    ToxicBubble = 0x40AC, // R1.700, spawn during fight
    FlowOfTheAbyss = 0x4110, // R1.000, x1
    Helper = 0x233C, // R0.500, x30, Helper type
    FlareTower = 0x1EB94E, // R0.500, EventObj type, spawn during fight
    FlareRay = 0x1EB94F, // R0.500, EventObj type, spawn during fight
    BlackHole = 0x1EB94C, // R0.500, EventObj type, spawn during fight
}

// 契約：這個列舉＝模組知道的技能。列舉以外的敵方讀條由 Components.UnclaimedCastAOEs 照沒有模組時的猜法處理。
// 沒有元件引用的成員一律在行尾寫「不處理：理由」。
public enum AID : uint
{
    AutoAttack = 35912, // Boss->player, no cast, single-target；不處理：普通攻擊

    AbyssalNox = 35575, // Boss->self, 5.0s cast, range 60 circle, raidwide + doom
    AbyssalEchoesVisualCardinal = 35576, // Helper->self, 9.0s cast, single-target；不處理：預告線，沒有判定
    AbyssalEchoesVisualIntercardinal = 35577, // Helper->self, 9.0s cast, single-target；不處理：預告線，沒有判定
    AbyssalEchoes = 35578, // Helper->self, 16.0s cast, range 12 circle

    SableThread = 35566, // Boss->self, 5.0s cast, single-target；不處理：首領本體的演出，判定在 SableThreadAOE
    SableThreadTarget = 35567, // Helper->player, no cast, single-target, target selection
    SableThreadVisualHitIntermediate = 35568, // Boss->self, no cast, single-target；不處理：演出
    SableThreadVisualHitLast = 35569, // Boss->self, no cast, single-target；不處理：演出
    SableThreadAOE = 35570, // Helper->self, no cast, range 60 width 12 rect, line stack, 5 hits

    FracturedEventideWE = 35571, // Boss->self, 8.0s cast, single-target；不處理：首領本體的演出
    FracturedEventideEW = 35572, // Boss->self, 8.0s cast, single-target；不處理：首領本體的演出
    FracturedEventideAOEFirstWE = 35573, // Helper->self, 8.5s cast, range 60 width 8 rect
    FracturedEventideAOEFirstEW = 35910, // Helper->self, 8.5s cast, range 60 width 8 rect
    FracturedEventideAOERest = 35574, // Helper->self, no cast, range 60 width 8 rect, 20 hits

    VisceralWhirlR = 35579, // Boss->self, 8.0s cast, single-target；不處理：首領本體的演出
    VisceralWhirlRAOE1 = 35580, // Helper->self, 8.8s cast, range 29 width 28 rect
    VisceralWhirlRAOE2 = 35581, // Helper->self, 8.8s cast, range 60 width 28 rect
    VisceralWhirlL = 35582, // Boss->self, 8.0s cast, single-target；不處理：首領本體的演出
    VisceralWhirlLAOE1 = 35583, // Helper->self, 8.8s cast, range 29 width 28 rect
    VisceralWhirlLAOE2 = 35584, // Helper->self, 8.8s cast, range 60 width 28 rect

    BigBang = 35587, // Boss->self, 10.0s cast, range 60 circle, raidwide
    BigBangAOE = 35589, // Helper->location, 8.0s cast, range 5 circle
    BigCrunch = 35588, // Boss->self, 10.0s cast, range 60 circle, raidwide
    BigCrunchAOE = 36144, // Helper->location, 8.0s cast, range 5 circle
    DarkDivides = 35593, // Helper->player, no cast, range 5 circle spread
    DarkBeckons = 35594, // Helper->players, no cast, range 6 circle stack

    VoidMeteor = 35596, // Boss->self, 4.8s cast, single-target；不處理：首領本體的演出
    MeteorImpactProximity = 35601, // Comet->self, 6.0s cast, range 40 circle with falloff
    MeteorImpact = 35595, // Boss->self, 11.0s cast, single-target；不處理：首領本體的演出
    MeteorImpactCharge = 35597, // Comet->location, no cast, width 4 rect charge；不處理：連線隕石要記每個人的線，沒有讀條可跟
    MeteorImpactAppear = 36024, // Helper->location, no cast, range 2 circle；不處理：隕石落地的演出
    MeteorImpactExplosion = 36147, // Comet->self, 5.0s cast, range 10 circle

    Flare = 35602, // Boss->self, 7.0s cast, single-target；不處理：首領本體的演出
    FlareAOE = 35603, // Helper->self, 8.0s cast, range 5 circle tower
    FlareScald = 35765, // Helper->self, no cast, range 5 circle, tower aftereffect
    FlareKill = 35605, // Helper->self, 5.0s cast, range 5 circle
    ProminenceSpine = 35606, // Helper->self, 5.0s cast, range 60 width 10 rect

    VoidBio = 35607, // Boss->self, 5.0s cast, single-target；不處理：首領本體的演出，危險區跟著 ToxicBubble 走
    RendTheRift = 35609, // Boss->self, 6.0s cast, range 60 circle, raidwide

    Nostalgia = 35610, // Boss->self, 5.0s cast, single-target；不處理：首領本體的演出
    NostalgiaBury1 = 35612, // Helper->self, 0.7s cast, range 60 circle
    NostalgiaBury2 = 35613, // Helper->self, 1.7s cast, range 60 circle
    NostalgiaBury3 = 35614, // Helper->self, 2.7s cast, range 60 circle
    NostalgiaBury4 = 35615, // Helper->self, 3.7s cast, range 60 circle
    NostalgiaRoar1 = 35616, // Helper->self, 5.7s cast, range 60 circle
    NostalgiaRoar2 = 35617, // Helper->self, 6.7s cast, range 60 circle
    NostalgiaPrimalRoar = 35618, // Helper->self, 9.7s cast, range 60 circle
    NostalgiaDimensionalSurge = 35633, // Helper->location, 4.0s cast, range 5 circle
    DimensionalSurgeSmall = 35634, // Helper->location, 4.0s cast, range 2 circle
    DimensionalSurgeLine = 35635, // Helper->self, 4.0s cast, range 60 width 2 rect

    FlowOfTheAbyss = 36089, // Boss->self, 7.0s cast, single-target；不處理：首領本體的演出
    FlowOfTheAbyssDimensionalSurge = 35637, // Helper->self, 9.0s cast, range 60 width 14 rect
    AkhRhaiStart = 35619, // Helper->self, 3.0s cast, range 5 circle
    AkhRhaiAOE = 35620, // Helper->self, no cast, range 5 circle, 10 hits
    ChasmicNails = 35622, // Boss->self, 7.0s cast, single-target；不處理：首領本體的演出
    ChasmicNailsAOE1 = 35628, // Helper->self, 7.7s cast, range 60 40-degree cone
    ChasmicNailsAOE2 = 35629, // Helper->self, 8.4s cast, range 60 40-degree cone
    ChasmicNailsAOE3 = 35630, // Helper->self, 9.1s cast, range 60 40-degree cone
    ChasmicNailsAOE4 = 35631, // Helper->self, 9.8s cast, range 60 40-degree cone
    ChasmicNailsAOE5 = 35632, // Helper->self, 10.5s cast, range 60 40-degree cone
    ChasmicNailsVisual1 = 35623, // Helper->self, 1.5s cast；不處理：預兆，判定在 ChasmicNailsAOE1
    ChasmicNailsVisual2 = 35624, // Helper->self, 3.0s cast；不處理：預兆，判定在 ChasmicNailsAOE2
    ChasmicNailsVisual3 = 35625, // Helper->self, 4.0s cast；不處理：預兆，判定在 ChasmicNailsAOE3
    ChasmicNailsVisual4 = 35626, // Helper->self, 5.0s cast；不處理：預兆，判定在 ChasmicNailsAOE4
    ChasmicNailsVisual5 = 35627, // Helper->self, 6.0s cast；不處理：預兆，判定在 ChasmicNailsAOE5

    DarkMatter = 35638, // Boss->self, 4.0s cast, single-target；不處理：首領本體的演出
    DarkMatterAOE = 35639, // Helper->player, no cast, range 8 circle tankbuster, 3 hits

    Nox = 36135, // Boss->self, 4.0s cast, single-target；不處理：首領本體的演出
    NoxAOEFirst = 36137, // Helper->self, 5.0s cast, range 10 circle
    NoxAOERest = 36131, // Helper->self, no cast, range 10 circle, 2 hits

    BlackHole = 36133, // Boss->self, 5.0s cast, single-target；不處理：首領本體的演出
    BlackHoleAOE = 36134, // Helper->self, 5.7s cast, range 4 circle
}

public enum SID : uint
{
    AccelerationBomb = 2657, // none->player, extra=0x0
}

public enum IconID : uint
{
    DarkMatter = 364, // player
    DarkBeckons = 100, // player
    DarkDivides = 376, // player
    Nox = 197, // player
    AkhRhai = 23, // player
}
