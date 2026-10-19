// Unreal Byakko (幻白虎征魂戰) - Dawntrail 7.1. Mechanics mirror Ex6Byakko (Extreme Byakko); IDs come
// from a captured combat log (see UnByakkoEnums.cs). TODO: mechanics whose AIDs never showed up in
// the log are not fabricated - UnByakkoStates.cs keeps them as Timeout placeholders at the same
// relative position as in Ex6's timeline. Arena bounds are borrowed from Ex6Byakko.
namespace BossMod.Dawntrail.Unreal.UnByakko;

using Ex6 = BossMod.Stormblood.Extreme.Ex6Byakko;

// boss's own attack shapes - real confirmed AIDs, shapes mirrored from Ex6Byakko's equivalent moves
class StormPulse(BossModule module) : Components.RaidwideCast(module, (uint)AID.StormPulse);
class FireAndLightningBoss(BossModule module) : Components.SimpleAOEs(module, (uint)AID.FireAndLightningBoss, new AOEShapeRect(54.3f, 10f)); // shape mirrored from Ex6.FireAndLightningBoss
class HeavenlyStrike(BossModule module) : Components.BaitAwayCast(module, (uint)AID.HeavenlyStrike, 3f);
class SweepTheLegBoss(BossModule module) : Components.SimpleAOEs(module, (uint)AID.SweepTheLegBoss, new AOEShapeCone(28.3f, 135f.Degrees())); // shape mirrored from Ex6.SweepTheLegBoss
class DistantClap(BossModule module) : Components.SimpleAOEs(module, (uint)AID.DistantClap, new AOEShapeDonut(4f, 25f)); // shape mirrored from Ex6.DistantClap
class FireAndLightningAdd(BossModule module) : Components.SimpleAOEs(module, (uint)AID.FireAndLightningAdd, new AOEShapeRect(54.75f, 10f)); // shape mirrored from Ex6.FireAndLightningAdd
class RoarOfThunder(BossModule module) : Components.RaidwideCast(module, (uint)AID.RoarOfThunder, "Add enrage");
class ImperialGuard(BossModule module) : Components.SimpleAOEs(module, (uint)AID.ImperialGuard, new AOEShapeRect(44.75f, 2.5f)); // shape mirrored from Ex6.ImperialGuard
class IntermissionSweepTheLeg(BossModule module) : Components.SimpleAOEs(module, (uint)AID.IntermissionSweepTheLeg, new AOEShapeDonut(5f, 25f)); // shape mirrored from Ex6.IntermissionSweepTheLeg
class HundredfoldHavoc(BossModule module) : Components.CastCounter(module, (uint)AID.HundredfoldHavoc); // TODO: real fight is a true Exaflare (Ex6.HundredfoldHavoc); simplified to a cast counter since we never observed the first-vs-rest split needed to drive Components.Exaflare correctly

// StateOfShock/HighestStakes (grab+throw+tower) - real confirmed AIDs, logic mirrored from Ex6
class StateOfShockGrab(BossModule module) : Components.CastCounter(module, (uint)AID.StateOfShockGrab);
class HighestStakes(BossModule module) : Components.SimpleAOEs(module, (uint)AID.HighestStakes, new AOEShapeCircle(6f)); // TODO: real mechanic is a tower share (Ex6.HighestStakes via GenericTowers + icon 62); simplified to a plain AOE until the tower-share soak logic can be verified

// The duty rotated out with 7.2: ContentFinderCondition row 1007 (the id upstream's Un1Byakko used) is an
// empty row in the TC sheet. RemovedUnreal is the group for that case and, unlike GroupType.CFC, does no
// Lumina row lookup (ModuleViewer.Classify dereferences the CFC row unchecked for every registered module).
[ModuleInfo(BossModuleInfo.Maturity.WIP, GroupType = BossModuleInfo.GroupType.RemovedUnreal, GroupID = 1007, NameID = 7092, PlanLevel = 100)]
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
