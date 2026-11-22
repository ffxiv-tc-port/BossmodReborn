namespace BossMod.Heavensward.Alliance.A11Cetus;

class ElectricSwipe(BossModule module) : Components.SimpleAOEs(module, (uint)AID.ElectricSwipe, new AOEShapeCone(25, 30.Degrees()));
class BodySlam(BossModule module) : Components.SimpleAOEs(module, (uint)AID.BodySlam, 10);
class Immersion(BossModule module) : Components.RaidwideCast(module, (uint)AID.Immersion);
class ElectricWhorl(BossModule module) : Components.SimpleAOEs(module, (uint)AID.ElectricWhorl, new AOEShapeDonut(7, 60));
class ExpulsionAOE(BossModule module) : Components.SimpleAOEs(module, (uint)AID.Expulsion, 14);
class ExpulsionKnockback(BossModule module) : Components.SimpleKnockbacks(module, (uint)AID.Expulsion, 30, stopAtWall: true);
class BiteAndRun(BossModule module) : Components.ChargeAOEs(module, (uint)AID.BiteAndRun, 2.5f);

// Each living Hydrosphere (outside the barrier) gives the Hydrocore (inside) one Vulnerability Down stack.
// Only players closer to a Hydrosphere than to the Hydrocore are sent at the spheres; the party pulled inside keeps the core.
sealed class Hydrospheres(BossModule module) : BossComponent(module)
{
    private readonly List<Actor> _cores = module.Enemies((uint)OID.Hydrocore);
    private readonly List<Actor> _spheres = module.Enemies((uint)OID.Hydrosphere);

    private static Actor? FirstAlive(List<Actor> actors)
    {
        var count = actors.Count;
        for (var i = 0; i < count; ++i)
        {
            var a = actors[i];
            if (!a.IsDead)
                return a;
        }
        return null;
    }

    public override void AddGlobalHints(GlobalHints hints)
    {
        if (FirstAlive(_cores) is Actor core && core.FindStatus((uint)SID.VulnerabilityDown) is ActorStatus s)
            hints.Add(string.Format(Loc.T("{0} has damage reduction x{1}: kill the tethered adds outside the barrier first!"), core.Name, s.Extra & 0xFF));
    }

    public override void AddAIHints(int slot, Actor actor, PartyRolesConfig.Assignment assignment, AIHints hints)
    {
        if (FirstAlive(_cores) is not Actor core)
            return;
        var coreDistSq = (core.Position - actor.Position).LengthSq();
        var count = _spheres.Count;
        for (var i = 0; i < count; ++i)
        {
            var s = _spheres[i];
            if (!s.IsDead && (s.Position - actor.Position).LengthSq() < coreDistSq)
            {
                hints.PrioritizeTargetsByOID((uint)OID.Hydrosphere, 1);
                return;
            }
        }
    }
}

[ModuleInfo(BossModuleInfo.Maturity.WIP, Contributors = "The Combat Reborn Team", GroupType = BossModuleInfo.GroupType.CFC, GroupID = 120, NameID = 4613)]
public class A11Cetus(WorldState ws, Actor primary) : BossModule(ws, primary, new(-288, 0), new ArenaBoundsCircle(30))
{
    protected override void DrawEnemies(int pcSlot, Actor pc)
    {
        Arena.Actor(PrimaryActor);
        Arena.Actors(Enemies(OID.HybodusPup));
        Arena.Actors(Enemies(OID.Hybodus));
        Arena.Actors(Enemies(OID.Hydrosphere));
        Arena.Actors(Enemies(OID.Hydrocore));
    }
}
