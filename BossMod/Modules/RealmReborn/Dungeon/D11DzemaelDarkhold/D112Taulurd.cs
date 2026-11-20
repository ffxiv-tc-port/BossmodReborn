namespace BossMod.RealmReborn.Dungeon.D11DzemaelDarkhold.D112Taulurd;

public enum OID : uint
{
    Boss = 0x608, // R1.56
    DeepVoidMonster = 0x609 // R1.04
}

public enum AID : uint
{
    AutoAttack = 870, // Boss->player, no cast, single-target
    BoneCrusher1 = 1122, // Boss->player, no cast, single-target
    BoneCrusher2 = 1123, // Boss->player, no cast, single-target, knockback
    ElbowDrop = 1124, // Boss->self, 2.5s cast, range 6+R 120-degree cone
    DoubleSmash = 1125, // Boss->self, 2.5s cast, range 6+R 120-degree cone
    Heat = 1131, // Boss->self, no cast, damage up buff
    BoulderDash = 1121, // DeepVoidMonster->player, no cast, single-target
    FireHose = 1115 // DeepVoidMonster->location, 2.5s cast, range 3 circle
}

sealed class ElbowDrop(BossModule module) : Components.SimpleAOEs(module, (uint)AID.ElbowDrop, new AOEShapeCone(7.56f, 60f.Degrees()));
sealed class DoubleSmash(BossModule module) : Components.SimpleAOEs(module, (uint)AID.DoubleSmash, new AOEShapeCone(7.56f, 60f.Degrees()));
sealed class FireHose(BossModule module) : Components.SimpleAOEs(module, (uint)AID.FireHose, 3f);

sealed class D112TaulurdStates : StateMachineBuilder
{
    public D112TaulurdStates(BossModule module) : base(module)
    {
        TrivialPhase()
            .ActivateOnEnter<ElbowDrop>()
            .ActivateOnEnter<DoubleSmash>()
            .ActivateOnEnter<FireHose>()
            .ActivateOnEnter<Components.UnclaimedCastAOEs>();
    }
}

// 場地＝碰撞地板描邊、往內縮 0.25 碼的多邊形；王開打時從東側高台跳下，落地前不在場地內。
[ModuleInfo(BossModuleInfo.Maturity.WIP, Contributors = "ffxiv-tc-port", GroupType = BossModuleInfo.GroupType.CFC, GroupID = 13, NameID = 1415)]
public sealed class D112Taulurd(WorldState ws, Actor primary) : BossModule(ws, primary, arena.Center, arena)
{
    private static readonly WPos[] vertices = [new(-112.12f, -0.50f), new(-113.12f, -0.50f), new(-113.50f, -0.88f), new(-113.25f, -2.62f), new(-113.75f, -3.12f),
    new(-113.75f, -4.62f), new(-114.88f, -5.25f), new(-115.38f, -5.00f), new(-116.00f, -5.62f), new(-115.25f, -7.12f),
    new(-115.50f, -7.88f), new(-116.50f, -8.62f), new(-116.25f, -10.12f), new(-117.00f, -11.62f), new(-114.25f, -13.62f),
    new(-113.50f, -15.62f), new(-111.25f, -17.88f), new(-111.00f, -19.88f), new(-110.00f, -20.62f), new(-109.50f, -21.88f),
    new(-110.25f, -23.12f), new(-109.75f, -24.62f), new(-108.88f, -25.75f), new(-107.88f, -25.50f), new(-107.50f, -25.88f),
    new(-108.25f, -28.12f), new(-108.25f, -30.62f), new(-107.50f, -30.88f), new(-108.25f, -31.62f), new(-108.25f, -32.38f),
    new(-107.50f, -33.62f), new(-108.00f, -35.38f), new(-107.38f, -35.75f), new(-106.88f, -35.50f), new(-105.62f, -37.25f),
    new(-104.25f, -37.88f), new(-104.50f, -38.12f), new(-103.00f, -39.38f), new(-99.88f, -44.00f), new(-98.62f, -44.50f),
    new(-97.38f, -43.75f), new(-95.62f, -44.25f), new(-95.12f, -43.75f), new(-93.88f, -43.50f), new(-93.12f, -44.25f),
    new(-92.25f, -43.62f), new(-92.50f, -42.88f), new(-91.00f, -41.88f), new(-88.88f, -39.00f), new(-84.00f, -35.88f),
    new(-83.50f, -34.62f), new(-84.25f, -33.38f), new(-83.75f, -32.38f), new(-84.00f, -31.62f), new(-83.50f, -31.12f),
    new(-83.50f, -29.88f), new(-82.75f, -29.12f), new(-82.75f, -27.38f), new(-83.25f, -26.62f), new(-82.62f, -26.25f),
    new(-81.88f, -27.00f), new(-80.38f, -25.75f), new(-78.62f, -25.50f), new(-76.25f, -23.12f), new(-76.50f, -21.88f),
    new(-75.75f, -20.88f), new(-75.75f, -19.88f), new(-74.00f, -17.88f), new(-74.00f, -17.38f), new(-75.75f, -15.38f),
    new(-75.50f, -13.62f), new(-76.62f, -12.00f), new(-77.38f, -11.75f), new(-79.38f, -12.25f), new(-81.12f, -10.75f),
    new(-82.12f, -11.00f), new(-82.88f, -12.00f), new(-84.88f, -12.00f), new(-87.88f, -13.75f), new(-90.12f, -13.00f),
    new(-91.12f, -13.25f), new(-92.88f, -11.50f), new(-93.62f, -11.50f), new(-95.38f, -13.50f), new(-97.88f, -13.25f),
    new(-98.88f, -14.50f), new(-99.62f, -14.75f), new(-101.38f, -14.75f), new(-102.88f, -13.50f), new(-104.12f, -14.25f),
    new(-104.88f, -13.50f), new(-106.12f, -13.50f), new(-109.50f, -11.62f), new(-110.00f, -9.88f), new(-108.75f, -9.12f),
    new(-109.00f, -7.62f), new(-107.75f, -5.88f), new(-110.00f, -5.12f), new(-109.50f, -1.12f), new(-109.88f, -0.75f)];
    private static readonly ArenaBoundsComplex arena = new([new PolygonCustom(vertices)]);
}
