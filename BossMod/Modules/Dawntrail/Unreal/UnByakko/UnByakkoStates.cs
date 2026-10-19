// Timeline reconstructed from a combat log capture, aligned against the SinglePhase sequence in
// Ex6ByakkoByakkoStates.cs (Unreal Byakko is a reskin of Ex6Byakko). Slots with a real AID in the log
// use it; slots without one (OminousWind bubbles, AratamaPuddle, WhiteHerald,
// GaleForce/VacuumClaw, FellSwoop) stay as "(unconfirmed)" Timeout placeholders at the same relative
// position as in Ex6 - mechanics are not dropped just because their AID is unknown.
namespace BossMod.Dawntrail.Unreal.UnByakko;

class UnByakkoStates : StateMachineBuilder
{
    private readonly UnByakko _module;

    public UnByakkoStates(UnByakko module) : base(module)
    {
        _module = module;
        DeathPhase(0, SinglePhase);
    }

    private void SinglePhase(uint id)
    {
        // opener (43s from pull to first cast)
        StormPulse(id, 43);
        HeavenlyStrike(id + 0x10000, 6.2f);
        StateOfShock(id + 0x20000, 10.2f);
        UnrelentingAnguish1(id + 0x30000, 16.8f);
        Hakutei1(id + 0x40000, 5.1f);
        HeavenlyStrike(id + 0x50000, 26.1f);
        HundredfoldHavoc1(id + 0x60000, 10);

        // second rotation post-intermission
        HeavenlyStrike(id + 0x70000, 20.5f);
        StormPulseThenLine(id + 0x80000, 12.3f);
        UnrelentingAnguish2(id + 0x90000, 9.1f);
        HundredfoldHavoc2(id + 0xA0000, 15.3f);
        HeavenlyStrike(id + 0xB0000, 5.1f);
        DistantClap(id + 0xC0000, 19.6f);

        // TODO: the log capture ends shortly after this point; enrage and any further mechanics beyond
        // this rotation are unconfirmed. Placeholder tail so the timeline doesn't dead-end; replace with
        // a real enrage cast once a full clear log is available.
        Timeout(id + 0xD0000, 60, "Enrage (unconfirmed)")
            .SetHint(StateMachine.StateHint.Raidwide);
    }

    private State StormPulse(uint id, float delay, string name = "Raidwide")
    {
        return Cast(id, (uint)AID.StormPulse, delay, 3.7f, name)
            .ActivateOnEnter<StormPulse>()
            .SetHint(StateMachine.StateHint.Raidwide);
    }

    // the cast that follows the raidwide here is the boss's line AOE (FireAndLightningBoss), not a second raidwide
    private void StormPulseThenLine(uint id, float delay)
    {
        StormPulse(id, delay);
        Cast(id + 0x10, (uint)AID.FireAndLightningBoss, 9, 3.7f, "Line")
            .ActivateOnEnter<FireAndLightningBoss>()
            .DeactivateOnExit<StormPulse>()
            .DeactivateOnExit<FireAndLightningBoss>();
    }

    private State HeavenlyStrike(uint id, float delay)
    {
        return Cast(id, (uint)AID.HeavenlyStrike, delay, 3.7f, "Tankbuster")
            .ActivateOnEnter<HeavenlyStrike>()
            .DeactivateOnExit<HeavenlyStrike>()
            .SetHint(StateMachine.StateHint.Tankbuster);
    }

    private void DistantClap(uint id, float delay)
    {
        Cast(id, (uint)AID.DistantClap, delay, 4.7f, "Donut")
            .ActivateOnEnter<DistantClap>()
            .DeactivateOnExit<DistantClap>();
    }

    // matches Ex6's StateOfShock: grab a tank (39937), throw them, tower drop (39939) - twice, ~10s apart
    private void StateOfShock(uint id, float delay)
    {
        Cast(id, (uint)AID.StateOfShockGrab, delay, 3.7f, "Grab tank")
            .ActivateOnEnter<StateOfShockGrab>();
        Cast(id + 0x10, (uint)AID.HighestStakes, 5, 4.7f, "Tower 1")
            .ActivateOnEnter<HighestStakes>();
        Cast(id + 0x20, (uint)AID.StateOfShockGrab, 5, 3.7f, "Grab tank");
        Cast(id + 0x30, (uint)AID.HighestStakes, 5, 4.7f, "Tower 2")
            .DeactivateOnExit<StateOfShockGrab>()
            .DeactivateOnExit<HighestStakes>();
    }

    // TODO: AID unconfirmed, no log evidence, assumed identical to Ex6 pending real data.
    // Ex6's UnrelentingAnguish1: orb visual, extra raidwide pulse, OminousWind bubbles, line AOE.
    // We only have real AIDs for the "orbs start" visual and the extra raidwide pulse.
    private void UnrelentingAnguish1(uint id, float delay)
    {
        Cast(id, (uint)AID.UnrelentingAnguishStart, delay, 2.7f, "Orbs start");
        StormPulse(id + 0x10, 2.2f);
        Timeout(id + 0x20, 2.9f, "Bubbles (unconfirmed)");
        Timeout(id + 0x30, 4.5f, "Line (unconfirmed)")
            .SetHint(StateMachine.StateHint.Raidwide);
        Timeout(id + 0x40, 1.5f, "Orbs end (unconfirmed)");
    }

