namespace BossMod.Pathfinding;

// 閃避預判：已登記與未來危險區依生效時間分波，從最後一波往回推每一波的「可站範圍」
// （這一波打不到，而且在兩波之間來得及走進下一波的可站範圍）；安全格裡接不上第一波的扣重分，
// 接得上的依「離下一波可站範圍多遠」再扣一點（離得越近越好，plateau 內不扣）。
// 契約：只改 map.PixelPriority／MaxPriority，不碰 PixelMaxG ⇒ 危險格不會因此變成可去；
// 安全格全部接得上或全部接不上時一格都不改（＝現行行為）。
public static class DodgeLookahead
{
    public const int MaxWaves = 6;
    public const float HorizonSeconds = 25f;
    // 壓過保持距離、繞側背、閃避方向等小權重，低於互動目標的 100
    public const float Penalty = 8f;
    // 接得上的格子最多再扣這麼多（恆小於 Penalty ⇒ 接得上的永遠勝過接不上的）
    public const float GradeMax = 3f;
    // 離下一波可站範圍這麼近以內一律不扣（至少這麼多；呼叫端再依「與禁區保持距離」加寬）：
    // 讓那個緩衝照常挑位置，不要逼人貼著危險區邊緣站（1 碼時離線模擬會停在離圈邊 0.4 碼處）
    public const float MinPlateauYalms = 2f;
    private const float WaveClusterSeconds = 0.5f; // 與光柵化的分群相同
    private const byte Impassable = 0x80;

    public sealed class Buffers
    {
        public float[] Times = [];
        public int[] RegisteredWave = [];
        public int[] FutureWave = [];
        public readonly float[] WaveStart = new float[MaxWaves];
        public readonly float[] WaveEnd = new float[MaxWaves];
        public byte[] Cover = [];
        public byte[] Set = [];
        public int[] Dist = [];
        public int[] Queue = [];
        public bool[] Queued = [];
        public bool Steering;
        public bool ErrorLogged;
        public float[] PrioBackup = [];
        public float MaxPrioBackup;
        public bool PriorityDirty; // 扣分迴圈寫到一半（只有那段之後才會動到 map）
    }

    // 扣分寫到一半就出錯時，把權重放回扣分前（危險度本來就沒動過）
    public static void RestoreIfDirty(Buffers b, Map map)
    {
        if (!b.PriorityDirty)
            return;
        Array.Copy(b.PrioBackup, map.PixelPriority, map.Width * map.Height);
        map.MaxPriority = b.MaxPrioBackup;
        b.PriorityDirty = false;
    }

    public struct Result
    {
        public bool Applied;
        public bool StartGood;
        public int Waves;
        public float FirstWaveIn;
        public int GoodCells;
        public int SafeCells;
        // 玩家這一格接得上時：最晚幾秒後要動身才走得進下一波的可站範圍（已經在裡面＝MaxValue）
        public float MoveBySeconds;
    }

    public static bool IsGood(Buffers b, int cell) => (uint)cell < (uint)b.Set.Length && b.Set[cell] != 0;

