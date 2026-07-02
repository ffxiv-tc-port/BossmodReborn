// Timeline/mechanics copied from Ex6Byakko (Stormblood Extreme Byakko) - same fight, revisited as Unreal
// TODO: verify timings against real Unreal pull, they may differ slightly from the Extreme version
using Ex6 = BossMod.Stormblood.Extreme.Ex6Byakko;

namespace BossMod.Dawntrail.Unreal.UnByakko;

class UnByakkoStates : StateMachineBuilder
{
    private readonly UnByakko _module;

    public UnByakkoStates(UnByakko module) : base(module)
    {
        _module = module;
        DeathPhase(0, SinglePhase)
            .ActivateOnEnter<Ex6.UnrelentingAnguish>(); // these orbs linger after mechanic ends
    }

    private void SinglePhase(uint id)
    {
        StormPulse(id, 6.2f);
        HeavenlyStrike(id + 0x10000, 2.1f);
        StateOfShock(id + 0x20000, 6.1f);
        UnrelentingAnguish1(id + 0x30000, 11.1f);
        Hakutei1(id + 0x40000, 13.4f);
        Intermission(id + 0x50000, 2.1f);
        HeavenlyStrike(id + 0x60000, 7.2f);
        HundredfoldHavoc1(id + 0x70000, 6.2f);
        UnrelentingAnguish2(id + 0x80000, 11.5f);
        Hakutei2(id + 0x90000, 8.9f); // note: variance is quite large
        StormPulseDouble(id + 0xA0000, 11.2f);
        HundredfoldHavoc2(id + 0xB0000, 8);
        HeavenlyStrike(id + 0xC0000, 10.2f);
        StormPulseDouble(id + 0xD0000, 6.2f);
        DistantClap(id + 0xE0000, 6.1f);
        HeavenlyStrike(id + 0xF0000, 2.1f);
        UnrelentingAnguish2(id + 0x100000, 9.5f);
        StormPulseDouble(id + 0x110000, 10.5f);
        HundredfoldHavoc1(id + 0x120000, 6.1f);
        StormPulseQuadruple(id + 0x130000, 11.5f);
        Cast(id + 0x140000, (uint)Ex6.AID.StormPulseEnrage, 2.1f, 8, "Enrage");
    }

    private State StormPulse(uint id, float delay, string name = "Raidwide")
    {
        return Cast(id, (uint)Ex6.AID.StormPulse, delay, 4, name)
            .SetHint(StateMachine.StateHint.Raidwide);
    }

    private void StormPulseDouble(uint id, float delay)
    {
        StormPulse(id, delay, "Raidwide 1");
        ComponentCondition<Ex6.StormPulseRepeat>(id + 2, 2.2f, comp => comp.NumCasts > 0, "Raidwide 2")
            .ActivateOnEnter<Ex6.StormPulseRepeat>()
            .DeactivateOnExit<Ex6.StormPulseRepeat>()
            .SetHint(StateMachine.StateHint.Raidwide);
    }

    private void StormPulseQuadruple(uint id, float delay)
    {
        StormPulse(id, delay, "Raidwide 1");
        ComponentCondition<Ex6.StormPulseRepeat>(id + 2, 2.2f, comp => comp.NumCasts > 0, "Raidwide 2")
            .ActivateOnEnter<Ex6.StormPulseRepeat>()
            .SetHint(StateMachine.StateHint.Raidwide);
        ComponentCondition<Ex6.StormPulseRepeat>(id + 3, 2.1f, comp => comp.NumCasts > 1, "Raidwide 3")
            .SetHint(StateMachine.StateHint.Raidwide);
        ComponentCondition<Ex6.StormPulseRepeat>(id + 4, 2.1f, comp => comp.NumCasts > 2, "Raidwide 4")
            .DeactivateOnExit<Ex6.StormPulseRepeat>()
            .SetHint(StateMachine.StateHint.Raidwide);
    }

    private State HeavenlyStrike(uint id, float delay)
    {
        return Cast(id, (uint)Ex6.AID.HeavenlyStrike, delay, 4, "Tankbuster")
            .ActivateOnEnter<Ex6.HeavenlyStrike>()
            .DeactivateOnExit<Ex6.HeavenlyStrike>()
            .SetHint(StateMachine.StateHint.Tankbuster);
    }

    private void DistantClap(uint id, float delay)
    {
        Cast(id, (uint)Ex6.AID.DistantClap, delay, 5, "Donut")
            .ActivateOnEnter<Ex6.DistantClap>()
            .DeactivateOnExit<Ex6.DistantClap>();
    }

