namespace BossMod.Heavensward.Alliance.A21ArachneEve;

class DarkSpike(BossModule module) : Components.SingleTargetCast(module, (uint)AID.DarkSpike);
class SilkenSpray(BossModule module) : Components.SimpleAOEs(module, (uint)AID.SilkenSpray, new AOEShapeCone(24.5f, 30.Degrees()));
class ShadowBurst(BossModule module) : Components.StackWithCastTargets(module, (uint)AID.ShadowBurst, 6, 8);
class SpiderThread(BossModule module) : Components.SpreadFromCastTargets(module, (uint)AID.SpiderThread, 6);
// 破土突襲是距離衰減（全場 60 米），以首領 20 米內當危險區（同隕石衝擊）。
sealed class Pitfall(BossModule module) : Components.SimpleAOEs(module, (uint)AID.Pitfall1, 20f);
class FrondAffeared(BossModule module) : Components.CastGaze(module, (uint)AID.FrondAffeared);
class TheWidowsEmbrace(BossModule module) : Components.SimpleKnockbacks(module, (uint)AID.TheWidowsEmbrace, 18, kind: Kind.TowardsOrigin, stopAtWall: true);
class TheWidowsKiss(BossModule module) : Components.SimpleKnockbacks(module, (uint)AID.TheWidowsKiss, 4, kind: Kind.TowardsOrigin, stopAtWall: true);

[ModuleInfo(BossModuleInfo.Maturity.WIP, Contributors = "The Combat Reborn Team", GroupType = BossModuleInfo.GroupType.CFC, GroupID = 168, NameID = 4871)]
public class A21ArachneEve(WorldState ws, Actor primary) : BossModule(ws, primary, DefaultArena.Center, DefaultArena)
{
    // 下層地板（y≈-0.5）：圓形、南北兩端照碰撞截平。
    public static readonly ArenaBoundsComplex DefaultArena = new([new Polygon(new(16f, -55f), 29.1f, 96)],
        [new Rectangle(new(16f, -84.5f), 22.9f, 1.25f), new Rectangle(new(16f, -25.6f), 22.9f, 1.25f)]);

    protected override void DrawEnemies(int pcSlot, Actor pc)
    {
        Arena.Actor(PrimaryActor);
        Arena.Actors(Enemies(OID.Keyknot));
        Arena.Actors(Enemies(OID.Webmaiden));
        Arena.Actors(Enemies(OID.SpittingSpider));
        Arena.Actors(Enemies(OID.SkitteringSpider));
        Arena.Actors(Enemies(OID.EarthAether));
        Arena.Actors(Enemies(OID.DeepEarthAether));
    }
}
