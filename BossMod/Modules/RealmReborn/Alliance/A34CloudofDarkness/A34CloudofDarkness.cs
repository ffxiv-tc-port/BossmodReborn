namespace BossMod.RealmReborn.Alliance.A34CloudofDarkness;

class ZeroFormParticleBeam(BossModule module) : Components.SimpleAOEs(module, (uint)AID.ZeroFormParticleBeam, new AOEShapeRect(74, 12));
class ParticleBeam2(BossModule module) : Components.RaidwideCast(module, (uint)AID.ParticleBeam2);

// 波動爆炸沒有讀條；落點的 EventObj 約在判定前 9.8 秒出現，判定後約 4 秒才消失，所以以判定為準移除、消失只當備援。
class ParticleBeam1(BossModule module) : Components.GenericAOEs(module, (uint)AID.ParticleBeam1)
{
    private static readonly AOEShapeCircle circle = new(5f);
    private readonly List<AOEInstance> _aoes = new(5);

    public override ReadOnlySpan<AOEInstance> ActiveAOEs(int slot, Actor actor) => CollectionsMarshal.AsSpan(_aoes);

    public override void OnActorCreated(Actor actor)
    {
        if (actor.OID == (uint)OID.Actor1e974e)
            _aoes.Add(new(circle, actor.Position.Quantized(), default, WorldState.FutureTime(9.8d)));
    }

    public override void OnActorDestroyed(Actor actor)
    {
        if (actor.OID == (uint)OID.Actor1e974e)
            RemoveEarliest(actor.Position, WorldState.CurrentTime);
    }

    public override void OnEventCast(Actor caster, ActorCastEvent spell)
    {
        if (spell.Action.ID == WatchedAction)
        {
            ++NumCasts;
            RemoveEarliest(spell.TargetXZ, DateTime.MaxValue);
        }
    }

    // 落點 1 碼內、生效不晚於 notAfter 的最早一個；EventObj 消失時只清已過生效時間的，免得誤刪同點的下一波。
    // 不存 actorID：判定的施放者是 helper，不是 EventObj。
    private void RemoveEarliest(WPos pos, DateTime notAfter)
    {
        var best = -1;
        var aoes = CollectionsMarshal.AsSpan(_aoes);
        for (var i = 0; i < aoes.Length; ++i)
        {
            ref readonly var a = ref aoes[i];
            if (a.Activation <= notAfter && a.Origin.InCircle(pos, 1f) && (best < 0 || a.Activation < aoes[best].Activation))
                best = i;
        }
        if (best >= 0)
            _aoes.RemoveAt(best);
    }
}

// 第一下讀條（半徑 8）後還會追 16 下（半徑 3）：每下約隔 0.55 秒、往目標移動約 3 碼；目標中途倒下會提早結束。
class FeintParticleBeam(BossModule module) : Components.StandardChasingAOEs(module, new AOEShapeCircle(3f), (uint)AID.FeintParticleBeam1, (uint)AID.FeintParticleBeam2, 3, 0.55f, 17, true)
{
}
class FeintParticleBeamFirst(BossModule module) : Components.SimpleAOEs(module, (uint)AID.FeintParticleBeam1, 8f);

[ModuleInfo(BossModuleInfo.Maturity.WIP, Contributors = "The Combat Reborn Team", GroupType = BossModuleInfo.GroupType.CFC, GroupID = 111, NameID = 3240)]
public class A34CloudofDarkness(WorldState ws, Actor primary) : BossModule(ws, primary, new(-300, -400), new ArenaBoundsCircle(30))
{
    protected override void DrawEnemies(int pcSlot, Actor pc)
    {
        Arena.Actor(PrimaryActor);
        Arena.Actors(Enemies(OID.DarkCloud));
        Arena.Actors(Enemies(OID.DarkStorm));
        Arena.Actors(Enemies(OID.HyperchargedCloud));
    }
}
