namespace BossMod.Shadowbringers.Alliance.A13Engels;

class A13MarxEngelsStates : StateMachineBuilder
{
    public A13MarxEngelsStates(BossModule module) : base(module)
    {
        TrivialPhase()
            .ActivateOnEnter<DemolishStructureArenaChange>()

            .ActivateOnEnter<PrecisionGuidedMissile2>()
            .ActivateOnEnter<DiffuseLaser>()
            .ActivateOnEnter<LaserSight1>()
            .ActivateOnEnter<GuidedMissile2>()
            .ActivateOnEnter<IncendiaryBombing2>()
            //.ActivateOnEnter<IncendiaryBombing1>()
            .ActivateOnEnter<GuidedMissile>()
            .ActivateOnEnter<BossVisualAOEs>()
            .ActivateOnEnter<ArmLaser>()
            .ActivateOnEnter<MarxThrust>()
            .ActivateOnEnter<WideAngleDiffuseLaser>()
            .ActivateOnEnter<MarxCrush>()
            .ActivateOnEnter<SurfaceMissile2>();
    }
}
