namespace BossMod.RealmReborn.Dungeon.D19HaukkeHard.D192Ash;

public enum OID : uint
{
    Boss = 0x96F, // R1.5
    Helper = 0x1B2, // R0.5
    AbyssalLance = 0x971, // R1.0
    AbyssalScythe = 0x972, // R1.0
    VoidRift = 0x977, // R0.5
    ServantMinotaur = 0x970 // R1.68
}

public enum AID : uint
{
    AutoAttack = 1700, // Boss->player, no cast, single-target
    AbyssalSwing = 1701, // Boss->self, no cast, range 8+R cone, tank cleave
    VoidCall = 1704, // Boss->location, 1.0s cast, range 4 circle
    Execution = 1705, // Boss->self, 1.0s cast, summons the scythe or the guillotines
    AbyssalImpale = 1706, // AbyssalLance->self, 3.0s cast, range 40 width 4 rect
    DeathScythe = 1707, // AbyssalScythe->self, no cast, range 4+R circle, every ~2.5s, knockback
    Dismemberment = 1708 // Helper->self, 4.0s cast, range 35 width 8 rect
}

sealed class AbyssalImpale(BossModule module) : Components.SimpleAOEs(module, (uint)AID.AbyssalImpale, new AOEShapeRect(40f, 2f));
sealed class Dismemberment(BossModule module) : Components.SimpleAOEs(module, (uint)AID.Dismemberment, new AOEShapeRect(35f, 4f));
sealed class DeathScythe(BossModule module) : Components.Voidzone(module, 5f, GetScythes)
{
    private static List<Actor> GetScythes(BossModule module)
    {
        var scythes = module.Enemies((uint)OID.AbyssalScythe);
        var count = scythes.Count;
        if (count == 0)
        {
            return scythes;
        }
        var alive = new List<Actor>(count);
        for (var i = 0; i < count; ++i)
        {
            var s = scythes[i];
            if (!s.IsDead)
            {
                alive.Add(s);
            }
        }
        return alive;
    }
}

sealed class D192AshStates : StateMachineBuilder
{
    public D192AshStates(BossModule module) : base(module)
    {
        TrivialPhase()
            .ActivateOnEnter<AbyssalImpale>()
            .ActivateOnEnter<Dismemberment>()
            .ActivateOnEnter<DeathScythe>()
            .ActivateOnEnter<Components.UnclaimedCastAOEs>();
    }
}

// 深淵迴轉（無讀條順劈）錄影量不出角度，刻意不畫。
// 場地＝錄影裡戰鬥範圍內、經碰撞驗過全是地板的矩形（一樓大廳，比房間小）。
[ModuleInfo(BossModuleInfo.Maturity.WIP, Contributors = "ffxiv-tc-port", GroupType = BossModuleInfo.GroupType.CFC, GroupID = 19, NameID = 2341)]
public sealed class D192Ash(WorldState ws, Actor primary) : BossModule(ws, primary, new(-2.85f, -0.5f), new ArenaBoundsRect(16.5f, 9.5f));
