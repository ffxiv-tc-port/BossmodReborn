namespace BossMod.Stormblood.Alliance.A33ThunderGod;

class A33ThunderGodStates : StateMachineBuilder
{
    public A33ThunderGodStates(BossModule module) : base(module)
    {
        TrivialPhase()
                .ActivateOnEnter<HallowedBolt>()
                .ActivateOnEnter<CrushWeapon>()
                .ActivateOnEnter<Duskblade>()
                .ActivateOnEnter<HolySword>()
                .ActivateOnEnter<ColosseumArena>()
                .ActivateOnEnter<BalanceAsunder>()
                .ActivateOnEnter<HallowedBoltStack>()
                .ActivateOnEnter<Shadowblade>();
    }
}