    private void UnrelentingAnguish2(uint id, float delay)
    {
        Cast(id, (uint)AID.UnrelentingAnguishStart, delay, 2.7f, "Orbs start");
        StormPulseThenLine(id + 0x10, 2.1f);
        Timeout(id + 0x20, 2, "Baited voidzone (unconfirmed)");
        Timeout(id + 0x30, 6.9f, "Bubbles (unconfirmed)");
        Timeout(id + 0x40, 4.5f, "Line (unconfirmed)")
            .SetHint(StateMachine.StateHint.Raidwide);
        Timeout(id + 0x50, 4.5f, "Line (unconfirmed)")
            .SetHint(StateMachine.StateHint.Raidwide);
        Timeout(id + 0x60, 1.5f, "Orbs/voidzones end (unconfirmed)");
    }

    // add appears (already present whole fight in this log, unlike Ex6's spawn-in) + its line AOE + real intermission
    private void Hakutei1(uint id, float delay)
    {
        // cast by the add, not by the primary actor
        ActorCast(id, _module.Hakutei, (uint)AID.FireAndLightningAdd, delay, 3.7f, false, "Line")
            .ActivateOnEnter<FireAndLightningAdd>()
            .DeactivateOnExit<FireAndLightningAdd>();
        Timeout(id + 0x10, 3, "Spread (unconfirmed, WhiteHerald-equivalent)");
        Timeout(id + 0x20, 5, "Puddle bait (unconfirmed, AratamaPuddle-equivalent)");
        Intermission(id + 0x30, 4.4f);
    }

    // real intermission: boss becomes untargetable, add casts RoarOfThunder (raidwide enrage-style),
    // 3x ImperialGuard line AOE + 2x IntermissionSweepTheLeg donut confirmed from log, boss returns
    private void Intermission(uint id, float delay)
    {
        ActorTargetable(id, _module.Boss, false, delay, "Boss disappears");
        ActorCast(id + 0x10, _module.Hakutei, (uint)AID.RoarOfThunder, 4.4f, 19.7f, true, "Add enrage")
            .SetHint(StateMachine.StateHint.Raidwide | StateMachine.StateHint.DowntimeStart);
        Cast(id + 0x20, (uint)AID.IntermissionSweepTheLeg, 36.5f, 5.1f, "Donut 1")
            .ActivateOnEnter<IntermissionSweepTheLeg>();
        Cast(id + 0x21, (uint)AID.ImperialGuard, 5.7f, 3f, "Line 1")
            .ActivateOnEnter<ImperialGuard>();
        Cast(id + 0x22, (uint)AID.ImperialGuard, 12, 3f, "Line 2");
        Cast(id + 0x23, (uint)AID.IntermissionSweepTheLeg, 13.6f, 5.1f, "Donut 2")
            .DeactivateOnExit<IntermissionSweepTheLeg>();
        Cast(id + 0x24, (uint)AID.ImperialGuard, 3.4f, 3f, "Line 3")
            .DeactivateOnExit<ImperialGuard>();
        Timeout(id + 0x26, 20.2f, "Raidwide (unconfirmed, FellSwoop-equivalent)")
            .SetHint(StateMachine.StateHint.Raidwide);
        ActorTargetable(id + 0x30, _module.Boss, true, 7.9f, "Boss reappears")
            .SetHint(StateMachine.StateHint.DowntimeEnd);
    }

    // exaflare ring cast by 4 BossHelper clones simultaneously (confirmed AID 39942), followed by
    // StateOfShock and SweepTheLegBoss wide cone, matching Ex6's HundredfoldHavoc1/2 structure
    private void HundredfoldHavoc1(uint id, float delay)
    {
        Cast(id, (uint)AID.HundredfoldHavoc, delay, 0, "Exaflare ring 1")
            .ActivateOnEnter<HundredfoldHavoc>();
        Cast(id + 0x10, (uint)AID.HundredfoldHavoc, 3.1f, 0, "Exaflare ring 2")
            .DeactivateOnExit<HundredfoldHavoc>();
        StateOfShock(id + 0x20, 5);
        Cast(id + 0x1000, (uint)AID.SweepTheLegBoss, 1.9f, 3.7f, "Wide cone")
            .ActivateOnEnter<SweepTheLegBoss>()
            .DeactivateOnExit<SweepTheLegBoss>();
    }

    private void HundredfoldHavoc2(uint id, float delay)
    {
        Timeout(id, delay, "Baited voidzone (unconfirmed, GaleForce-equivalent)");
        Cast(id + 0x10, (uint)AID.HundredfoldHavoc, 2.2f, 0, "Exaflare ring 1")
            .ActivateOnEnter<HundredfoldHavoc>();
        Cast(id + 0x11, (uint)AID.HundredfoldHavoc, 3.1f, 0, "Exaflare ring 2")
            .DeactivateOnExit<HundredfoldHavoc>();
        StateOfShock(id + 0x20, 5);
        Cast(id + 0x1000, (uint)AID.SweepTheLegBoss, 1.9f, 3.7f, "Wide cone")
            .ActivateOnEnter<SweepTheLegBoss>()
            .DeactivateOnExit<SweepTheLegBoss>();
    }
}