    private void StateOfShock(uint id, float delay)
    {
        Cast(id, (uint)Ex6.AID.StateOfShock, delay, 4);
        ComponentCondition<Ex6.StateOfShock>(id + 0x10, 0.9f, comp => comp.NumStuns > 0, "Grab tank")
            .ActivateOnEnter<Ex6.StateOfShock>()
            .ActivateOnEnter<Ex6.HighestStakes>();
        Cast(id + 0x20, (uint)Ex6.AID.HighestStakes, 1.5f, 5);
        ComponentCondition<Ex6.HighestStakes>(id + 0x30, 0.8f, comp => comp.NumCasts > 0, "Tower 1")
            .DeactivateOnExit<Ex6.StateOfShock>();
        ComponentCondition<Ex6.StateOfShock>(id + 0x40, 2, comp => comp.NumCasts > 0)
            .ActivateOnEnter<Ex6.StateOfShock>();
        ComponentCondition<Ex6.StateOfShock>(id + 0x41, 0.9f, comp => comp.NumStuns > 0, "Grab tank");
        Cast(id + 0x50, (uint)Ex6.AID.HighestStakes, 1.2f, 5);
        ComponentCondition<Ex6.HighestStakes>(id + 0x60, 0.8f, comp => comp.NumCasts > 1, "Tower 2")
            .DeactivateOnExit<Ex6.StateOfShock>()
            .DeactivateOnExit<Ex6.HighestStakes>();
    }

    private void UnrelentingAnguish1(uint id, float delay)
    {
        Cast(id, (uint)Ex6.AID.UnrelentingAnguish, delay, 3, "Orbs start");
        StormPulse(id + 0x10, 2.2f);
        ComponentCondition<Ex6.OminousWind>(id + 0x20, 2.9f, comp => comp.Targets.Any())
            .ActivateOnEnter<Ex6.OminousWind>();
        Cast(id + 0x30, (uint)Ex6.AID.FireAndLightningBoss, 4.5f, 4, "Line")
            .ActivateOnEnter<Ex6.FireAndLightningBoss>()
            .DeactivateOnExit<Ex6.FireAndLightningBoss>();
        ComponentCondition<Ex6.OminousWind>(id + 0x40, 1.5f, comp => comp.Targets.None(), "Orbs end")
            .DeactivateOnExit<Ex6.OminousWind>();
    }

    private void Hakutei1(uint id, float delay)
    {
        ActorTargetable(id, _module.Hakutei, true, delay, "Tiger appears")
            .ActivateOnEnter<Ex6.AratamaPuddleBait>(); // icons appear ~2s before tiger
        ComponentCondition<Ex6.AratamaPuddleBait>(id + 0x10, 3.1f, comp => comp.NumFinishedSpreads > 0, "Puddle baits start")
            .ActivateOnEnter<Ex6.AratamaPuddleVoidzone>();
        StormPulse(id + 0x20, 4)
            .ActivateOnEnter<Ex6.SteelClaw>()
            .DeactivateOnExit<Ex6.AratamaPuddleBait>();
        ComponentCondition<Ex6.SteelClaw>(id + 0x30, 0.1f, comp => comp.NumCasts > 0, "Cleave 1");
        CastStart(id + 0x40, (uint)Ex6.AID.HeavenlyStrike, 6);
        ComponentCondition<Ex6.SteelClaw>(id + 0x41, 0.1f, comp => comp.NumCasts > 1, "Cleave 2")
            .ActivateOnEnter<Ex6.HeavenlyStrike>()
            .DeactivateOnExit<Ex6.SteelClaw>();
        CastEnd(id + 0x42, 3.9f, "Tankbuster")
            .DeactivateOnExit<Ex6.HeavenlyStrike>()
            .SetHint(StateMachine.StateHint.Tankbuster);
        ActorTargetable(id + 0x50, _module.Hakutei, false, 3.1f, "Tiger disappears");

        ComponentCondition<Ex6.WhiteHerald>(id + 0x60, 0.7f, comp => comp.Active)
            .ActivateOnEnter<Ex6.WhiteHerald>();
        CastStart(id + 0x61, (uint)Ex6.AID.DistantClap, 2.4f);
        ComponentCondition<Ex6.WhiteHerald>(id + 0x62, 2.7f, comp => comp.NumFinishedSpreads > 0, "Flare")
            .ActivateOnEnter<Ex6.DistantClap>()
            .DeactivateOnExit<Ex6.WhiteHerald>();
        ActorTargetable(id + 0x63, _module.Hakutei, true, 2, "Tiger reappears");
        CastEnd(id + 0x64, 0.3f, "Donut")
            .DeactivateOnExit<Ex6.DistantClap>();
        ComponentCondition<Ex6.FireAndLightningAdd>(id + 0x65, 3.9f, comp => comp.NumCasts > 0, "Line") // note: pretty large variance here
            .ActivateOnEnter<Ex6.FireAndLightningAdd>()
            .DeactivateOnExit<Ex6.FireAndLightningAdd>();
        StormPulse(id + 0x70, 0.2f)
            .DeactivateOnExit<Ex6.AratamaPuddleVoidzone>();
    }

