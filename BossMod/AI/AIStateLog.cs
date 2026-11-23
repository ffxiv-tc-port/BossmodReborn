namespace BossMod.AI;

/// <summary>AI 開關、跟隨槽位、會讓 AI 不移動／不出招的設定，以及 AI 預設的一份快照。AI 關著時 MasterSlot 一律記成自己。</summary>
internal readonly record struct AIStateSnapshot(bool On, int MasterSlot, bool ForbidMovement, bool IdleWhileMounted, bool ForbidActions, string? Preset);

/// <summary>
/// 只在 AI 狀態<b>真的改變</b>時寫一行 log（附改前、改後、來源），讓事後只看紀錄就知道某一刻 AI 開著沒、能不能動。
/// </summary>
/// <remarks>
/// 契約：只在遊戲主執行緒呼叫；<c>source</c> 一律傳常數字串（同時是節流的鍵）；沒變的那一次不配置記憶體。
/// 同一來源 <see cref="WindowMs"/> 內最多逐筆記 <see cref="MaxLinesPerWindow"/> 行，其餘只計數，視窗到期補一行彙總（含當下狀態）。
/// 第一次呼叫不比對，只記一行起始狀態。
/// </remarks>
internal sealed class AIStateLog(Func<long> nowMs, Action<string> sink)
{
    public const long WindowMs = 60_000;
    public const int MaxLinesPerWindow = 6;

    private sealed class Window
    {
        public long Start;
        public int Logged;
        public int Suppressed;
    }

    private readonly Dictionary<string, Window> _windows = [];
    private int _pendingSummaries;
    private AIStateSnapshot _last;
    private bool _started;

    public void Note(in AIStateSnapshot cur, string source)
    {
        if (!_started)
        {
            _started = true;
            _last = cur;
            sink($"[BMR AI 狀態] 起始狀態｜目前：{Describe(cur)}");
            return;
        }
        if (_pendingSummaries != 0)
            FlushExpired(nowMs(), cur);
        if (cur == _last)
            return;

        var prev = _last;
        _last = cur;
        var now = nowMs();
        if (!_windows.TryGetValue(source, out var w))
            _windows[source] = w = new() { Start = now };
        else if (now - w.Start >= WindowMs)
        {
            w.Start = now;
            w.Logged = 0;
        }
        if (w.Logged >= MaxLinesPerWindow)
        {
            if (w.Suppressed++ == 0)
                ++_pendingSummaries;
            return;
        }
        ++w.Logged;
        sink($"[BMR AI 狀態] {Diff(prev, cur)}（來源：{source}）｜目前：{Describe(cur)}");
    }

    private void FlushExpired(long now, in AIStateSnapshot cur)
    {
        foreach (var (source, w) in _windows)
        {
            if (w.Suppressed == 0 || now - w.Start < WindowMs)
                continue;
            sink($"[BMR AI 狀態] 來源「{source}」在 {WindowMs / 1000} 秒內另改了 {w.Suppressed} 次沒有逐筆記（同一來源每 {WindowMs / 1000} 秒最多記 {MaxLinesPerWindow} 行）｜目前：{Describe(cur)}");
            w.Suppressed = 0;
            w.Logged = 0;
            w.Start = now;
            --_pendingSummaries;
        }
    }

    private static string OnOff(bool v) => v ? "開" : "關";
    private static string Slot(int slot) => slot == PartyState.PlayerSlot ? "自己" : $"第 {slot + 1} 格";
    private static string PresetName(string? p) => p != null ? $"「{p}」" : "（不選）";

    public static string Describe(in AIStateSnapshot s)
        => $"AI {OnOff(s.On)}{(s.On ? $"・跟隨 {Slot(s.MasterSlot)}" : "")}・禁止移動 {OnOff(s.ForbidMovement)}・騎乘時閒置 {OnOff(s.IdleWhileMounted)}・禁止技能 {OnOff(s.ForbidActions)}・AI 預設{PresetName(s.Preset)}";

    public static string Diff(in AIStateSnapshot a, in AIStateSnapshot b)
    {
        var parts = new List<string>(6);
        if (a.On != b.On)
            parts.Add($"AI {OnOff(a.On)}→{OnOff(b.On)}");
        if (b.On && (a.MasterSlot != b.MasterSlot || !a.On))
            parts.Add(a.On ? $"跟隨 {Slot(a.MasterSlot)}→{Slot(b.MasterSlot)}" : $"跟隨 {Slot(b.MasterSlot)}");
        if (a.ForbidMovement != b.ForbidMovement)
            parts.Add($"禁止移動 {OnOff(a.ForbidMovement)}→{OnOff(b.ForbidMovement)}");
        if (a.IdleWhileMounted != b.IdleWhileMounted)
            parts.Add($"騎乘時閒置 {OnOff(a.IdleWhileMounted)}→{OnOff(b.IdleWhileMounted)}");
        if (a.ForbidActions != b.ForbidActions)
            parts.Add($"禁止技能 {OnOff(a.ForbidActions)}→{OnOff(b.ForbidActions)}");
        if (!string.Equals(a.Preset, b.Preset, StringComparison.Ordinal))
            parts.Add($"AI 預設{PresetName(a.Preset)}→{PresetName(b.Preset)}");
        return string.Join("、", parts);
    }
}
