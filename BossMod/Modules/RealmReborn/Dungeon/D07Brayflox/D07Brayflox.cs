namespace BossMod.RealmReborn.Dungeon.D07Brayflox;

// 衝浪水蜥的「水浪吐息」：無讀條、沒有前兆、間隔不固定，朝當前目標噴扇形（半徑 8 從中心算；半角取 60，實測打中最寬約 45 度）。
// 比照 Components.Cleave：常駐畫給「不是它目標」的人；被它盯上的人本來就躲不掉，不畫。只在沒有首領模組啟用時生效（ZoneModule 契約）。
// 刻意不做 Cleave 的「被盯上時把它轉離隊友」：這裡只處理自己被波及，轉向會牽動坦克走位。
[ZoneModuleInfo(BossModuleInfo.Maturity.Contributed, 8)]
public sealed class D07Brayflox(WorldState ws) : ZoneModule(ws)
{
    private const uint SurfOID = 0x418; // R3.24
    private static readonly AOEShapeCone _breath = new(8f, 60f.Degrees());

    public override void CalculateAIHints(int playerSlot, Actor player, AIHints hints)
    {
        foreach (var a in World.Actors)
        {
            if (a.OID != SurfOID || a.IsDead || !a.IsTargetable || a.TargetID == player.InstanceID)
                continue;
            if (World.Actors.Find(a.TargetID) is not { } target)
                continue;
            hints.AddForbiddenZone(_breath, a.Position.Quantized(), Angle.FromDirection(target.Position - a.Position));
        }
    }
}
