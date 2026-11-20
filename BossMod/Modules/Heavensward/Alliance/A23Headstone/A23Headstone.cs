namespace BossMod.Heavensward.Alliance.A23Headstone;

class TremblingEpigraph(BossModule module) : Components.RaidwideCast(module, (uint)AID.TremblingEpigraph);
class FlaringEpigraph(BossModule module) : Components.RaidwideCast(module, (uint)AID.FlaringEpigraph);
class BigBurst(BossModule module) : Components.RaidwideCast(module, (uint)AID.BigBurst);

[ModuleInfo(BossModuleInfo.Maturity.WIP, Contributors = "The Combat Reborn Team", GroupType = BossModuleInfo.GroupType.CFC, GroupID = 168, NameID = 4868)]
class A23Headstone(WorldState ws, Actor primary) : BossModule(ws, primary, arena.Center, arena)
{
    private static readonly Angle am60 = -60f.Degrees();
    // 外框取自碰撞：三塊相連的平台（兩端較小）；兩個矩形是兩側入口的條件式牆。
    // 上游的「依角色碰撞半徑內縮」我方沒有，改用尋路內縮 0.5。
    private static readonly ArenaBoundsComplex arena = new([new Polygon(new(-184.52202f, 197.28719f), 16f, 32, am60),
        new Polygon(new(-168.522f, 225f), 20f, 32, am60), new Polygon(new(-152.522f, 252.7128f), 16f, 32, am60)],
        [new Rectangle(new(-185.9566f, 235.09061f), 9f, 0.75f, am60), new Rectangle(new(-150.99809f, 214.88989f), 9f, 0.75f, am60)], Offset: -0.5f);

    protected override void DrawEnemies(int pcSlot, Actor pc)
    {
        Arena.Actor(PrimaryActor);
        Arena.Actors(Enemies(OID.Parthenope));
        Arena.Actors(Enemies(OID.VoidFire));
    }
}
