namespace BossMod.Shadowbringers.Raid.E4STitan;

// NOTE (WIP): no per-cast timeline (the Wheels/Gauntlets path choice is random, so a fully
// deterministic one is impossible anyway). Broad phase blocks keep every relevant component active,
// with one placeholder state per phase; components react to live cast/icon events, so only the lead
// time of the "upcoming mechanic" UI is lost, not hint accuracy.
class E4STitanStates : StateMachineBuilder
{
    public E4STitanStates(BossModule module) : base(module)
    {
        SimplePhase(0, Phase1, "Phase 1-2 (Titan)")
            .ActivateOnEnter<Stonecrusher>()
            .ActivateOnEnter<PulseOfTheLand>()
            .ActivateOnEnter<EvilEarth>()
            .ActivateOnEnter<ForceOfTheLand>()
            .ActivateOnEnter<VoiceOfTheLand>()
            .ActivateOnEnter<Geocrush>()
            .ActivateOnEnter<MassiveLandslideFront>()
            .ActivateOnEnter<MassiveLandslideSides>()
            .ActivateOnEnter<LandslideBackCorners>()
            .ActivateOnEnter<LandslideDirectional>()
            .ActivateOnEnter<FaultLineSides>()
            .ActivateOnEnter<FaultLineFront>()
            .ActivateOnEnter<MagnitudeFive>()
            .ActivateOnEnter<BombBoulders>()
            .ActivateOnEnter<SeismicWave>()
            .Raw.Update = () => Module.PrimaryActor.IsDestroyed || !Module.PrimaryActor.IsTargetable; // ends at Orogenesis untargetable

        SimplePhase(1, Transition, "Orogenesis transition")
            .Raw.Update = () => Module.PrimaryActor.IsDestroyed || Module.PrimaryActor.IsTargetable;

        DeathPhase(2, Phase3)
            .ActivateOnEnter<EarthenFury>()
            .ActivateOnEnter<Tumult>()
            .ActivateOnEnter<TectonicUplift>()
            .ActivateOnEnter<EarthenAnguish>()
            .ActivateOnEnter<EarthenFist>()
            .ActivateOnEnter<DualEarthenFists>()
            .ActivateOnEnter<Megalith>()
            .ActivateOnEnter<WeightOfTheWorld>()
            .ActivateOnEnter<GraniteGaol>()
            .ActivateOnEnter<PlateFracture>()
            .ActivateOnEnter<VoiceOfTheLand>()
            .ActivateOnEnter<PulseOfTheLand>()
            .ActivateOnEnter<ForceOfTheLand>();
    }

    private void Phase1(uint id) => SimpleState(id, 1000, "Titan mechanics (phase ends via untargetable check above)");
    private void Transition(uint id) => SimpleState(id, 1000, "Orogenesis (phase ends via targetable check above)");
    private void Phase3(uint id) => SimpleState(id, 1000, "Titan Maximum mechanics (enrage)");
}
