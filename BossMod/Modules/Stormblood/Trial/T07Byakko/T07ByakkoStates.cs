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
            // re-enabled 2026-08-16: the "stack marker not being removed properly" note was a symptom of
            // using StackWithIcon (a component for icons that land on PLAYERS) for an icon that lands on a
            // helper actor. Rebuilt on GenericTowers like Ex6Byakko; cleared on the 10806 cast event.
            .ActivateOnEnter<HighestStakes>()
            .ActivateOnEnter<FireAndLightning>()
            .ActivateOnEnter<DistantClap>()
            // re-enabled 2026-08-16: the AIDs were always live on TC (16x 10808 + 48x 10809 in a single
            // replay); the line parameters were wrong (1d / 10 explosions vs the measured 1.1s / 4), so
            // lines never reached ExplosionsLeft == 0 and were never retired. Parameters are now measured
            // from the replays - see the comment on HundredfoldHavoc in T07Byakko.cs.
            .ActivateOnEnter<HundredfoldHavoc>()
            // 荒彈 had no component at all, which is why nothing was drawn for it during the aerial phase.
            // Only the cast-bar variant (10793) is covered; see the note below about 10818.
            .ActivateOnEnter<Aratama>()
            .ActivateOnEnter<AratamaForce>();
        // NOT covered, deliberately, because the replays give no basis for drawing them:
        //  - Aratama2 (10818) is the aerial-phase puddle rain and is the one the live report is about,
        //    but it is still not safely drawable. What IS established from 11 replays: the impact centre
        //    is the cast event's TargetPos (players in the hit list sit 0.01-1.66 from it, so the enum's
        //    "range 2 circle" is right), and 10817 fires earlier at exactly the same point, so 10817
        //    looks like the telegraph. What is NOT established is the delay between them: bursts repeat
        //    at the SAME location every ~2s, so pairing a given 10817 with a given 10818 is ambiguous
        //    (candidate delays cluster at both ~0.1s and ~2.0s). An activation time guessed wrong draws
        //    the circle at the wrong moment, which is worse than drawing nothing, so this waits for a
        //    replay where puddles land at distinct locations and the pairing is unambiguous.
        //  - AratamaForce (OID 0x20F9) never spawns in any of the 11 TC replays, so the Voidzone above
        //    is currently dead code on TC. Left in place: absence is not proof it cannot happen.
        //  - StateOfShock (10070/10208), SteelClaw (10802), WhiteHerald (10828), FellSwoop (10829),
        //    Bombogenesis2 (10811) and UnrelentingAnguish (10221) still have no component.
    }
}
