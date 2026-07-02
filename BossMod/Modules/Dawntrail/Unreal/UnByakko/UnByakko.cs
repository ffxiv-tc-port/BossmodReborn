// Unreal Byakko (幻白虎征魂戰) - Dawntrail 7.1
// Reuses Ex6Byakko components (same fight mechanics, different CFC/level scaling)
// GroupID 1032 is best-effort; verify against game data if fight doesn't appear in module list
using Ex6 = BossMod.Stormblood.Extreme.Ex6Byakko;

namespace BossMod.Dawntrail.Unreal.UnByakko;

[ModuleInfo(BossModuleInfo.Maturity.WIP, Contributors = "Lother", GroupType = BossModuleInfo.GroupType.CFC, GroupID = 1042, NameID = 7092, PlanLevel = 100)]
public sealed class UnByakko(WorldState ws, Actor primary) : BossModule(ws, primary, default, Ex6.Ex6Byakko.NormalBounds)
{
    private Actor? _hakutei;
    public Actor? Boss() => PrimaryActor;
    public Actor? Hakutei() => _hakutei;

    protected override void UpdateModule()
    {
        // same hack as Ex6Byakko: on wipe, any actor can be deleted and recreated in the same frame
        _hakutei ??= StateMachine.ActivePhaseIndex >= 0 ? Enemies((uint)Ex6.OID.Hakutei).FirstOrDefault() : null;
    }

    protected override void DrawEnemies(int pcSlot, Actor pc)
    {
        Arena.Actor(PrimaryActor);
        Arena.Actor(_hakutei);
    }
}
