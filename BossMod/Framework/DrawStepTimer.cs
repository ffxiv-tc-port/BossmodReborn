using System.Diagnostics;

namespace BossMod;

// 繪製卡頓診斷：DrawUI 把一幀切成連續的段（BeginFrame → Next… → EndFrame），每個 BMR 視窗的 PreOpenCheck 也各開一段；
// 某一段單幀超過門檻就寫一行 Information（同一段 60 秒內最多一行）。
// 契約：BeginFrame／Next／EndFrame 只在繪製執行緒生效（別的執行緒呼叫一律忽略）；Start／Stop 是獨立的一小段，任何執行緒可用。
// 平常只讀時間戳與 GC 計數器，不配置、不寫 log；字串與 GC 資訊只在超過門檻時才組。
internal static class DrawStepTimer
{
    private const double SlowStepMs = 200d;
    private static readonly long SlowStepTicks = (long)(SlowStepMs * Stopwatch.Frequency / 1000d);
    private static readonly long LogThrottleTicks = 60L * Stopwatch.Frequency;

    public readonly struct Sample(long timestamp, long allocated, int gen0, int gen1, int gen2, TimeSpan pause)
    {
        public readonly long Timestamp = timestamp;
        public readonly long Allocated = allocated;
        public readonly int Gen0 = gen0;
        public readonly int Gen1 = gen1;
        public readonly int Gen2 = gen2;
        public readonly TimeSpan Pause = pause;

        public static Sample Take() => new(Stopwatch.GetTimestamp(), GC.GetAllocatedBytesForCurrentThread(), GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2), GC.GetTotalPauseDuration());
    }

    private static int _ownerThread;
    private static bool _active;
    private static Sample _segmentStart;
    private static string _area = "";
    private static string? _detail;

    // 鍵＝（段名, 細項）；只在超過門檻時才讀寫，鎖內只查表與寫回。
    private static readonly Dictionary<(string, string?), long> _lastLogged = [];
    private static readonly object _logGate = new();

    public static void BeginFrame(string area)
    {
        _ownerThread = Environment.CurrentManagedThreadId;
        _active = true;
        _area = area;
        _detail = null;
        _segmentStart = Sample.Take();
    }

    public static void Next(string area, string? detail = null)
    {
        if (!_active || Environment.CurrentManagedThreadId != _ownerThread)
            return;
        var now = Sample.Take();
        Check(_area, _detail, in _segmentStart, in now);
        _area = area;
        _detail = detail;
        _segmentStart = now;
    }

    public static void EndFrame()
    {
        if (!_active || Environment.CurrentManagedThreadId != _ownerThread)
            return;
        var now = Sample.Take();
        Check(_area, _detail, in _segmentStart, in now);
        _active = false;
    }

    public static Sample Start() => Sample.Take();

    // detail 只在超過門檻時才讀 Name（Type.Name 第一次讀會配置字串）
    public static void Stop(in Sample start, string area, Type detail)
    {
        var now = Sample.Take();
        if (now.Timestamp - start.Timestamp >= SlowStepTicks)
            Report(area, detail.Name, in start, in now);
    }

    private static void Check(string area, string? detail, in Sample start, in Sample end)
    {
        if (end.Timestamp - start.Timestamp >= SlowStepTicks)
            Report(area, detail, in start, in end);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Report(string area, string? detail, in Sample start, in Sample end)
    {
        try
        {
            lock (_logGate)
            {
                if (_lastLogged.TryGetValue((area, detail), out var last) && end.Timestamp - last < LogThrottleTicks)
                    return;
                if (_lastLogged.Count >= 256)
                    _lastLogged.Clear();
                _lastLogged[(area, detail)] = end.Timestamp;
            }
            var ms = (end.Timestamp - start.Timestamp) * 1000d / Stopwatch.Frequency;
            var where = detail == null ? $"「{area}」" : $"「{area}」（{detail}）";
            var bgc = GC.GetGCMemoryInfo(GCKind.Background);
            Service.Logger.Information($"[Draw] 單幀卡頓：{where}這一段花了 {ms:f0} ms（門檻 {SlowStepMs:f0} ms）。這段期間本執行緒配置 {(end.Allocated - start.Allocated) / 1024d:f0} KB、" +
                $"GC 次數 第0代 +{end.Gen0 - start.Gen0}／第1代 +{end.Gen1 - start.Gen1}／第2代 +{end.Gen2 - start.Gen2}、GC 暫停合計 {(end.Pause - start.Pause).TotalMilliseconds:f0} ms；" +
                $"最近一次完成的背景 GC 是第 {bgc.Index} 次（第2代累計 {end.Gen2} 次）。同一段 60 秒內只記一行。");
        }
        catch
        {
            // 診斷本身失敗不能影響繪製
        }
    }
}
