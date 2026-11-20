namespace BossMod.RealmReborn.Dungeon.D05Totorak.D051CoeurlONineTails;

public enum OID : uint
{
    Boss = 0x10E, // R1.6
    GaolWhip = 0x10F // R0.8, 第二場同名首領戰才出現
}

public enum AID : uint
{
    AutoAttack = 872, // Boss/GaolWhip->player, no cast, single-target
    AcidMist = 1014 // Boss/GaolWhip->self, no cast, range 6+R circle, poison
}

// 酸霧沒有讀條、約每 6.4 秒對自身周圍放一次；首領不會移動，坦克與近戰本來就得貼身，只讓治療與遠程站到圈外。
sealed class AcidMist(BossModule module) : BossComponent(module)
{
    private static readonly uint[] sources = [(uint)OID.Boss, (uint)OID.GaolWhip];

    public override void AddAIHints(int slot, Actor actor, PartyRolesConfig.Assignment assignment, AIHints hints)
    {
        if (actor.Role is not Role.Healer and not Role.Ranged)
        {
            return;
        }
        for (var o = 0; o < sources.Length; ++o)
        {
            var enemies = Module.Enemies(sources[o]);
            var count = enemies.Count;
            for (var i = 0; i < count; ++i)
            {
                var e = enemies[i];
                if (!e.IsDead)
                {
                    hints.AddForbiddenZone(ShapeDistance.Circle(e.Position.Quantized(), 6f + e.HitboxRadius));
                }
            }
        }
    }

    public override void DrawArenaForeground(int pcSlot, Actor pc)
    {
        for (var o = 0; o < sources.Length; ++o)
        {
            var enemies = Module.Enemies(sources[o]);
            var count = enemies.Count;
            for (var i = 0; i < count; ++i)
            {
                var e = enemies[i];
                if (!e.IsDead)
                {
                    Arena.AddCircle(e.Position, 6f + e.HitboxRadius, Colors.Danger);
                }
            }
        }
    }
}

sealed class D051CoeurlONineTailsStates : StateMachineBuilder
{
    public D051CoeurlONineTailsStates(BossModule module) : base(module)
    {
        TrivialPhase()
            .ActivateOnEnter<AcidMist>()
            .ActivateOnEnter<Components.UnclaimedCastAOEs>();
    }
}

// 同一個 OID 打兩場（地圖上不同走廊）；場地依首領出生位置選，兩塊都是碰撞地板描邊後往內縮的多邊形。
[ModuleInfo(BossModuleInfo.Maturity.WIP, Contributors = "ffxiv-tc-port", GroupType = BossModuleInfo.GroupType.CFC, GroupID = 1, NameID = 442)]
public sealed class D051CoeurlONineTails(WorldState ws, Actor primary) : BossModule(ws, primary, IsFirst(primary) ? first.Center : second.Center, IsFirst(primary) ? first : second)
{
    private static bool IsFirst(Actor primary) => primary.Position.Z > 0f;

    private static readonly WPos[] verticesFirst = [new(-104.12f, 123.00f), new(-110.88f, 123.00f), new(-111.62f, 122.25f), new(-112.38f, 122.25f), new(-113.12f, 123.00f),
    new(-119.38f, 123.00f), new(-122.12f, 120.75f), new(-124.12f, 120.25f), new(-125.25f, 117.88f), new(-126.88f, 116.25f),
    new(-128.75f, 115.88f), new(-128.75f, 114.38f), new(-130.62f, 112.50f), new(-132.12f, 112.50f), new(-133.88f, 114.50f),
    new(-135.75f, 115.88f), new(-135.25f, 116.88f), new(-133.00f, 118.88f), new(-133.75f, 122.88f), new(-140.88f, 123.00f),
    new(-140.75f, 117.38f), new(-139.88f, 116.75f), new(-138.12f, 116.75f), new(-136.25f, 115.12f), new(-137.88f, 113.50f),
    new(-138.88f, 113.25f), new(-141.00f, 110.38f), new(-141.00f, 101.88f), new(-137.62f, 102.00f), new(-134.88f, 102.75f),
    new(-135.00f, 106.12f), new(-133.12f, 108.25f), new(-130.88f, 108.25f), new(-130.12f, 107.50f), new(-126.88f, 107.75f),
    new(-125.00f, 105.88f), new(-124.00f, 103.62f), new(-121.62f, 101.00f), new(-119.88f, 100.50f), new(-117.62f, 98.75f),
    new(-116.88f, 98.75f), new(-116.38f, 99.25f), new(-114.38f, 99.25f), new(-113.62f, 98.50f), new(-110.88f, 98.25f),
    new(-109.88f, 99.25f), new(-108.38f, 99.25f), new(-108.25f, 99.88f), new(-106.75f, 101.38f), new(-106.50f, 103.62f),
    new(-105.38f, 104.75f), new(-104.12f, 104.75f), new(-102.62f, 105.50f), new(-101.12f, 107.00f), new(-99.00f, 107.88f),
    new(-99.25f, 119.38f), new(-99.88f, 120.25f), new(-101.88f, 121.00f)];
    private static readonly WPos[] verticesSecond = [new(-75.62f, -33.00f), new(-84.12f, -33.00f), new(-86.12f, -35.00f), new(-87.25f, -35.38f), new(-86.00f, -36.88f),
    new(-85.75f, -38.12f), new(-86.88f, -39.50f), new(-87.50f, -39.62f), new(-87.25f, -40.12f), new(-88.75f, -41.38f),
    new(-88.25f, -42.12f), new(-89.12f, -43.00f), new(-89.62f, -42.50f), new(-91.38f, -43.25f), new(-92.62f, -42.00f),
    new(-94.38f, -43.50f), new(-96.00f, -43.88f), new(-96.00f, -51.88f), new(-93.88f, -52.75f), new(-92.12f, -54.75f),
    new(-91.38f, -55.00f), new(-90.00f, -56.38f), new(-88.88f, -58.00f), new(-71.62f, -58.00f), new(-72.25f, -56.62f),
    new(-70.88f, -55.50f), new(-70.38f, -55.50f), new(-69.25f, -56.62f), new(-69.25f, -57.12f), new(-70.00f, -57.88f),
    new(-69.38f, -58.00f), new(-68.00f, -55.38f), new(-69.25f, -53.12f), new(-69.00f, -51.88f), new(-65.88f, -49.00f),
    new(-64.62f, -49.00f), new(-63.62f, -50.50f), new(-63.00f, -50.38f), new(-63.00f, -45.62f), new(-63.62f, -45.50f),
    new(-63.75f, -46.12f), new(-64.88f, -47.25f), new(-65.62f, -47.25f), new(-69.75f, -42.12f), new(-69.75f, -41.12f),
    new(-70.25f, -40.62f), new(-69.50f, -39.12f), new(-69.75f, -37.38f), new(-71.88f, -35.50f), new(-73.88f, -35.00f)];
    private static readonly ArenaBoundsComplex first = new([new PolygonCustom(verticesFirst)]);
    private static readonly ArenaBoundsComplex second = new([new PolygonCustom(verticesSecond)],
    // 挖掉的四個小圓＝障礙物圖上的柱子（碰撞描邊補洞時被填平）。
    [new Circle(new(-74.25f, -55.39f), 1f), new Circle(new(-73.08f, -53.42f), 1f), new Circle(new(-88.89f, -53.25f), 1f), new Circle(new(-73.11f, -37.25f), 1f)]);
}
