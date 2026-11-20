namespace BossMod.Stormblood.Alliance.A13Rofocale;

sealed class A13RofocaleStates : StateMachineBuilder
{
    public A13RofocaleStates(BossModule module) : base(module)
    {
        TrivialPhase()
            .ActivateOnEnter<Chariot>()
            .ActivateOnEnter<CryOfVictory>()
            .ActivateOnEnter<CrushWeapon>()
            .ActivateOnEnter<Maverick>()
            .ActivateOnEnter<Karma>()
            .ActivateOnEnter<UnholyDarkness>()
            .ActivateOnEnter<Components.UnclaimedCastAOEs>();
    }
}
