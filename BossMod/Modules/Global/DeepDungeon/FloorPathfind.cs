using static FFXIVClientStructs.FFXIV.Client.Game.InstanceContent.InstanceContentDeepDungeon;

namespace BossMod.Global.DeepDungeon;

public enum Direction
{
    North,
    South,
    East,
    West
}

// neat feature of deep dungeons - there is only one path from any room to any other room (no loops) and the grid is so small that brute forcing is basically free!
internal sealed class FloorPathfind(ReadOnlySpan<RoomFlags> Map)
{
    public readonly RoomFlags[] Map = Map.ToArray();

    private readonly bool[] Explored = new bool[25];

    private readonly Queue<List<int>> Queue = [];

    public List<int> Pathfind(int startRoom, int destRoom)
    {
        if (startRoom == destRoom)
            return [];
        // defend against garbage/out-of-range room indices (e.g. stale or misread game data) instead of crashing
        if ((uint)startRoom >= 25 || (uint)destRoom >= 25)
            return [];

        Explored[startRoom] = true;
        Queue.Enqueue([startRoom]);
        while (Queue.TryDequeue(out var v))
        {
            var v1 = v[^1];
            if (v1 == destRoom)
            {
                v.RemoveAt(0);
                return v;
            }
            var edges = CollectionsMarshal.AsSpan(Edges(v1));
            var len = edges.Length;
            for (var i = 0; i < len; ++i)
            {
                var w = edges[i];
                if ((uint)w < 25 && !Explored[w])
                {
                    Explored[w] = true;
                    Queue.Enqueue([.. v, w]);
                }
            }
        }

        return [];
    }

    /// <summary>
    /// 從 <paramref name="startRoom"/> 出發，用同一張房間圖找出<b>路徑距離最近</b>的、
    /// 符合 <paramref name="predicate"/> 的房間；找不到回 -1。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 「距離」＝要穿過幾道<b>目前真的連通</b>的門（與 <see cref="Pathfind"/> 吃的是同一組
    /// <c>ConnectionN/S/W/E</c> 旗標、同一組邊界檢查），不是格子的直線距離，也不是房號差。
    /// </para>
    /// <para>
    /// 📌 <b>起點自己不算候選</b>：它是「我現在站的那一間」，選它等於原地打轉。
    /// </para>
    /// <para>
    /// 📌 距離相同時取<b>房號小</b>的。這不是美觀問題——目標每幀重算，
    /// 平手時若順序會抖，角色就會在兩間房之間來回。
    /// </para>
    /// <para>
    /// ⚠️ 未探索的房間拿不到它自己的連通旗標，所以 BFS <b>穿不過去</b>：
    /// 隔著未探索區的房間會被判成走不到（回 -1）。這與 <see cref="Pathfind"/> 的限制完全一樣，
    /// 呼叫端要自己準備退路。
    /// </para>
    /// </remarks>
    public int NearestRoom(int startRoom, Func<RoomFlags, bool> predicate)
    {
        if ((uint)startRoom >= 25)
            return -1;

        // 每間房最多進佇列一次 ⇒ 25 格夠用，而且完全不配置堆積記憶體（這是每幀路徑）
        Span<int> dist = stackalloc int[25];
        dist.Fill(-1);
        Span<int> queue = stackalloc int[25];

        dist[startRoom] = 0;
        queue[0] = startRoom;
        var head = 0;
        var tail = 1;

        while (head < tail)
        {
            var v = queue[head++];
            var md = Map[v];
            var nd = dist[v] + 1;
            for (var i = 0; i < 4; ++i)
            {
                var (flag, delta) = Neighbours[i];
                if (!md.HasFlag(flag))
                    continue;
                var w = v + delta;
                if ((uint)w >= 25 || dist[w] >= 0)
                    continue;
                dist[w] = nd;
                queue[tail++] = w;
            }
        }

        var best = -1;
        var bestDist = int.MaxValue;
        for (var r = 0; r < 25; ++r)
        {
            // dist == 0 只可能是起點自己
            if (dist[r] <= 0 || dist[r] >= bestDist)
                continue;
            if (!predicate(Map[r]))
                continue;
            best = r;
            bestDist = dist[r];
        }
        return best;
    }

    /// <summary>
    /// 四個方向的（連通旗標，房號位移）。
    /// </summary>
    /// <remarks>
    /// ⚠️ 位移與邊界處理刻意與 <see cref="Edges"/> <b>逐字相同</b>（含「右緣的 E 會繞到下一列」
    /// 這個既有行為——實際資料不會在邊緣掛連通旗標，這裡不「順手修正」它，
    /// 免得兩個函式對同一張圖給出不一樣的答案）。
    /// </remarks>
    private static readonly (RoomFlags Flag, int Delta)[] Neighbours =
    [
        (RoomFlags.ConnectionN, -5),
        (RoomFlags.ConnectionS, 5),
        (RoomFlags.ConnectionW, -1),
        (RoomFlags.ConnectionE, 1),
    ];

    private List<int> Edges(int roomIndex)
    {
        var md = Map[roomIndex];
        var edges = new List<int>(4);
        if (md.HasFlag(RoomFlags.ConnectionN))
            edges.Add(roomIndex - 5);
        if (md.HasFlag(RoomFlags.ConnectionS))
            edges.Add(roomIndex + 5);
        if (md.HasFlag(RoomFlags.ConnectionW))
            edges.Add(roomIndex - 1);
        if (md.HasFlag(RoomFlags.ConnectionE))
            edges.Add(roomIndex + 1);
        return edges;
    }
}
