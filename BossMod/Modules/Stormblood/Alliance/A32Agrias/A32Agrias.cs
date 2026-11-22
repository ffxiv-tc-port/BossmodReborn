namespace BossMod.Stormblood.Alliance.A32Agrias;

class DivineLight(BossModule module) : Components.RaidwideCast(module, (uint)AID.DivineLight);
class NorthswainsStrikeEphemeralKnight(BossModule module) : Components.SimpleAOEs(module, (uint)AID.NorthswainsStrikeEphemeralKnight, new AOEShapeRect(60, 3));
class CleansingFlameSpread(BossModule module) : Components.SpreadFromCastTargets(module, (uint)AID.CleansingFlameSpread, 6);
class HallowedBoltAOE(BossModule module) : Components.SimpleAOEs(module, (uint)AID.HallowedBoltAOE, 15);

// Emblazoned Shields tethered to the Shield Knight keep it invulnerable; they are broken with the Swordbearer duty action.
// Deliberately hint-only: the duty action is never pressed for the player (positioning and facing in a 24-player fight).
sealed class ShieldKnightInvulnerable(BossModule module) : BossComponent(module)
{
    private readonly List<Actor> _knights = module.Enemies((uint)OID.ShieldKnight);

    private Actor? Invulnerable()
    {
        for (var i = 0; i < _knights.Count; ++i)
        {
            var k = _knights[i];
            if (!k.IsDead && k.FindStatus((uint)SID.Invincibility2) != null)
                return k;
        }
        return null;
    }

    public override void AddGlobalHints(GlobalHints hints)
    {
        if (Invulnerable() is Actor k)
            hints.Add(string.Format(Loc.T("{0} is invulnerable: take the sword from the silver circle and break the tethered shields with the duty action!"), k.Name));
    }

    public override void AddHints(int slot, Actor actor, TextHints hints)
    {
        if (actor.FindStatus((uint)SID.Swordbearer) != null && Invulnerable() != null)
            hints.Add(Loc.T("You hold the sword: face the tethered shields and use the duty action!"), false);
    }

    public override void AddAIHints(int slot, Actor actor, PartyRolesConfig.Assignment assignment, AIHints hints)
    {
        for (var i = 0; i < _knights.Count; ++i)
        {
            var k = _knights[i];
            if (k.FindStatus((uint)SID.Invincibility2) != null && hints.FindEnemy(k) is AIHints.Enemy e && e.Priority > AIHints.Enemy.PriorityInvincible)
                e.Priority = AIHints.Enemy.PriorityInvincible;
        }
    }
}

[ModuleInfo(BossModuleInfo.Maturity.WIP, Contributors = "The Combat Reborn Team", GroupType = BossModuleInfo.GroupType.CFC, GroupID = 636, NameID = 7916)]
public class A32Agrias(WorldState ws, Actor primary) : BossModule(ws, primary, new(600, -54), new ArenaBoundsCircle(30));
