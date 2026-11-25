namespace BossMod.Stormblood.Trial.T02Lakshmi;

class T02LakshmiStates : StateMachineBuilder
{
    public T02LakshmiStates(BossModule module) : base(module)
    {
        TrivialPhase()
            .ActivateOnEnter<DivineDenial>()
            .ActivateOnEnter<DivineDenialKnockback>()
            .ActivateOnEnter<Stotram1>()
            .ActivateOnEnter<Stotram2>()
            .ActivateOnEnter<JagadishwariVril>()
            .ActivateOnEnter<ThePallOfLightStack>()
            .ActivateOnEnter<ThePullOfLightTB1>()
            .ActivateOnEnter<ThePullOfLightTB2>()
            .ActivateOnEnter<InnerDemons>()
            .ActivateOnEnter<EmanationTrash>()
            .ActivateOnEnter<VrilOrbs>()
            .ActivateOnEnter<PathOfLight>()
            .ActivateOnEnter<BlissfulBaits>()
            .ActivateOnEnter<CircleZone>()
            .ActivateOnEnter<Components.UnclaimedCastAOEs>();
    }
}
