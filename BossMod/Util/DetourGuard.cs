using System.Threading;

namespace BossMod;

// detour 是「原生程式碼直接呼叫的受管理函式」：受管理例外從 detour 逸出會穿過沒有 SEH handler 的原生呼叫框架，行程極可能直接被終止。
// fail-closed 約定：每一支 detour 的自訂邏輯包在 try 裡、Original 留在 try 外，catch 之後照樣呼叫 Original 並回傳它的結果。
// ⚠️ 攔不到 AccessViolationException（.NET Core 的 corrupted-state exception）；防的是受管理例外，裸指標靠判空。
// try/catch 在不擲例外時近乎零成本，熱路徑也可以包；要避免的是在 catch 以外做額外工作。
public static class DetourGuard
{
    // 同一個 detour 最多每 60 秒印一行。封包 detour 一旦開始持續擲例外，
    // 不節流會在幾秒內把 log 灌爆、反而讓使用者回報不出有用的東西。
    private const long ThrottleMillis = 60_000;

    private sealed class SiteState
    {
        public long LastReportMs;
        public int Total;
    }

    private static readonly ConcurrentDictionary<string, SiteState> _sites = [];

    /// <summary>
    /// 在 detour 的 catch 區塊裡呼叫。節流地記一行要使用者回報的診斷（Information），
    /// 然後靜靜返回 —— 呼叫端必須繼續呼叫 Original。
    /// </summary>
    /// <param name="site">出事的 detour 名稱，用 nameof() 傳。</param>
    /// <param name="ex">攔下來的受管理例外。</param>
    // NoInlining：把字串格式化與字典操作留在這裡，detour 本體的程式碼才不會被撐大。
    // 格式化只有真的 catch 到時才發生。
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Report(string site, Exception ex)
    {
        try
        {
            var state = _sites.GetOrAdd(site, static _ => new SiteState());
            var total = Interlocked.Increment(ref state.Total);

            var now = Environment.TickCount64;
            var last = Volatile.Read(ref state.LastReportMs);
            if (last != 0L && now - last < ThrottleMillis)
                return; // 節流窗內：只累計次數，不印
            Volatile.Write(ref state.LastReportMs, now);

            Service.Logger.Information($"[BMR][detour-guard] {site} 擲出未處理的受管理例外（累計 {total} 次，已吞下，Original 照常執行）: {ex}");
        }
        catch
        {
            // 記錄本身絕不能把例外送回原生框架 —— 這個 catch 是刻意留白的。
        }
    }
}