    private void Intermission(uint id, float delay)
    {
        ActorTargetable(id, _module.Boss, false, delay, "Boss disappears");
        ActorCast(id + 0x10, _module.Hakutei, (uint)Ex6.AID.RoarOfThunder, 4.4f, 20, true, "Add enrage") // note: pretty large variance here
            .ActivateOnEnter<Ex6.VoiceOfThunder>()
            .DeactivateOnExit<Ex6.VoiceOfThunder>()
            .SetHint(StateMachine.StateHint.Raidwide | StateMachine.StateHint.DowntimeStart);
        ComponentCondition<Ex6.Intermission>(id + 0x12, 5.7f, comp => comp.Active)
            .ActivateOnEnter<Ex6.Intermission>()
            .OnExit(() => Module.Arena.Bounds = Ex6.Ex6Byakko.IntermissionBounds);
        ComponentCondition<Ex6.IntermissionSweepTheLeg>(id + 0x20, 36.5f, comp => comp.NumCasts > 0, "Donut 1")
            .ActivateOnEnter<Ex6.IntermissionOrbAratama>()
            .ActivateOnEnter<Ex6.IntermissionSweepTheLeg>();
        ComponentCondition<Ex6.ImperialGuard>(id + 0x21, 5.7f, comp => comp.NumCasts > 0, "Line 1")
            .ActivateOnEnter<Ex6.ImperialGuard>();
        ComponentCondition<Ex6.ImperialGuard>(id + 0x22, 12, comp => comp.NumCasts > 1, "Line 2");
        ComponentCondition<Ex6.IntermissionSweepTheLeg>(id + 0x23, 13.6f, comp => comp.NumCasts > 1, "Donut 2")
            .DeactivateOnExit<Ex6.IntermissionOrbAratama>()
            .DeactivateOnExit<Ex6.IntermissionSweepTheLeg>();
        ComponentCondition<Ex6.ImperialGuard>(id + 0x24, 3.4f, comp => comp.NumCasts > 2, "Line 3")
            .DeactivateOnExit<Ex6.ImperialGuard>();
        ComponentCondition<Ex6.Intermission>(id + 0x25, 7.5f, comp => !comp.Active)
            .DeactivateOnExit<Ex6.Intermission>()
            .OnExit(() => Module.Arena.Bounds = Ex6.Ex6Byakko.NormalBounds);
        ComponentCondition<Ex6.FellSwoop>(id + 0x26, 20.2f, comp => comp.NumCasts > 0, "Raidwide")
            .ActivateOnEnter<Ex6.FellSwoop>()
            .DeactivateOnExit<Ex6.FellSwoop>()
            .SetHint(StateMachine.StateHint.Raidwide);
        ActorTargetable(id + 0x30, _module.Boss, true, 7.9f, "Boss reappears")
            .SetHint(StateMachine.StateHint.DowntimeEnd);
    }

