namespace BossMod.Heavensward.Alliance.A12IrminsulSawtooth;

class WhiteBreath(BossModule module) : Components.SimpleAOEs(module, (uint)AID.WhiteBreath, new AOEShapeCone(28f, 60f.Degrees()));
class MeanThrash(BossModule module) : Components.SimpleAOEs(module, (uint)AID.MeanThrash, new AOEShapeCone(12f, 60f.Degrees()));
class MeanThrashKnockback(BossModule module) : Components.SimpleKnockbacks(module, (uint)AID.MeanThrash, 10f, stopAtWall: true);
class MucusBomb(BossModule module) : Components.SpreadFromCastTargets(module, (uint)AID.MucusBomb, 10f);
class MucusSpray(BossModule module) : Components.SimpleAOEs(module, (uint)AID.MucusSpray2, new AOEShapeDonut(6f, 20f));
class Rootstorm(BossModule module) : Components.RaidwideCast(module, (uint)AID.Rootstorm);
class Ambush(BossModule module) : Components.SimpleAOEs(module, (uint)AID.Ambush, 9f);
class AmbushKnockback(BossModule module) : Components.SimpleKnockbacks(module, (uint)AID.Ambush, 30f, stopAtWall: true, kind: Kind.TowardsOrigin);

class ShockwaveStomp(BossModule module) : Components.CastLineOfSightAOE(module, (uint)AID.ShockwaveStomp, 70f)
{
    public override ReadOnlySpan<Actor> BlockerActors()
    {
        var boulders = Module.Enemies((uint)OID.Irminsul);
        var count = boulders.Count;
        if (count == 0)
            return [];
        var actors = new List<Actor>();
        for (var i = 0; i < count; ++i)
        {
            var b = boulders[i];
            if (!b.IsDead)
                actors.Add(b);
        }
        return CollectionsMarshal.AsSpan(actors);
    }
}

// Which boss gets Ranged Resistance and which gets Magic Resistance is random, so immunity is read from statuses, never from OID.
// Only lowers priority (never raises an enemy that is already below PriorityInvincible, e.g. one fighting another party).
sealed class Resistances(BossModule module) : BossComponent(module)
{
    private readonly List<Actor> _irminsul = module.Enemies((uint)OID.Irminsul);
    private readonly List<Actor> _sawtooth = module.Enemies((uint)OID.Sawtooth);

    public static bool IsImmuneTo(Actor target, Actor attacker)
    {
        if (target.FindStatus((uint)SID.Invincibility2) != null)
            return true;
        var sid = attacker.Class.GetClassCategory(false) switch
        {
            ClassCategory.PhysRanged => (uint)SID.RangedResistance,
            ClassCategory.Healer or ClassCategory.Caster => (uint)SID.MagicResistance,
            _ => 0u
        };
        return sid != 0 && target.FindStatus(sid) != null;
    }

    public override void AddHints(int slot, Actor actor, TextHints hints)
    {
        if ((CurrentImmune(_irminsul, actor) ?? CurrentImmune(_sawtooth, actor)) is Actor t)
            hints.Add(string.Format(Loc.T("{0} is immune to your attacks, switch targets!"), t.Name));
    }

    public override void AddAIHints(int slot, Actor actor, PartyRolesConfig.Assignment assignment, AIHints hints)
    {
        Lower(_irminsul, actor, hints);
        Lower(_sawtooth, actor, hints);
    }

    private static Actor? CurrentImmune(List<Actor> targets, Actor actor)
    {
        var count = targets.Count;
        for (var i = 0; i < count; ++i)
        {
            var t = targets[i];
            if (t.InstanceID == actor.TargetID && !t.IsDead && IsImmuneTo(t, actor))
                return t;
        }
        return null;
    }

    private static void Lower(List<Actor> targets, Actor actor, AIHints hints)
    {
        var count = targets.Count;
        for (var i = 0; i < count; ++i)
        {
            var t = targets[i];
            if (IsImmuneTo(t, actor) && hints.FindEnemy(t) is AIHints.Enemy e && e.Priority > AIHints.Enemy.PriorityInvincible)
                e.Priority = AIHints.Enemy.PriorityInvincible;
        }
    }
}

[ModuleInfo(BossModuleInfo.Maturity.WIP, Contributors = "The Combat Reborn Team", PrimaryActorOID = (uint)OID.Irminsul, GroupType = BossModuleInfo.GroupType.CFC, GroupID = 120, NameID = 4623)]
public class A12IrminsulSawtooth(WorldState ws, Actor primary) : BossModule(ws, primary, new(0, 130), arena)
{
    private static readonly ArenaBoundsComplex arena = new([new Donut(new(default, 130f), 8, 35)]);
    private Actor? _sawtooth;

    public Actor? Irminsul() => PrimaryActor;
    public Actor? Sawtooth() => _sawtooth;

    protected override void UpdateModule()
    {
        // TODO: this is an ugly hack, think how multi-actor fights can be implemented without it...
        // the problem is that on wipe, any actor can be deleted and recreated in the same frame
        _sawtooth ??= StateMachine.ActivePhaseIndex == 0 ? Enemies((uint)OID.Sawtooth)[0] : null;
    }

    protected override void DrawEnemies(int pcSlot, Actor pc)
    {
        Arena.Actor(PrimaryActor);
        Arena.Actor(_sawtooth);
        Arena.Actors(Enemies((uint)OID.ArkKed));
    }
}
