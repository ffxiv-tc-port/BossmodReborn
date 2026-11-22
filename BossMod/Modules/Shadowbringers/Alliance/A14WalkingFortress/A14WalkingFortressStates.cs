namespace BossMod.Shadowbringers.Alliance.A14WalkingFortress;

sealed class A14WalkingFortressStates : StateMachineBuilder
{
    public A14WalkingFortressStates(BossModule module) : base(module)
    {
        TrivialPhase()
            .ActivateOnEnter<Neutralization>()
            .ActivateOnEnter<LaserSaturation>()
            .ActivateOnEnter<LaserTurret>()
            .ActivateOnEnter<GroundToGroundMissile>()
            .ActivateOnEnter<BallisticImpact>()
            .ActivateOnEnter<LaserSuppression>()
            .ActivateOnEnter<MarxImpact>()
            .ActivateOnEnter<Divebomb>()
            .ActivateOnEnter<ConvenientSelfDestructionLarge>()
            .ActivateOnEnter<ConvenientSelfDestructionSmall>()
            .ActivateOnEnter<ClangingBlow>()
            .ActivateOnEnter<ShrapnelImpact>()
            .ActivateOnEnter<CentrifugalSpin>()
            .ActivateOnEnter<AnnihilationBombing>()
            .ActivateOnEnter<MissileBarrage>()
            .ActivateOnEnter<GoliathTankPriority>()
            .ActivateOnEnter<Components.UnclaimedCastAOEs>();
    }
}
