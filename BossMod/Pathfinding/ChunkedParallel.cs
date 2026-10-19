using System.Runtime.ExceptionServices;
using System.Threading;

namespace BossMod.Pathfinding;

/// <summary>分段平行的一段工作：處理 [from, to)。</summary>
/// <remarks>與 <c>Parallel.For</c> 逐位元相同的前提＝每個索引只寫自己的格子、跨段的歸併可交換（整數加總、取最大值）。</remarks>
internal abstract class RangeJob
{
    public abstract void Run(int from, int to);
}

/// <summary>每幀尋路用的分段平行，穩定狀態下不配置（<c>Parallel.For</c> 每次約 3 KB）。</summary>
/// <remarks>
/// 每條呼叫端執行緒一份（<see cref="Local"/>）；呼叫端自己也領段來做，執行緒集區的幫手只是加速，
/// 所以集區塞住時不會卡住，只是變成單執行緒做完。返回前會等每個已開工的幫手收手，
/// 之後不會再有任何執行緒碰這次的工作。段裡擲的例外在呼叫端原樣重擲（不包 AggregateException）。
/// </remarks>
internal sealed class ChunkedParallel
{
    [ThreadStatic] private static ChunkedParallel? _local;
    public static ChunkedParallel Local => _local ??= new();

    /// <summary>呼叫端之外最多叫幾個幫手；0＝在呼叫端執行緒上依序做完。</summary>
    /// <remarks>預設＝處理器數－1，與 Parallel.For 的平行度相同：危險區多的幀要夠多幫手才追得上，少叫幾個 p95 就變慢。</remarks>
    public static int MaxHelpers = Math.Max(0, Environment.ProcessorCount - 1);
    /// <summary>每個參與者（呼叫端＋幫手）平均分到幾段：段越多越能攤平各列成本不均，段越少搶段的原子操作越少。</summary>
    public static int ChunksPerWorker = 4;

    private readonly Helper[] _helpers = new Helper[Math.Max(1, Environment.ProcessorCount - 1)];
    private RangeJob? _job;
    private int _count;
    private int _chunks;
    private int _next;
    private int _failed;
    private ExceptionDispatchInfo? _error;

    private ChunkedParallel()
    {
        for (var i = 0; i < _helpers.Length; ++i)
            _helpers[i] = new(this);
    }

    public void For(RangeJob job, int count)
    {
        if (count <= 0)
            return;
        var helpers = Math.Clamp(MaxHelpers, 0, _helpers.Length);
        var chunks = Math.Min(count, (helpers + 1) * Math.Max(1, ChunksPerWorker));
        if (helpers == 0 || chunks <= 1)
        {
            job.Run(0, count);
            return;
        }
        _job = job;
        _count = count;
        _chunks = chunks;
        _failed = 0;
        _error = null;
        Volatile.Write(ref _next, 0);
        var armed = Math.Min(helpers, chunks - 1);
        for (var i = 0; i < armed; ++i)
            _helpers[i].Arm();
        RunChunks();
        for (var i = 0; i < armed; ++i)
            _helpers[i].Disarm();
        _job = null;
        var error = _error;
        _error = null;
        error?.Throw();
    }

    private void RunChunks()
    {
        while (Volatile.Read(ref _failed) == 0)
        {
            var k = Interlocked.Increment(ref _next) - 1;
            if (k >= _chunks)
                return;
            var from = (int)((long)k * _count / _chunks);
            var to = (int)((long)(k + 1) * _count / _chunks);
            try
            {
                _job!.Run(from, to);
            }
            catch (Exception ex)
            {
                if (Interlocked.Exchange(ref _failed, 1) == 0)
                    _error = ExceptionDispatchInfo.Capture(ex);
            }
        }
    }

    // 一次 For 之內「預備」一次；集區裡可能還躺著上一次排的同一個物件，所以每次執行都先搶「預備→執行中」，搶不到就什麼都不做。
    private sealed class Helper(ChunkedParallel owner) : IThreadPoolWorkItem
    {
        private const int Idle = 0, Armed = 1, Running = 2;
        private int _state;
        private int _queued;

        public void Arm()
        {
            Volatile.Write(ref _state, Armed);
            if (Interlocked.Exchange(ref _queued, 1) == 0)
                ThreadPool.UnsafeQueueUserWorkItem(this, preferLocal: false);
        }

        public void Execute()
        {
            Volatile.Write(ref _queued, 0);
            if (Interlocked.CompareExchange(ref _state, Running, Armed) != Armed)
                return;
            owner.RunChunks();
            Volatile.Write(ref _state, Idle);
        }

        // 還沒開工的直接收回；已開工的等它做完手上那段（不睡 1 毫秒：計時器解析度會把等待放大到十幾毫秒）
        public void Disarm()
        {
            if (Interlocked.CompareExchange(ref _state, Idle, Armed) == Armed)
                return;
            var spin = new SpinWait();
            while (Volatile.Read(ref _state) == Running)
                spin.SpinOnce(sleep1Threshold: -1);
        }
    }
}
