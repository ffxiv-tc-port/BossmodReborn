namespace BossMod.Shadowbringers.Alliance.A12Hobbes;

sealed class A12HobbesStates : StateMachineBuilder
{
    public A12HobbesStates(BossModule module) : base(module)
    {
        TrivialPhase()
            .ActivateOnEnter<LaserResistanceTest>()
            .ActivateOnEnter<RingLaserOuter>()
            .ActivateOnEnter<ShockingDischarge>()
            .ActivateOnEnter<VariableCombatTestCircle>()
            .ActivateOnEnter<VariableCombatTestDonut>()
            .ActivateOnEnter<Impact>()
            .ActivateOnEnter<VariableCombatTestCone>()
            .ActivateOnEnter<Collapse>()
            .ActivateOnEnter<FireResistanceTest>()
            .ActivateOnEnter<ShortRangeMissile>()
            .ActivateOnEnter<LaserLineStack>()
            .ActivateOnEnter<Components.UnclaimedCastAOEs>();
    }
}
