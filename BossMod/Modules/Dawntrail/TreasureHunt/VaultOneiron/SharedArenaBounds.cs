namespace BossMod.Dawntrail.TreasureHunt.VaultOneiron;

public abstract class SharedBoundsBoss(WorldState ws, Actor primary) : BossModule(ws, primary, new(100f, 100f), new ArenaBoundsSquare(19.5f))
{
    // 上游把下面三支放在共用的 MiniArena／StateMachineBuilder；本 fork 的共用框架沒有，只給這個寶物庫的模組用
    public void DrawActors(uint[] oids, uint color = default)
    {
        for (var i = 0; i < oids.Length; ++i)
        {
            Arena.Actors(Enemies(oids[i]), color);
        }
    }

    public static bool AllDeadOrDestroyed(BossModule module, uint[] oids)
    {
        for (var i = 0; i < oids.Length; ++i)
        {
            var enemies = module.Enemies(oids[i]);
            var count = enemies.Count;
            for (var j = 0; j < count; ++j)
            {
                if (!enemies[j].IsDeadOrDestroyed)
                {
                    return false;
                }
            }
        }
        return true;
    }

    public static bool AllDestroyed(BossModule module, uint[] oids)
    {
        for (var i = 0; i < oids.Length; ++i)
        {
            var enemies = module.Enemies(oids[i]);
            var count = enemies.Count;
            for (var j = 0; j < count; ++j)
            {
                if (!enemies[j].IsDestroyed)
                {
                    return false;
                }
            }
        }
        return true;
    }
}
