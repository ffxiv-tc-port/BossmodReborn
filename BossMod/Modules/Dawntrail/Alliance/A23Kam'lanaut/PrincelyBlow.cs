namespace BossMod.Dawntrail.Alliance.A23Kamlanaut;

sealed class PrincelyBlow(BossModule module) : Components.BaitAwayIcon(module, new AOEShapeRect(60f, 5f), (uint)IconID.PrincelyBlow, (uint)AID.PrincelyBlow, 8.3d, tankbuster: true, damageType: AIHints.PredictedDamageType.Tankbuster);

// wall behaviour is fixed per instance in this fork (StopAtWall/StopAfterWall are readonly), so one instance handles the solid p1 wall
// and the other the p2 platform edge; whichever matches the arena when the icon appears owns that tankbuster
abstract class PrincelyBlowKB(BossModule module, bool platformEdge) : Components.GenericKnockback(module, stopAtWall: !platformEdge, stopAfterWall: platformEdge)
{
    private readonly ShieldBash shieldBash = module.FindComponent<ShieldBash>()!;
    public override ReadOnlySpan<Knockback> ActiveKnockbacks(int slot, Actor actor) => active && targets[slot] ? _kb : [];
    private Knockback[] _kb = [];
    private BitMask targets;
    private bool active;

    public override void OnEventIcon(Actor actor, uint iconID, ulong targetID)
    {
        if (iconID == (uint)IconID.PrincelyBlow)
        {
            if (targets == default)
            {
                _kb = [new(A23Kamlanaut.ArenaCenter.Quantized(), 30f, WorldState.FutureTime(8.3d))];
                active = (Arena.Bounds.Radius != 29.5f) == StopAfterWall;
            }
            targets.Set(Raid.FindSlot(targetID));
        }
    }

    public override void OnEventCast(Actor caster, ActorCastEvent spell)
    {
        if (spell.Action.ID == (uint)AID.PrincelyBlow)
        {
            targets = default;
            _kb = [];
            active = false;
        }
    }

    public override void AddAIHints(int slot, Actor actor, PartyRolesConfig.Assignment assignment, AIHints hints)
    {
        if (active && targets[slot] && Arena.Bounds.Radius > 30f)
        {
            ref readonly var kb = ref _kb[0];
            var act = kb.Activation;
            if (!IsImmune(slot, act))
            {
                // if possible press Arms Length so there are more places to go with the tankbuster
                hints.ActionsToExecute.Push(ActionID.MakeSpell(ClassShared.AID.ArmsLength), actor, ActionQueue.Priority.High);
                if (!shieldBash.PolygonInit)
                {
                    shieldBash.Polygon = A23Kamlanaut.P2ArenaWithBridges.poly.Offset(-1f); // pretend polygon is 1y smaller than real for less suspect knockbacks
                    shieldBash.PolygonInit = true;
                }
                var origin = kb.Origin;
                var center = Arena.Center;
                var poly = shieldBash.Polygon;
                hints.AddForbiddenZone(p =>
                {
                    if (poly.Contains(p - center + 30f * (p - origin).Normalized()))
                    {
                        return 1f;
                    }
                    return default;
                }, act);
            }
        }
    }
}

sealed class PrincelyBlowKBWall(BossModule module) : PrincelyBlowKB(module, false);
sealed class PrincelyBlowKBPlatformEdge(BossModule module) : PrincelyBlowKB(module, true);