    public static Result Apply(Buffers b, Map map, (Func<WPos, float> shapeDistance, DateTime activation, ulong source)[] registered,
        (Func<WPos, float> shapeDistance, DateTime activation)[] future, DateTime now, float speed, float reactionSeconds, float plateauYalms, int startCell)
    {
        var nr = registered.Length;
        var nf = future.Length;
        Grow(ref b.Times, nr + nf);
        Grow(ref b.RegisteredWave, nr);
        Grow(ref b.FutureWave, nf);

        var count = 0;
        for (var i = 0; i < nr; ++i)
        {
            var t = RegisteredTime(registered[i].activation, now);
            if (t <= HorizonSeconds)
                b.Times[count++] = t;
        }
        var anyFuture = false;
        for (var i = 0; i < nf; ++i)
        {
            var t = (float)(future[i].activation - now).TotalSeconds;
            if (t > 0f && t <= HorizonSeconds)
            {
                b.Times[count++] = t;
                anyFuture = true;
            }
        }
        if (!anyFuture)
            return default;

        Array.Sort(b.Times, 0, count);
        var waves = 1;
        b.WaveStart[0] = b.WaveEnd[0] = b.Times[0];
        for (var i = 1; i < count; ++i)
        {
            var t = b.Times[i];
            if (t > b.WaveStart[waves - 1] + WaveClusterSeconds)
            {
                if (waves == MaxWaves)
                    break;
                b.WaveStart[waves] = b.WaveEnd[waves] = t;
                ++waves;
            }
            else
                b.WaveEnd[waves - 1] = t;
        }

        for (var i = 0; i < nr; ++i)
        {
            var t = RegisteredTime(registered[i].activation, now);
            b.RegisteredWave[i] = t <= HorizonSeconds ? WaveOf(b, waves, t) : -1;
        }
        var futureInWaves = false;
        for (var i = 0; i < nf; ++i)
        {
            var t = (float)(future[i].activation - now).TotalSeconds;
            var w = t > 0f && t <= HorizonSeconds ? WaveOf(b, waves, t) : -1;
            b.FutureWave[i] = w;
            futureInWaves |= w >= 0;
        }
        if (!futureInWaves)
            return default;

        var res = new Result { Waves = waves, FirstWaveIn = b.WaveStart[0] };
        var cells = map.Width * map.Height;
        Grow(ref b.Cover, cells);
        Grow(ref b.Set, cells);
        ComputeCover(b, map, registered, future);

        // 最後一波：打不到的可通行格
        var lastBit = 1 << (waves - 1);
        var cover = b.Cover;
        var set = b.Set;
        for (var i = 0; i < cells; ++i)
            set[i] = (byte)((cover[i] & (lastBit | Impassable)) == 0 ? 1 : 0);
        var gradeLimit = 0; // 第一波→第二波那次擴張的上限（Dist 的單位）；0＝沒擴張，不分級
        for (var k = waves - 2; k >= 0; --k)
        {
            var budget = b.WaveStart[k + 1] - b.WaveEnd[k] - reactionSeconds;
            gradeLimit = Dilate(b, map, budget > 0f ? budget * speed : 0f);
            var bit = 1 << k;
            var any = false;
            for (var i = 0; i < cells; ++i)
            {
                if (set[i] != 0 && (cover[i] & bit) != 0)
                    set[i] = 0;
                any |= set[i] != 0;
            }
            if (!any)
                return res; // 整串接不起來 ⇒ 不扣分
        }

        var maxG = map.PixelMaxG;
        int good = 0, safe = 0;
        for (var i = 0; i < cells; ++i)
        {
            if (maxG[i] == float.MaxValue)
            {
                ++safe;
                if (set[i] != 0)
                    ++good;
            }
        }
        res.GoodCells = good;
        res.SafeCells = safe;
        res.StartGood = IsGood(b, startCell);
        res.MoveBySeconds = float.MaxValue;
        if (res.StartGood && gradeLimit > 0 && b.Dist[startCell] > 0)
            res.MoveBySeconds = Math.Max(0f, b.WaveStart[1] - reactionSeconds - b.Dist[startCell] * (map.Resolution / 5f) / speed);
        if (good == 0 || good == safe)
            return res;

        var prio = map.PixelPriority;
        Grow(ref b.PrioBackup, cells);
        Array.Copy(prio, b.PrioBackup, cells);
        b.MaxPrioBackup = map.MaxPriority;
        b.PriorityDirty = true;
        var maxPrio = float.MinValue;
        var unitYalms = map.Resolution / 5f;
        var span = gradeLimit * unitYalms - plateauYalms;
        var dist = b.Dist;
        for (var i = 0; i < cells; ++i)
        {
            if (maxG[i] != float.MaxValue)
                continue;
            if (set[i] == 0)
                prio[i] -= Penalty;
            else if (span > 0f && dist[i] * unitYalms - plateauYalms is var excess && excess > 0f)
                prio[i] -= GradeMax * Math.Min(1f, excess / span);
            if (prio[i] > maxPrio)
                maxPrio = prio[i];
        }
        map.MaxPriority = maxPrio;
        b.PriorityDirty = false;
        res.Applied = true;
        return res;
    }

    // 已過生效時間的已登記區域（持續區、預估時間已過的段）當作「現在」
    private static float RegisteredTime(DateTime activation, DateTime now) => Math.Max(0f, (float)(activation - now).TotalSeconds);

    private static int WaveOf(Buffers b, int waves, float t)
    {
        for (var w = waves - 1; w >= 0; --w)
            if (t >= b.WaveStart[w])
                return t <= b.WaveStart[w] + WaveClusterSeconds ? w : -1;
        return -1;
    }

    private static void ComputeCover(Buffers b, Map map, (Func<WPos, float> shapeDistance, DateTime activation, ulong source)[] registered, (Func<WPos, float> shapeDistance, DateTime activation)[] future)
    {
        var job = CoverJob.Local;
        job.Map = map;
        job.Width = map.Width;
        // 格心離形狀 0.71 格以內就算打得到：涵蓋光柵化「任一角落在內或格心在半格內」的判法
        job.Threshold = map.Resolution * 0.71f;
        job.Cover = b.Cover;
        job.RegisteredWave = b.RegisteredWave;
        job.FutureWave = b.FutureWave;
        job.Registered = registered;
        job.Future = future;
        try
        {
            ChunkedParallel.Local.For(job, map.Height);
        }
        finally
        {
            job.Clear();
        }
    }

    // ComputeCover 每一段（列）的本體（原本是 Parallel.For 的 lambda，逐式照搬）。每條呼叫端執行緒一份。
    private sealed class CoverJob : RangeJob
    {
        [ThreadStatic] private static CoverJob? _local;
        public static CoverJob Local => _local ??= new();

