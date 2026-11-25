namespace BossMod;

// 其他插件經 IPC 要求「暫時不要自動移動與閃避」的租約表：每筆都會自己到期（上限 MaxSeconds），擁有者卸載時清掉。
// 生效時 BMR 不注入任何移動輸入（與按住「暫停自動移動」鍵同一道閘門），出招、減傷、提示照常。
// 玩家血量比低於該筆的 MinHpRatio 時那一筆不算數 ⇒ 呼叫端停擺時 BMR 這一側仍會在血量不夠時照常閃。
// 執行緒：Hold／Release 可來自任何執行緒（鎖內以新陣列發布）；每幀判定與 Effective 只讀 volatile 欄位、不取鎖。
// 鎖內不呼叫 ImGui、不做 I/O、不寫 log、不取 Dalamud 的插件清單（呼叫端在鎖外先拿好）。
public static class ExternalMovementHold
{
    public const float MaxSeconds = 15f;

    public readonly record struct Lease(string Owner, long ExpiresAtMs, float MinHpRatio);

    public enum State { None, Active, YieldLowHp }

    private static readonly object _lock = new();
    private static volatile Lease[] _leases = [];
    private static volatile bool _effective;

    /// <summary>上一次 <see cref="UpdateFrame"/> 判定的結果（每幀快照）；給 IPC 唯讀端點用。</summary>
    public static bool Effective => _effective;

    public static bool HasLeases => _leases.Length != 0;

    /// <summary>同一擁有者重送＝取代（續期）。秒數夾到 <see cref="MaxSeconds"/>；非有限數、≤0、空擁有者、擁有者未載入一律拒收。</summary>
    public static bool Hold(string owner, float seconds, float minHpRatio, long nowMs, Func<bool> ownerIsLoaded)
    {
        if (string.IsNullOrWhiteSpace(owner) || !float.IsFinite(seconds) || seconds <= 0f || !float.IsFinite(minHpRatio))
            return false;
        var lease = new Lease(owner, nowMs + (long)(Math.Min(seconds, MaxSeconds) * 1000f), Math.Clamp(minHpRatio, 0f, 1f));
        lock (_lock)
        {
            // ownerIsLoaded 在鎖內求值：與卸載清除互斥，不會留下沒人清的租約。
            if (!ownerIsLoaded())
                return false;
            var old = _leases;
            var next = new List<Lease>(old.Length + 1);
            foreach (var l in old)
                if (l.Owner != owner && l.ExpiresAtMs > nowMs)
                    next.Add(l);
            next.Add(lease);
            _leases = [.. next];
        }
        return true;
    }

    /// <summary>移除該擁有者的租約；回傳移除筆數（含已到期未清的）。</summary>
    public static int Release(string owner)
    {
        lock (_lock)
        {
            var old = _leases;
            var kept = Array.FindAll(old, l => l.Owner != owner);
            if (kept.Length == old.Length)
                return 0;
            _leases = kept;
            return old.Length - kept.Length;
        }
    }

    public static void PruneExpired(long nowMs)
    {
        var any = false;
        foreach (var l in _leases)
            any |= l.ExpiresAtMs <= nowMs;
        if (!any)
            return;
        lock (_lock)
            _leases = Array.FindAll(_leases, l => l.ExpiresAtMs > nowMs);
    }

    public static void Reset()
    {
        lock (_lock)
            _leases = [];
        _effective = false;
    }

    /// <summary>純判定：有未到期且血量門檻過關的租約＝Active；有未到期但全部被血量擋下＝YieldLowHp。</summary>
    /// <remarks>hpRatio 為 NaN（MaxHP 為 0）時任何門檻都不過關＝照常閃。</remarks>
    public static State Evaluate(Lease[] leases, long nowMs, bool playerAlive, float hpRatio, out string? owner)
    {
        owner = null;
        var state = State.None;
        if (!playerAlive)
            return state;
        foreach (var l in leases)
        {
            if (l.ExpiresAtMs <= nowMs)
                continue;
            owner ??= l.Owner;
            if (hpRatio >= l.MinHpRatio)
            {
                owner = l.Owner;
                return State.Active;
            }
            state = State.YieldLowHp;
        }
        return state;
    }

    /// <summary>框架執行緒每幀呼叫一次，發布 <see cref="Effective"/>。沒有任何租約時只讀一個欄位就返回。</summary>
    public static State UpdateFrame(long nowMs, bool playerAlive, float hpRatio, out string? owner)
    {
        var leases = _leases;
        owner = null;
        var state = leases.Length == 0 ? State.None : Evaluate(leases, nowMs, playerAlive, hpRatio, out owner);
        _effective = state == State.Active;
        return state;
    }
}
