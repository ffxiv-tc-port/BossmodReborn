// Timeline reconstructed from a real combat log capture (log/_VPR100_Lo_U_2026_07_14_23_54_41.log),
// structurally aligned against Ex6ByakkoByakkoStates.cs's SinglePhase call sequence per the user's
// confirmation that Unreal Byakko's mechanics are a 1:1 reskin of Ex6Byakko (Extreme Byakko).
// Confirmed slots (StormPulse, HeavenlyStrike, StateOfShock/HighestStakes grab+tower pair,
// SweepTheLegBoss, DistantClap, SteelClaw, RoarOfThunder, ImperialGuard x3,
// IntermissionSweepTheLeg, HundredfoldHavoc exaflare ring) use real AIDs seen in CST+ lines.
// Slots with NO log evidence of a real AID (UnrelentingAnguish's OminousWind bubble sub-mechanic,
// AratamaPuddle bait/voidzone, WhiteHerald spread, FireAndLightning line AOEs, GaleForce/VacuumClaw
// baited voidzones, FellSwoop raidwide) are marked "TODO: AID unconfirmed, no log evidence, assumed
// identical to Ex6 pending real data" and kept as Timeout placeholders in the same relative position
// as Ex6's timeline, per explicit instruction to not omit mechanics the user says exist.
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
        // opener, ~54:41 pull -> first cast ~55:24 (43s prep)
        StormPulse(id, 43);
        HeavenlyStrike(id + 0x10000, 6.2f);
        StateOfShock(id + 0x20000, 10.2f);
        UnrelentingAnguish1(id + 0x30000, 16.8f);
        Hakutei1(id + 0x40000, 5.1f);
        HeavenlyStrike(id + 0x50000, 26.1f);
        HundredfoldHavoc1(id + 0x60000, 10);

        // second rotation post-intermission
        HeavenlyStrike(id + 0x70000, 20.5f); // ~59:56, after ~2m20s downtime (log-confirmed gap)
        StormPulseDouble(id + 0x80000, 12.3f);
        UnrelentingAnguish2(id + 0x90000, 9.1f);
        HundredfoldHavoc2(id + 0xA0000, 15.3f);
        HeavenlyStrike(id + 0xB0000, 5.1f);
        DistantClap(id + 0xC0000, 19.6f);

        // TODO: log capture ends shortly after this point (pull ended ~00:06:23); enrage and any
        // further mechanics beyond this repeating rotation are unconfirmed. Placeholder trivial tail
        // so the timeline doesn't dead-end; replace with a real enrage cast once a full clear log is available.
        Timeout(id + 0xD0000, 60, "Enrage (unconfirmed)")
            .SetHint(StateMachine.StateHint.Raidwide);
    }

    private State StormPulse(uint id, float delay, string name = "Raidwide")
    {
        return Cast(id, (uint)AID.StormPulse, delay, 3.7f, name)
            .ActivateOnEnter<StormPulse>()
            .SetHint(StateMachine.StateHint.Raidwide);
    }

    private void StormPulseDouble(uint id, float delay)
    {
        StormPulse(id, delay, "Raidwide 1");
        Cast(id + 0x10, (uint)AID.StormPulseRepeat, 9, 3.7f, "Raidwide 2")
            .ActivateOnEnter<StormPulseRepeat>()
            .DeactivateOnExit<StormPulse>()
            .DeactivateOnExit<StormPulseRepeat>()
            .SetHint(StateMachine.StateHint.Raidwide);
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

    // matches Ex6's StateOfShock: grab a tank, throw them, tower drop x2 - confirmed via 39937 (grab)
    // followed by 39939 (throw/tower) observed twice ~10s apart, both times
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
        StormPulseDouble(id + 0x10, 2.1f);
        Timeout(id + 0x20, 2, "Baited voidzone (unconfirmed)");
        Timeout(id + 0x30, 6.9f, "Bubbles (unconfirmed)");
        Timeout(id + 0x40, 4.5f, "Line (unconfirmed)")
            .SetHint(StateMachine.StateHint.Raidwide);
        Timeout(id + 0x50, 4.5f, "Line (unconfirmed)")
            .SetHint(StateMachine.StateHint.Raidwide);
        Timeout(id + 0x60, 1.5f, "Orbs/voidzones end (unconfirmed)");
    }

    // add appears (already present whole fight in this log, unlike Ex6's spawn-in) + cleave + real intermission
    private void Hakutei1(uint id, float delay)
    {
        Cast(id, (uint)AID.SteelClaw, delay, 3.7f, "Cleave")
            .ActivateOnEnter<SteelClaw>()
            .DeactivateOnExit<SteelClaw>();
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