        public Map Map = null!;
        public int Width;
        public float Threshold;
        public byte[] Cover = [];
        public int[] RegisteredWave = [];
        public int[] FutureWave = [];
        public (Func<WPos, float> shapeDistance, DateTime activation, ulong source)[] Registered = [];
        public (Func<WPos, float> shapeDistance, DateTime activation)[] Future = [];

        public void Clear()
        {
            Map = null!;
            Cover = [];
            RegisteredWave = [];
            FutureWave = [];
            Registered = [];
            Future = [];
        }

        public override void Run(int from, int to)
        {
            var map = Map;
            var width = Width;
            var threshold = Threshold;
            var cover = Cover;
            var rw = RegisteredWave;
            var fw = FutureWave;
            var registered = Registered;
            var future = Future;
            for (var y = from; y < to; ++y)
            {
                var row = y * width;
                for (var x = 0; x < width; ++x)
                {
                    var idx = row + x;
                    if (map.PixelMaxG[idx] < 0f)
                    {
                        cover[idx] = Impassable;
                        continue;
                    }
                    var p = map.GridToWorld(x, y, 0.5f, 0.5f);
                    var m = 0;
                    for (var i = 0; i < registered.Length; ++i)
                    {
                        var w = rw[i];
                        if (w >= 0 && (m & (1 << w)) == 0 && registered[i].shapeDistance(p) < threshold)
                            m |= 1 << w;
                    }
                    for (var i = 0; i < future.Length; ++i)
                    {
                        var w = fw[i];
                        if (w >= 0 && (m & (1 << w)) == 0 && future[i].shapeDistance(p) < threshold)
                            m |= 1 << w;
                    }
                    cover[idx] = (byte)m;
                }
            }
        }
    }

    // 從目前可站範圍沿可通行格往外擴 radius（8 方向，正交 5、斜向 7 的整數距離；斜走不切牆角）
    // 回傳用到的上限（Dist 的單位）；0＝沒擴張，Dist 不更新
    private static int Dilate(Buffers b, Map map, float radius)
    {
        var limit = (int)(radius / map.Resolution * 5f);
        if (limit <= 0)
            return 0;
        var width = map.Width;
        var height = map.Height;
        var cells = width * height;
        Grow(ref b.Dist, cells);
        Grow(ref b.Queue, cells);
        Grow(ref b.Queued, cells);
        var dist = b.Dist;
        var queue = b.Queue;
        var queued = b.Queued;
        var set = b.Set;
        var cover = b.Cover;
        int head = 0, size = 0;
        for (var i = 0; i < cells; ++i)
        {
            queued[i] = false;
            dist[i] = set[i] != 0 ? 0 : int.MaxValue;
        }
        // 只從邊界往外擴：內部格的鄰居也在範圍內，擴不出任何東西
        for (var y = 0; y < height; ++y)
        {
            var row = y * width;
            for (var x = 0; x < width; ++x)
            {
                var i = row + x;
                if (set[i] != 0 && (x > 0 && set[i - 1] == 0 || x < width - 1 && set[i + 1] == 0 || y > 0 && set[i - width] == 0 || y < height - 1 && set[i + width] == 0))
                {
                    queued[i] = true;
                    queue[size++] = i;
                }
            }
        }
        while (size > 0)
        {
            var c = queue[head];
            head = head + 1 == cells ? 0 : head + 1;
            --size;
            queued[c] = false;
            var d = dist[c];
            var cy = c / width;
            var cx = c - cy * width;
            for (var oy = -1; oy <= 1; ++oy)
            {
                var ny = cy + oy;
                if (ny < 0 || ny >= height)
                    continue;
                for (var ox = -1; ox <= 1; ++ox)
                {
                    var nx = cx + ox;
                    if (ox == 0 && oy == 0 || nx < 0 || nx >= width)
                        continue;
                    var n = ny * width + nx;
                    if ((cover[n] & Impassable) != 0)
                        continue;
                    var diag = ox != 0 && oy != 0;
                    if (diag && ((cover[cy * width + nx] & Impassable) != 0 || (cover[ny * width + cx] & Impassable) != 0))
                        continue;
                    var nd = d + (diag ? 7 : 5);
                    if (nd > limit || nd >= dist[n])
                        continue;
                    dist[n] = nd;
                    if (!queued[n])
                    {
                        queued[n] = true;
                        var tail = head + size;
                        queue[tail >= cells ? tail - cells : tail] = n;
                        ++size;
                    }
                }
            }
        }
        for (var i = 0; i < cells; ++i)
            set[i] = (byte)(dist[i] <= limit ? 1 : 0);
        return limit;
    }

    private static void Grow<T>(ref T[] arr, int size)
    {
        if (arr.Length < size)
            arr = new T[size];
    }
}
