// Unreal Byakko (幻白虎征魂戰) - Dawntrail 7.1
// TODO: mechanics structurally mirror Ex6Byakko (Extreme Byakko) 1:1 per user confirmation, but only
// ~13 of Ex6's ~20 distinct AIDs could be positively correlated from the captured log (see
// UnByakkoEnums.cs for the confirmed mapping and comments). The orb/OminousWind bubble mechanic,
// AratamaPuddle bait, WhiteHerald spread resolution, GaleForce/VacuumClaw voidzones, the boss/add
// line AOEs (FireAndLightning-equivalent) and FellSwoop's resolution AID were never observed with
// their own CST+ entry (some may be instant, no-cast-bar abilities that don't appear in this log
// format at all) - those are represented as Timeout-based placeholders in UnByakkoStates.cs, kept in
// the same relative position as Ex6's timeline, instead of being fabricated or omitted outright.
// Arena bounds are borrowed directly from Ex6Byakko since geometry is very likely shared 1:1.
namespace BossMod.Dawntrail.Unreal.UnByakko;

using Ex6 = BossMod.Stormblood.Extreme.Ex6Byakko;

// boss's own attack shapes - real confirmed AIDs, shapes mirrored from Ex6Byakko's equivalent moves
class StormPulse(BossModule module) : Components.RaidwideCast(module, (uint)AID.StormPulse);
class StormPulseRepeat(BossModule module) : Components.RaidwideCast(module, (uint)AID.StormPulseRepeat, "Raidwide (repeat)");
class HeavenlyStrike(BossModule module) : Components.BaitAwayCast(module, (uint)AID.HeavenlyStrike, 3f);
class SweepTheLegBoss(BossModule module) : Components.SimpleAOEs(module, (uint)AID.SweepTheLegBoss, new AOEShapeCone(28.3f, 135f.Degrees())); // shape mirrored from Ex6.SweepTheLegBoss
class DistantClap(BossModule module) : Components.SimpleAOEs(module, (uint)AID.DistantClap, new AOEShapeDonut(4f, 25f)); // shape mirrored from Ex6.DistantClap
class SteelClaw(BossModule module) : Components.Cleave(module, (uint)AID.SteelClaw, new AOEShapeCone(17.75f, 60f.Degrees()), [(uint)OID.Hakutei]); // shape mirrored from Ex6.SteelClaw
class RoarOfThunder(BossModule module) : Components.RaidwideCast(module, (uint)AID.RoarOfThunder, "Add enrage");
class ImperialGuard(BossModule module) : Components.SimpleAOEs(module, (uint)AID.ImperialGuard, new AOEShapeRect(44.75f, 2.5f)); // shape mirrored from Ex6.ImperialGuard
class IntermissionSweepTheLeg(BossModule module) : Components.SimpleAOEs(module, (uint)AID.IntermissionSweepTheLeg, new AOEShapeDonut(5f, 25f)); // shape mirrored from Ex6.IntermissionSweepTheLeg
class HundredfoldHavoc(BossModule module) : Components.CastCounter(module, (uint)AID.HundredfoldHavoc); // TODO: real fight is a true Exaflare (Ex6.HundredfoldHavoc); simplified to a cast counter since we never observed the first-vs-rest split needed to drive Components.Exaflare correctly

// StateOfShock/HighestStakes (grab+throw+tower) - real confirmed AIDs, logic mirrored from Ex6
class StateOfShockGrab(BossModule module) : Components.CastCounter(module, (uint)AID.StateOfShockGrab);
class HighestStakes(BossModule module) : Components.SimpleAOEs(module, (uint)AID.HighestStakes, new AOEShapeCircle(6f)); // TODO: real mechanic is a tower share (Ex6.HighestStakes via GenericTowers + icon 62); simplified to a plain AOE until the tower-share soak logic can be verified

// TODO: GroupID is an unverified guess for the real ContentFinderCondition row (The Jade Stoa (Unreal)).
// Using GroupType.None for now: GroupType.CFC does an unchecked Lumina row lookup at plugin startup for
// EVERY registered module (BossMod/Config/ModuleViewer.cs Classify()), so a wrong CFC id crashes the
// entire plugin on load, not just this module. Switch back to GroupType.CFC once the real row id is confirmed.
[ModuleInfo(BossModuleInfo.Maturity.WIP, Contributors = "Lother", GroupType = BossModuleInfo.GroupType.None, GroupID = 1042, NameID = 7092, PlanLevel = 100)]
public sealed class UnByakko(WorldState ws, Actor primary) : BossModule(ws, primary, default, Ex6.Ex6Byakko.NormalBounds)
{
    // borrowed directly from Ex6Byakko - arena geometry is very likely shared 1:1 between Unreal and Extreme
    public static readonly ArenaBoundsComplex NormalBounds = Ex6.Ex6Byakko.NormalBounds;
    public static readonly ArenaBoundsComplex IntermissionBounds = Ex6.Ex6Byakko.IntermissionBounds;

    private Actor? _hakutei;
    public Actor? Boss() => PrimaryActor;
    public Actor? Hakutei() => _hakutei;

    protected override void UpdateModule()
    {
        // same hack as Ex6Byakko: on wipe, any actor can be deleted and recreated in the same frame
        _hakutei ??= StateMachine.ActivePhaseIndex >= 0 ? Enemies((uint)OID.Hakutei).FirstOrDefault() : null;
    }

    protected override void DrawEnemies(int pcSlot, Actor pc)
    {
        Arena.Actor(PrimaryActor);
        Arena.Actor(_hakutei);
    }
}
