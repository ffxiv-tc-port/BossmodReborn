namespace BossMod;

// 「每次事件寫 Debug、每分鐘彙總一行 Information」的計數器：Information 每個實例每分鐘最多一行。
// 契約：Record／Tick 可從任何執行緒呼叫；鎖內只做計數與組字串，寫 log 一律在鎖外。
// 視窗從第一筆事件起算 60 秒；之後的事件或 Tick 先把上一個視窗結算掉再開新的。
public sealed class MinuteLogDigest
{
    public const double WindowSeconds = 60d;

    private readonly string _prefix;
    private readonly string[] _labels;
    private readonly int[] _gapGroup; // -1＝不量間隔；同組的種類共用「與上一筆的間隔」
    private readonly string[] _gapLabels;
    private readonly object _lock = new();
    private readonly int[] _counts;
    private readonly DateTime[] _lastInGroup;
    private readonly double[] _minGap;
    private DateTime _windowStart, _windowLast;
    private string? _detail;

    public MinuteLogDigest(string prefix, string[] labels, int[] gapGroup, string[] gapLabels)
    {
        _prefix = prefix;
        _labels = labels;
        _gapGroup = gapGroup;
        _gapLabels = gapLabels;
        _counts = new int[labels.Length];
        _lastInGroup = new DateTime[gapLabels.Length];
        _minGap = new double[gapLabels.Length];
        Array.Fill(_minGap, double.MaxValue);
    }

    public void Record(int kind, DateTime now, string? detail = null)
    {
        string? summary;
        lock (_lock)
        {
            summary = TakeSummaryIfDue(now);
            if (_windowStart == default)
                _windowStart = now;
            if (now > _windowLast)
                _windowLast = now;
            ++_counts[kind];
            var g = _gapGroup[kind];
            if (g >= 0)
            {
                if (_lastInGroup[g] != default)
                {
                    var gap = (now - _lastInGroup[g]).TotalSeconds;
                    if (gap >= 0d && gap < _minGap[g])
                        _minGap[g] = gap;
                }
                _lastInGroup[g] = now;
            }
            if (detail != null)
                _detail = detail;
        }
        if (summary != null)
            Service.Logger.Information(summary);
    }

    public void Tick(DateTime now)
    {
        string? summary;
        lock (_lock)
            summary = TakeSummaryIfDue(now);
        if (summary != null)
            Service.Logger.Information(summary);
    }

    private string? TakeSummaryIfDue(DateTime now)
    {
        if (_windowStart == default || (now - _windowStart).TotalSeconds < WindowSeconds)
            return null;
        var sb = new StringBuilder();
        sb.Append(_prefix).Append(' ').Append(_windowStart.ToString("HH:mm:ss")).Append('～').Append(_windowLast.ToString("HH:mm:ss")).Append('：');
        var first = true;
        for (var i = 0; i < _labels.Length; ++i)
        {
            if (!first)
                sb.Append('、');
            first = false;
            sb.Append(_labels[i]).Append(' ').Append(_counts[i]).Append(" 次");
        }
        for (var g = 0; g < _gapLabels.Length; ++g)
        {
            sb.Append('；').Append(_gapLabels[g]).Append(' ');
            // 視窗裡這一組只有一筆、前面也沒有可比的事件 ⇒ 最短間隔「不知道」，不寫成 0。
            sb.Append(_minGap[g] == double.MaxValue ? "?" : _minGap[g].ToString("f2") + "s");
        }
        if (_detail != null)
            sb.Append('。').Append(_detail);
        Array.Clear(_counts);
        Array.Fill(_minGap, double.MaxValue);
        _windowStart = _windowLast = default;
        _detail = null;
        return sb.ToString();
    }
}
