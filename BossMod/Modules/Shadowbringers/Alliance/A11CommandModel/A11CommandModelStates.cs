namespace BossMod.Shadowbringers.Alliance.A11CommandModel;

sealed class A11CommandModelStates : StateMachineBuilder
{
    public A11CommandModelStates(BossModule module) : base(module)
    {
        TrivialPhase()
            .ActivateOnEnter<ClangingBlow>()
            .ActivateOnEnter<ForcefulImpact>()
            .ActivateOnEnter<EnergyBombardment>()
            .ActivateOnEnter<EnergyAssault>()
            .ActivateOnEnter<EnergyRing>()
            .ActivateOnEnter<SidestrikingSpin>()
            .ActivateOnEnter<CentrifugalSpin>()
            .ActivateOnEnter<HighCaliberLaser>()
            .ActivateOnEnter<Shockwave>()
            .ActivateOnEnter<EnergyOrb>()
            .ActivateOnEnter<GroundEnergyBomb>()
            .ActivateOnEnter<Components.UnclaimedCastAOEs>();
    }
}
