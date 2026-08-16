namespace BossMod.Stormblood.Trial.T07Byakko;

class T07ByakkoStates : StateMachineBuilder
{
    public T07ByakkoStates(BossModule module) : base(module)
    {
        TrivialPhase()
            .ActivateOnEnter<StormPulse>()
            .ActivateOnEnter<HeavenlyStrike>()
            .ActivateOnEnter<HeavenlyStrikeSpread>()
            .ActivateOnEnter<SweepTheLeg1>()
            .ActivateOnEnter<SweepTheLeg3>()
            .ActivateOnEnter<TheRoarOfThunder>()
            .ActivateOnEnter<ImperialGuard>()
            // still disabled: stack marker not being removed properly.
            // NOT an "it never fires on TC" problem - three TC replays of CFC 290 (2026-08-16) show icon 62 on
            // Helper (OID 0x18D6) followed by HighestStakes2 (AID 10806), exactly as the enums describe.
            //.ActivateOnEnter<HighestStakes>()
            .ActivateOnEnter<FireAndLightning>()
            .ActivateOnEnter<DistantClap>()
            // still disabled: "just not appearing, unsure why" (upstream note).
            // The AIDs themselves are definitely live on TC - the 2026-08-16 CFC 290 replay has 16 casts of
            // HundredfoldHavocFirst (10808, 4 Helpers casting in a ring simultaneously) plus 48 cast events of
            // HundredfoldHavocRest (10809), so whatever is wrong is in the Exaflare line-tracking, not the data.
            //.ActivateOnEnter<HundredfoldHavoc>()
            .ActivateOnEnter<AratamaForce>();
    }
}
