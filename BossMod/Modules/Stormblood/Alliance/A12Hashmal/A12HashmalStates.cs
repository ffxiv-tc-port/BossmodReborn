namespace BossMod.Stormblood.Alliance.A12Hashmal;

sealed class A12HashmalStates : StateMachineBuilder
{
    public A12HashmalStates(BossModule module) : base(module)
    {
        TrivialPhase()
            .ActivateOnEnter<QuakeIV>()
            .ActivateOnEnter<JaggedEdge>()
            .ActivateOnEnter<ControlTower>()
            .ActivateOnEnter<CommandTower>()
            .ActivateOnEnter<EarthShaker>()
            .ActivateOnEnter<Towerfall>()
            .ActivateOnEnter<ExtremeEdge>()
            .ActivateOnEnter<FallingBoulder>()
            .ActivateOnEnter<FallingRock>()
            .ActivateOnEnter<Components.UnclaimedCastAOEs>();
    }
}
