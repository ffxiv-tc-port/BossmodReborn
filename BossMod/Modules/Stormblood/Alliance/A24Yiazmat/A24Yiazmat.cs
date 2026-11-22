namespace BossMod.Stormblood.Alliance.A24Yiazmat;

class RakeTB(BossModule module) : Components.SingleTargetCast(module, (uint)AID.RakeTB);
class RakeSpread(BossModule module) : Components.SpreadFromCastTargets(module, (uint)AID.RakeSpread, 5);
class RakeAOE(BossModule module) : Components.SimpleAOEs(module, (uint)AID.RakeAOE, 10);
class RakeLoc1(BossModule module) : Components.SimpleAOEs(module, (uint)AID.RakeLoc1, 10);
class RakeLoc2(BossModule module) : Components.SimpleAOEs(module, (uint)AID.RakeLoc2, 10);
class StoneBreath(BossModule module) : Components.SimpleAOEs(module, (uint)AID.StoneBreath, new AOEShapeCone(60, 22.5f.Degrees()));
class DustStorm2(BossModule module) : Components.RaidwideCast(module, (uint)AID.DustStorm2);
class WhiteBreath(BossModule module) : Components.SimpleAOEs(module, (uint)AID.WhiteBreath, new AOEShapeDonut(10, 60));

class AncientAero(BossModule module) : Components.SimpleAOEs(module, (uint)AID.AncientAero, new AOEShapeRect(40, 3));
class Karma(BossModule module) : Components.SimpleAOEs(module, (uint)AID.Karma, new AOEShapeCone(30, 45.Degrees()));
class UnholyDarkness(BossModule module) : Components.SimpleAOEs(module, (uint)AID.UnholyDarkness, 8);

class SolarStorm1(BossModule module) : Components.RaidwideCast(module, (uint)AID.SolarStorm1);

// Archaeodemons standing close together tether and gain Vulnerability Down. Only a tank holding exactly one of them moves,
// and only while that one has the buff: away from the ones tethered to it and from the nearest other one.
sealed class PullApart(BossModule module) : BossComponent(module)
{
    private readonly List<Actor> _adds = module.Enemies((uint)OID.Archaeodemon);

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

[ModuleInfo(BossModuleInfo.Maturity.WIP, Contributors = "The Combat Reborn Team", GroupType = BossModuleInfo.GroupType.CFC, GroupID = 550, NameID = 7070)]
public class A24Yiazmat(WorldState ws, Actor primary) : BossModule(ws, primary, new(800, -400), new ArenaBoundsCircle(30))
{
    protected override void DrawEnemies(int pcSlot, Actor pc)
    {
        Arena.Actor(PrimaryActor);
        Arena.Actors(Enemies(OID.HeartOfTheDragon));
        Arena.Actors(Enemies(OID.Archaeodemon));
        Arena.Actors(Enemies(OID.WindAzer));
    }
}
