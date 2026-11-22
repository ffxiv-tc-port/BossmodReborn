namespace BossMod.Heavensward.Alliance.A14Echidna;

class SickleStrike(BossModule module) : Components.SingleTargetCast(module, (uint)AID.SickleStrike);

abstract class SickleSlash(BossModule module, uint aid) : Components.SimpleAOEs(module, aid, new AOEShapeRect(18.5f, 30));
class SickleSlash1(BossModule module) : SickleSlash(module, (uint)AID.SickleSlash1);
class SickleSlash2(BossModule module) : SickleSlash(module, (uint)AID.SickleSlash2);

class AbyssalReaper(BossModule module) : Components.SimpleAOEs(module, (uint)AID.AbyssalReaper, 14);
class AbyssalReaperKnockback(BossModule module) : Components.SimpleKnockbacks(module, (uint)AID.AbyssalReaper, 5, stopAtWall: true);
class Petrifaction1(BossModule module) : Components.CastGaze(module, (uint)AID.Petrifaction1);
class Petrifaction2(BossModule module) : Components.CastGaze(module, (uint)AID.Petrifaction2);
class Gehenna(BossModule module) : Components.RaidwideCast(module, (uint)AID.Gehenna);
class BloodyHarvest(BossModule module) : Components.SimpleAOEs(module, (uint)AID.BloodyHarvest, 12);
class Deathstrike(BossModule module) : Components.SimpleAOEs(module, (uint)AID.Deathstrike, new AOEShapeRect(62, 3));
class FlameWreath(BossModule module) : Components.SimpleAOEs(module, (uint)AID.FlameWreath, 18);
class SerpentineStrike(BossModule module) : Components.SimpleAOEs(module, (uint)AID.SerpentineStrike, 20);

// Split adds standing close together tether and stack Vulnerability Down. Only a tank holding exactly one add moves,
// and only while that add has the buff: away from the adds tethered to it and from the nearest other add.
sealed class PullApart(BossModule module) : BossComponent(module)
{
    private readonly List<Actor>[] _adds = [module.Enemies((uint)OID.Dexter), module.Enemies((uint)OID.Sinister), module.Enemies((uint)OID.Echidna)];

    public override void AddGlobalHints(GlobalHints hints)
    {
        for (var l = 0; l < _adds.Length; ++l)
        {
            var list = _adds[l];
            for (var i = 0; i < list.Count; ++i)
            {
                var a = list[i];
                if (!a.IsDead && a.FindStatus((uint)SID.VulnerabilityDown) != null)
                {
                    hints.Add(string.Format(Loc.T("Tanks: pull {0} away from the other adds (damage reduction while close)!"), a.Name));
                    return;
                }
            }
        }
    }

    public override void AddAIHints(int slot, Actor actor, PartyRolesConfig.Assignment assignment, AIHints hints)
    {
        if (actor.Role != Role.Tank)
            return;
        Actor? mine = null;
        for (var l = 0; l < _adds.Length; ++l)
        {
            var list = _adds[l];
            for (var i = 0; i < list.Count; ++i)
            {
                var a = list[i];
                if (!a.IsDead && a.TargetID == actor.InstanceID)
                {
                    if (mine != null)
                        return; // holding two adds: moving cannot separate them
                    mine = a;
                }
            }
        }
        if (mine == null || mine.FindStatus((uint)SID.VulnerabilityDown) == null)
            return;
        Actor? nearest = null;
        var nearestSq = float.MaxValue;
        for (var l = 0; l < _adds.Length; ++l)
        {
            var list = _adds[l];
            for (var i = 0; i < list.Count; ++i)
            {
                var a = list[i];
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
        }
        if (nearest != null)
            AvoidFrom(nearest, actor, hints);
    }

    private static void AvoidFrom(Actor other, Actor actor, AIHints hints)
        => hints.AddForbiddenZone(ShapeDistance.Circle(other.Position, (other.Position - actor.Position).Length() + 2f), DateTime.MaxValue);
}

[ModuleInfo(BossModuleInfo.Maturity.WIP, Contributors = "The Combat Reborn Team", GroupType = BossModuleInfo.GroupType.CFC, GroupID = 120, NameID = 4631)]
public class A14Echidna(WorldState ws, Actor primary) : BossModule(ws, primary, new(288, -126), new ArenaBoundsCircle(29.5f))
{
    protected override void DrawEnemies(int pcSlot, Actor pc)
    {
        Arena.Actor(PrimaryActor);
        Arena.Actors(Enemies(OID.Dexter));
        Arena.Actors(Enemies(OID.Sinister));
        Arena.Actors(Enemies(OID.Bloodguard));
    }
}