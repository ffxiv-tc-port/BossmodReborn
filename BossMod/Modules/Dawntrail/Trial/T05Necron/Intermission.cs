namespace BossMod.Dawntrail.Trial.T05Necron;

sealed class Prisons(BossModule module) : BossComponent(module)
{
    private BitMask activeTeleporters;

    private static readonly WPos[] prisonPositions = [new(100f, -100f), new(300f, -100f), new(300f, 100f), new(300f, 300f),
    new(100f, 300f), new(-100f, 300f), new(-100f, 100f), new(-100f, -100f)];
    // 每座監牢的場地只建一次：場地換成另一個實例會讓三角化與尋路底圖快取（存在場地實例上）失效，每幀 new＝每幀重建。
    private readonly ArenaBoundsComplex?[] prisonArenas = new ArenaBoundsComplex?[8];

    public override void OnEventEnvControl(byte index, uint state)
    {
        if (index <= 0x07)
        {
            switch (state)
            {
                case 0x00020001u:
                    activeTeleporters.Set(index);
                    break;
                case 0x00080004u:
                    activeTeleporters.Clear(index);
                    break;
            }
        }
    }

    public override void DrawArenaForeground(int pcSlot, Actor pc)
    {
        if (pc.PosRot.Y < -100f)
        {
            var playerPos = pc.Position;
            for (var i = 0; i < 8; ++i)
            {
                var pos = prisonPositions[i];
                if (playerPos.InSquare(pos, 50f))
                {
                    if (activeTeleporters[i])
                    {
                        var color = Colors.SafeFromAOE;
                        Arena.ZoneCircle(pos + new WDir(default, -7.4f), 2f, color);
                        Arena.ZoneCircle(pos + new WDir(-2.5f, -20f), 2f, color);
                        Arena.ZoneCircle(pos + new WDir(15f, -11.5f), 2f, color);
                        Arena.ZoneCircle(pos + new WDir(20f, default), 1.5f, color);
                    }
                    return;
                }
            }
        }
    }

    // 場地在 Update 切而不是在畫雷達時切：AI 的尋路視窗取自模組場地，雷達沒畫（視窗關著、被隱藏）時也要跟著進監牢。
    public override void Update()
    {
        if (Raid.Player() is not { } pc)
            return;
        if (pc.PosRot.Y < -100f)
        {
            var playerPos = pc.Position;
            if (Arena.Bounds.Radius == 17.5f && playerPos.InSquare(Arena.Center, 50f)) // grand cross could happen while player is in prison
            {
                return;
            }
            for (var i = 0; i < 8; ++i)
            {
                var pos = prisonPositions[i];
                if (playerPos.InSquare(pos, 50f))
                {
                    var arena = prisonArenas[i] ??= new ArenaBoundsComplex([new Polygon(pos, 9.5f, 32), new Polygon(pos + new WDir(-5f, -21f), 4.5f, 32),
                        new Polygon(pos + new WDir(14f, -14f), 4.5f, 32), new Polygon(pos + new WDir(20f, default), 3.25f, 32)]);
                    Arena.Bounds = arena;
                    Arena.Center = arena.Center;
                    return;
                }
            }
        }
        else if (Arena.Bounds.Radius == 17.5f)
        {
            Arena.Bounds = new ArenaBoundsRect(18f, 15f);
            Arena.Center = Necron.ArenaCenter;
        }
    }

    public override void AddAIHints(int slot, Actor actor, PartyRolesConfig.Assignment assignment, AIHints hints)
    {
        if (actor.PosRot.Y < -100f)
        {
            var playerPos = actor.Position;
            for (var i = 0; i < 8; ++i)
            {
                var pos = prisonPositions[i];
                if (playerPos.InSquare(pos, 50f))
                {
                    if (activeTeleporters[i])
                    {
                        // 本 fork 的尋路沒有傳送點：沿用上游加入傳送點支援之前的寫法，把四個傳送點都設成目標
                        hints.GoalZones.Add(hints.GoalSingleTarget(pos + new WDir(default, -7.4f), 1f, 9f));
                        hints.GoalZones.Add(hints.GoalSingleTarget(pos + new WDir(-2.5f, -20f), 1f, 9f));
                        hints.GoalZones.Add(hints.GoalSingleTarget(pos + new WDir(15f, -11.5f), 1f, 9f));
                        hints.GoalZones.Add(hints.GoalSingleTarget(pos + new WDir(20f, default), 1f, 9f));
                    }
                    return;
                }
            }
        }
    }
}
