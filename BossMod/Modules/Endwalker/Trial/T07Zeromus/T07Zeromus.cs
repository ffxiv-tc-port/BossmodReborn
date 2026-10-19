namespace BossMod.Endwalker.Trial.T07Zeromus;

class AbyssalNox(BossModule module) : Components.RaidwideCast(module, (uint)AID.AbyssalNox);
class AbyssalEchoes(BossModule module) : Components.SimpleAOEs(module, (uint)AID.AbyssalEchoes, 12f);
class SableThread(BossModule module) : Components.LineStack(module, aidMarker: (uint)AID.SableThreadTarget, aidResolve: (uint)AID.SableThreadAOE, activationDelay: 5.7d, range: 60f, halfWidth: 6f, maxCasts: 5, markerIsFinalTarget: false);

class BigBang(BossModule module) : Components.RaidwideCast(module, (uint)AID.BigBang);
class BigCrunch(BossModule module) : Components.RaidwideCast(module, (uint)AID.BigCrunch);
class BigBangPuddle(BossModule module) : Components.SimpleAOEGroups(module, [(uint)AID.BigBangAOE, (uint)AID.BigCrunchAOE], 5f);
class DarkBeckons(BossModule module) : Components.StackWithIcon(module, (uint)IconID.DarkBeckons, (uint)AID.DarkBeckons, 6f, 8d, 2, 8);
class DarkDivides(BossModule module) : Components.SpreadFromIcon(module, (uint)IconID.DarkDivides, (uint)AID.DarkDivides, 5f, 5d);

// 距離衰減：照極版模組的取法，離落點 10 碼以上
class MeteorImpactProximity(BossModule module) : Components.SimpleAOEs(module, (uint)AID.MeteorImpactProximity, 10f);
class MeteorImpactExplosion(BossModule module) : Components.SimpleAOEs(module, (uint)AID.MeteorImpactExplosion, 10f);

class FlareTower(BossModule module) : Components.CastTowers(module, (uint)AID.FlareAOE, 5f, 1, 8);
class FlareKill(BossModule module) : Components.SimpleAOEs(module, (uint)AID.FlareKill, 5f);
class ProminenceSpine(BossModule module) : Components.SimpleAOEs(module, (uint)AID.ProminenceSpine, new AOEShapeRect(60f, 5f));

class RendTheRift(BossModule module) : Components.RaidwideCast(module, (uint)AID.RendTheRift);
class Nostalgia(BossModule module) : Components.RaidwideCasts(module, [(uint)AID.NostalgiaBury1, (uint)AID.NostalgiaBury2, (uint)AID.NostalgiaBury3, (uint)AID.NostalgiaBury4,
    (uint)AID.NostalgiaRoar1, (uint)AID.NostalgiaRoar2, (uint)AID.NostalgiaPrimalRoar]);
class NostalgiaDimensionalSurge(BossModule module) : Components.SimpleAOEs(module, (uint)AID.NostalgiaDimensionalSurge, 5f);
class DimensionalSurgeSmall(BossModule module) : Components.SimpleAOEs(module, (uint)AID.DimensionalSurgeSmall, 2f);
class DimensionalSurgeLine(BossModule module) : Components.SimpleAOEs(module, (uint)AID.DimensionalSurgeLine, new AOEShapeRect(60f, 1f));
class FlowOfTheAbyssDimensionalSurge(BossModule module) : Components.SimpleAOEs(module, (uint)AID.FlowOfTheAbyssDimensionalSurge, new AOEShapeRect(60f, 7f));

class BlackHole(BossModule module) : Components.SimpleAOEs(module, (uint)AID.BlackHoleAOE, 4f);
class NoxFirst(BossModule module) : Components.SimpleAOEs(module, (uint)AID.NoxAOEFirst, 10f);

class T07ZeromusStates : StateMachineBuilder
{
    public T07ZeromusStates(BossModule module) : base(module)
    {
        TrivialPhase()
            .ActivateOnEnter<AbyssalNox>()
            .ActivateOnEnter<AbyssalEchoes>()
            .ActivateOnEnter<SableThread>()
            .ActivateOnEnter<FracturedEventide>()
            .ActivateOnEnter<VisceralWhirl>()
            .ActivateOnEnter<BigBang>()
            .ActivateOnEnter<BigCrunch>()
            .ActivateOnEnter<BigBangPuddle>()
            .ActivateOnEnter<AccelerationBomb>()
            .ActivateOnEnter<DarkBeckons>()
            .ActivateOnEnter<DarkDivides>()
            .ActivateOnEnter<MeteorImpactProximity>()
            .ActivateOnEnter<MeteorImpactExplosion>()
            .ActivateOnEnter<FlareTower>()
            .ActivateOnEnter<FlareScald>()
            .ActivateOnEnter<FlareKill>()
            .ActivateOnEnter<ProminenceSpine>()
            .ActivateOnEnter<VoidBio>()
            .ActivateOnEnter<RendTheRift>()
            .ActivateOnEnter<Nostalgia>()
            .ActivateOnEnter<NostalgiaDimensionalSurge>()
            .ActivateOnEnter<DimensionalSurgeSmall>()
            .ActivateOnEnter<DimensionalSurgeLine>()
            .ActivateOnEnter<FlowOfTheAbyssDimensionalSurge>()
            .ActivateOnEnter<AkhRhaiSpread>()
            .ActivateOnEnter<AkhRhai>()
            .ActivateOnEnter<ChasmicNails>()
            .ActivateOnEnter<DarkMatter>()
            .ActivateOnEnter<Nox>()
            .ActivateOnEnter<NoxFirst>()
            .ActivateOnEnter<BlackHole>()
            .ActivateOnEnter<Components.UnclaimedCastAOEs>();
    }
}

[ModuleInfo(BossModuleInfo.Maturity.Contributed, Contributors = "ffxiv-tc-port", GroupType = BossModuleInfo.GroupType.CFC, GroupID = 964, NameID = 12586)]
public class T07Zeromus(WorldState ws, Actor primary) : BossModule(ws, primary, ArenaCenter, new ArenaBoundsSquare(20f))
{
    public static readonly WPos ArenaCenter = new(100f, 100f);
}
