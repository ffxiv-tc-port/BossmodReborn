namespace BossMod.Stormblood.Alliance.A22Belias;

class FireIV(BossModule module) : Components.RaidwideCast(module, (uint)AID.FireIV);
class Eruption(BossModule module) : Components.SimpleAOEs(module, (uint)AID.Eruption, 8f);
class TimeBomb2(BossModule module) : Components.SimpleAOEs(module, (uint)AID.TimeBomb2, new AOEShapeCone(60f, 45f.Degrees()));
class TimeEruption(BossModule module) : Components.SimpleAOEGroupsByTimewindow(module, [(uint)AID.TimeEruptionAOEFirst, (uint)AID.TimeEruptionAOESecond], new AOEShapeRect(20f, 10f), expectedNumCasters: 9);

// Gigas standing close together tether and gain Vulnerability Down. Only a tank holding exactly one of them moves,
// and only while that one has the buff: away from the ones tethered to it and from the nearest other one.
sealed class PullApart(BossModule module) : BossComponent(module)
{
    private readonly List<Actor> _adds = module.Enemies((uint)OID.Gigas);

    public override void AddGlobalHints(GlobalHints hints)
    {
        for (var i = 0; i < _adds.Count; ++i)
        {
            var a = _adds[i];
            if (!a.IsDead && a.FindStatus((uint)SID.VulnerabilityDown) != null)
            {
                hints.Add(string.Format(Loc.T("Tanks: pull {0} away from the other adds (damage reduction while close)!"), a.Name));
                return;
            }
        }
    }

    public override void AddAIHints(int slot, Actor actor, PartyRolesConfig.Assignment assignment, AIHints hints)
    {
        if (actor.Role != Role.Tank)
            return;
        Actor? mine = null;
        for (var i = 0; i < _adds.Count; ++i)
        {
            var a = _adds[i];
            if (!a.IsDead && a.TargetID == actor.InstanceID)
            {
                if (mine != null)
                    return; // holding two adds: moving cannot separate them
                mine = a;
            }
        }
        if (mine == null || mine.FindStatus((uint)SID.VulnerabilityDown) == null)
            return;
        Actor? nearest = null;
        var nearestSq = float.MaxValue;
        for (var i = 0; i < _adds.Count; ++i)
        {
            var a = _adds[i];
            if (a.IsDead || a.InstanceID == mine.InstanceID)
                continue;
            if (a.Tether.Target == mine.InstanceID || mine.Tether.Target == a.InstanceID)
                AvoidFrom(a, actor, hints);
            var dSq = (a.Position - mine.Position).LengthSq();
            if (dSq < nearestSq)
            {
                nearestSq = dSq;
                nearest = a;
            }
        }
        if (nearest != null)
            AvoidFrom(nearest, actor, hints);
    }

    private static void AvoidFrom(Actor other, Actor actor, AIHints hints)
        => hints.AddForbiddenZone(ShapeDistance.Circle(other.Position, (other.Position - actor.Position).Length() + 2f), DateTime.MaxValue);
}

[ModuleInfo(BossModuleInfo.Maturity.WIP, Contributors = "The Combat Reborn Team", GroupType = BossModuleInfo.GroupType.CFC, GroupID = 550, NameID = 7223)]
public class A22Belias(WorldState ws, Actor primary) : BossModule(ws, primary, new(-200f, -541f), new ArenaBoundsSquare(30))
{
    protected override void DrawEnemies(int pcSlot, Actor pc)
    {
        Arena.Actor(PrimaryActor);
        Arena.Actors(Enemies((uint)OID.Gigas));
    }
}