    private void HundredfoldHavoc1(uint id, float delay)
    {
        ComponentCondition<Ex6.HundredfoldHavoc>(id, delay, comp => comp.Active)
            .ActivateOnEnter<Ex6.HundredfoldHavoc>();
        CastStart(id + 1, (uint)Ex6.AID.StateOfShock, 2.1f);
        ComponentCondition<Ex6.HundredfoldHavoc>(id + 2, 2.9f, comp => comp.NumCasts > 0, "Exaflares start");
        CastEnd(id + 3, 1.1f);

        ComponentCondition<Ex6.StateOfShock>(id + 0x10, 0.9f, comp => comp.NumStuns > 0, "Grab tank")
            .ActivateOnEnter<Ex6.StateOfShock>()
            .ActivateOnEnter<Ex6.HighestStakes>();
        Cast(id + 0x20, (uint)Ex6.AID.HighestStakes, 1.5f, 5)
            .DeactivateOnExit<Ex6.HundredfoldHavoc>();
        ComponentCondition<Ex6.HighestStakes>(id + 0x30, 0.8f, comp => comp.NumCasts > 0, "Tower 1")
            .DeactivateOnExit<Ex6.StateOfShock>();
        ComponentCondition<Ex6.StateOfShock>(id + 0x40, 2, comp => comp.NumCasts > 0)
            .ActivateOnEnter<Ex6.StateOfShock>();
        ComponentCondition<Ex6.StateOfShock>(id + 0x41, 0.9f, comp => comp.NumStuns > 0, "Grab tank");
        Cast(id + 0x50, (uint)Ex6.AID.HighestStakes, 1.3f, 5);
        ComponentCondition<Ex6.HighestStakes>(id + 0x60, 0.8f, comp => comp.NumCasts > 1, "Tower 2")
            .DeactivateOnExit<Ex6.StateOfShock>()
            .DeactivateOnExit<Ex6.HighestStakes>();

        Cast(id + 0x1000, (uint)Ex6.AID.SweepTheLegBoss, 1.9f, 4, "Wide cone")
            .ActivateOnEnter<Ex6.SweepTheLegBoss>()
            .DeactivateOnExit<Ex6.SweepTheLegBoss>();
    }

    private void UnrelentingAnguish2(uint id, float delay)
    {
        Cast(id, (uint)Ex6.AID.UnrelentingAnguish, delay, 3, "Orbs start");
        StormPulseDouble(id + 0x10, 2.1f);

        ComponentCondition<Ex6.GaleForce>(id + 0x20, 2, comp => comp.CurrentBaits.Count > 0)
            .ActivateOnEnter<Ex6.GaleForce>();
        ComponentCondition<Ex6.OminousWind>(id + 0x21, 6.9f, comp => comp.Targets.Any())
            .ActivateOnEnter<Ex6.VacuumClaw>()
            .ActivateOnEnter<Ex6.OminousWind>();
        ComponentCondition<Ex6.GaleForce>(id + 0x22, 1.2f, comp => comp.NumCasts > 0, "Baits")
            .DeactivateOnExit<Ex6.GaleForce>();

        Cast(id + 0x30, (uint)Ex6.AID.FireAndLightningBoss, 3.4f, 4, "Line")
            .ActivateOnEnter<Ex6.FireAndLightningBoss>()
            .DeactivateOnExit<Ex6.FireAndLightningBoss>();
        ComponentCondition<Ex6.OminousWind>(id + 0x40, 1.5f, comp => comp.Targets.None(), "Orbs end")
            .DeactivateOnExit<Ex6.OminousWind>();

        Cast(id + 0x50, (uint)Ex6.AID.FireAndLightningBoss, 3.4f, 4, "Line")
            .ActivateOnEnter<Ex6.FireAndLightningBoss>()
            .DeactivateOnExit<Ex6.FireAndLightningBoss>();
        ComponentCondition<Ex6.VacuumClaw>(id + 0x60, 0.7f, comp => !comp.Active, "Voidzones end")
            .DeactivateOnExit<Ex6.VacuumClaw>();
    }

