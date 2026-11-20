namespace BossMod.Stormblood.Alliance.A14Argath;

sealed class A14ArgathStates : StateMachineBuilder
{
    public A14ArgathStates(BossModule module) : base(module)
    {
        TrivialPhase()
            .ActivateOnEnter<Raidwides>()
            .ActivateOnEnter<CripplingBlow>()
            .ActivateOnEnter<CrushWeapon>()
            .ActivateOnEnter<Soulfix>()
            .ActivateOnEnter<Heartless>()
            .ActivateOnEnter<CommandState>()
            .ActivateOnEnter<CommandFlee>()
            .ActivateOnEnter<CommandTurn>()
            .ActivateOnEnter<Components.UnclaimedCastAOEs>();
    }
}
