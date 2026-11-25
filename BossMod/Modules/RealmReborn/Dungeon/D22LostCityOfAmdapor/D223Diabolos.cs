namespace BossMod.RealmReborn.Dungeon.D22LostCityOfAmdapor.D223Diabolos;

public enum OID : uint
{
    Boss = 0xA7C, // R1.5
    GravityBall = 0xA8B // R1.5, untargetable; appears ~1.5s after Gravity Ball resolves and stays until the boss dies
}

public enum AID : uint
{
    AutoAttack = 1882, // Boss->self, no cast, range 9+R 90-degree cone toward the main target
    Nightmare = 1884, // Boss->self, 4.0s cast, raidwide + sleep
    GravityBall = 1890, // Boss->player, 2.0s cast, single-target; spawns a GravityBall near the target (not handled: unavoidable, drop rule unknown)
    CriticalGravity = 1891, // GravityBall->self, no cast, range 8+R circle; first pulse ~4.7s after spawn, then every ~15.1s
    RuinousOmen = 1893, // Boss->self, 15.0s cast, raidwide; avoided by the Voidbound status from going through a matching door pair
    UltimateTerror = 1894 // Boss->self, 4.0s cast, ring around the boss: safe directly under the boss or far away
}

sealed class AutoAttack(BossModule module) : Components.Cleave(module, (uint)AID.AutoAttack, new AOEShapeCone(10.5f, 45f.Degrees()));

sealed class Nightmare(BossModule module) : Components.RaidwideCast(module, (uint)AID.Nightmare);

sealed class RuinousOmen(BossModule module) : Components.CastHint(module, (uint)AID.RuinousOmen, "Go through a matching door pair (Voidbound)!", true);

// Action 表只寫圓形半徑 20、沒有預兆；攻略與錄影都是王腳下與遠處安全 ⇒ 甜甜圈。
// 內徑取 5（錄影：離王 4.9／5.3 沒被打、11.5 被打）；外徑沒有反證，維持 20＋hitbox（危險區只會偏大）。
sealed class UltimateTerror(BossModule module) : Components.SimpleAOEs(module, (uint)AID.UltimateTerror, new AOEShapeDonut(5f, 20f + module.PrimaryActor.HitboxRadius));

// 重力球沒有讀條、到王倒下前週期性爆炸 ⇒ 常駐危險區，不預測爆炸時刻。
sealed class CriticalGravity(BossModule module) : Components.Voidzone(module, 9.5f, GetBalls)
{
    private static List<Actor> GetBalls(BossModule module)
    {
        var balls = module.Enemies((uint)OID.GravityBall);
        var count = balls.Count;
        var result = new List<Actor>(count);
        for (var i = 0; i < count; ++i)
        {
            var b = balls[i];
            if (!b.IsDead)
                result.Add(b);
        }
        return result;
    }
}

sealed class D223DiabolosStates : StateMachineBuilder
{
    public D223DiabolosStates(BossModule module) : base(module)
    {
        TrivialPhase()
            .ActivateOnEnter<AutoAttack>()
            .ActivateOnEnter<Nightmare>()
            .ActivateOnEnter<RuinousOmen>()
            .ActivateOnEnter<UltimateTerror>()
            .ActivateOnEnter<CriticalGravity>()
            .ActivateOnEnter<Components.UnclaimedCastAOEs>();
    }
}

// 場地＝碰撞地板裡放得下的最大圓（比房間小，保守側）；錄影裡戰鬥中的人與王都在圈內。
[ModuleInfo(BossModuleInfo.Maturity.Contributed, Contributors = "ffxiv-tc-port", GroupType = BossModuleInfo.GroupType.CFC, GroupID = 22, NameID = 2564)]
public sealed class D223Diabolos(WorldState ws, Actor primary) : BossModule(ws, primary, default, new ArenaBoundsCircle(23f));