    private void Hakutei2(uint id, float delay)
    {
        ActorTargetable(id, _module.Hakutei, true, delay, "Tiger appears")
            .ActivateOnEnter<Ex6.AratamaPuddleBait>(); // icons appear ~2s before tiger

        ComponentCondition<Ex6.AratamaPuddleBait>(id + 0x10, 3.1f, comp => comp.NumFinishedSpreads > 0, "Puddle baits start")
            .ActivateOnEnter<Ex6.AratamaPuddleVoidzone>();
        ActorTargetable(id + 0x20, _module.Hakutei, false, 6, "Tiger disappears");

        ComponentCondition<Ex6.WhiteHerald>(id + 0x30, 0.7f, comp => comp.Active)
            .ActivateOnEnter<Ex6.WhiteHerald>();
        CastStart(id + 0x31, (uint)Ex6.AID.DistantClap, 2.4f);
        ComponentCondition<Ex6.WhiteHerald>(id + 0x32, 2.7f, comp => comp.NumFinishedSpreads > 0, "Flare")
            .ActivateOnEnter<Ex6.DistantClap>()
            .DeactivateOnExit<Ex6.WhiteHerald>();
        ActorTargetable(id + 0x33, _module.Hakutei, true, 2, "Tiger reappears");
        CastEnd(id + 0x34, 0.3f, "Donut 1")
            .DeactivateOnExit<Ex6.DistantClap>();

        CastStart(id + 0x40, (uint)Ex6.AID.DistantClap, 3.2f)
            .ActivateOnEnter<Ex6.FireAndLightningAdd>();
        ComponentCondition<Ex6.FireAndLightningAdd>(id + 0x41, 0.7f, comp => comp.NumCasts > 0, "Line 1")
            .ActivateOnEnter<Ex6.DistantClap>()
            .DeactivateOnExit<Ex6.FireAndLightningAdd>();
        CastEnd(id + 0x42, 4.3f, "Donut 2")
            .DeactivateOnExit<Ex6.DistantClap>();
        ComponentCondition<Ex6.FireAndLightningAdd>(id + 0x50, 4, comp => comp.NumCasts > 0, "Line 2")
            .ActivateOnEnter<Ex6.FireAndLightningAdd>()
            .DeactivateOnExit<Ex6.FireAndLightningAdd>();

        HeavenlyStrike(id + 0x100, 3.2f)
            .ActivateOnEnter<Ex6.SteelClaw>()
            .DeactivateOnExit<Ex6.SteelClaw>();

        ActorCastStart(id + 0x200, _module.Hakutei, (uint)Ex6.AID.RoarOfThunder, 10.6f, false)
            .ActivateOnEnter<Ex6.VoiceOfThunder>()
            .DeactivateOnExit<Ex6.AratamaPuddleVoidzone>();
        StormPulseDouble(id + 0x210, 5.6f);
        ActorCastEnd(id + 0x220, _module.Hakutei, 8.2f, false, "Add enrage")
            .DeactivateOnExit<Ex6.VoiceOfThunder>()
            .SetHint(StateMachine.StateHint.Raidwide);

        Cast(id + 0x300, (uint)Ex6.AID.FireAndLightningBoss, 8.3f, 4, "Line")
            .ActivateOnEnter<Ex6.FireAndLightningBoss>()
            .DeactivateOnExit<Ex6.FireAndLightningBoss>();
    }

    private void HundredfoldHavoc2(uint id, float delay)
    {
        ComponentCondition<Ex6.GaleForce>(id, delay, comp => comp.CurrentBaits.Count > 0)
            .ActivateOnEnter<Ex6.GaleForce>();

        ComponentCondition<Ex6.HundredfoldHavoc>(id + 0x10, 2.2f, comp => comp.Active)
            .ActivateOnEnter<Ex6.HundredfoldHavoc>();
        CastStart(id + 0x11, (uint)Ex6.AID.StateOfShock, 4.6f);
        ComponentCondition<Ex6.HundredfoldHavoc>(id + 0x12, 0.4f, comp => comp.NumCasts > 0, "Exaflares start");
        ComponentCondition<Ex6.GaleForce>(id + 0x13, 0.9f, comp => comp.NumCasts > 0, "Baits")
            .ActivateOnEnter<Ex6.VacuumClaw>()
            .DeactivateOnExit<Ex6.GaleForce>();
        CastEnd(id + 0x14, 2.7f);

        ComponentCondition<Ex6.StateOfShock>(id + 0x20, 0.9f, comp => comp.NumStuns > 0, "Grab tank")
            .ActivateOnEnter<Ex6.StateOfShock>()
            .ActivateOnEnter<Ex6.HighestStakes>();
        Cast(id + 0x30, (uint)Ex6.AID.HighestStakes, 1.3f, 5)
            .DeactivateOnExit<Ex6.HundredfoldHavoc>();
        ComponentCondition<Ex6.HighestStakes>(id + 0x40, 0.8f, comp => comp.NumCasts > 0, "Tower 1")
            .DeactivateOnExit<Ex6.StateOfShock>();
        ComponentCondition<Ex6.StateOfShock>(id + 0x50, 2, comp => comp.NumCasts > 0)
            .ActivateOnEnter<Ex6.StateOfShock>();
        ComponentCondition<Ex6.StateOfShock>(id + 0x51, 0.9f, comp => comp.NumStuns > 0, "Grab tank");
        Cast(id + 0x60, (uint)Ex6.AID.HighestStakes, 1.2f, 5)
            .DeactivateOnExit<Ex6.VacuumClaw>();
        ComponentCondition<Ex6.HighestStakes>(id + 0x70, 0.8f, comp => comp.NumCasts > 1, "Tower 2")
            .DeactivateOnExit<Ex6.StateOfShock>()
            .DeactivateOnExit<Ex6.HighestStakes>();

        Cast(id + 0x1000, (uint)Ex6.AID.SweepTheLegBoss, 1.9f, 4, "Wide cone")
            .ActivateOnEnter<Ex6.SweepTheLegBoss>()
            .DeactivateOnExit<Ex6.SweepTheLegBoss>();
    }
}
