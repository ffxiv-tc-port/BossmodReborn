namespace BossMod;

// 讀條猜測（沒有模組時的 AIHintsBuilder、模組裡的 Components.UnclaimedCastAOEs）的逾時清除判斷。
// 契約：只看施放者與它當下的讀條，不看猜測當時算的生效時刻：讀條條滿後還掛著超過 Grace 秒、施放者倒地超過 Grace 秒、
// 施放者已不在物件表或讀條已經不在，才算殘留。錄影裡正常讀條條滿後 1 秒內就結束，Grace 內不會碰到還沒判定的招。
// 判斷不成立（例如遊戲回報的讀條時間永遠到不了條滿）時一律回 None＝照舊保留。只在主執行緒呼叫；沒有清除的幀不配置。
public struct CastGuessTimeout
{
    public const double Grace = 3d;

    public enum Verdict { None, Gone, Overdue, Dead }

    private ActorCastInfo? _cast;
    private DateTime _fullSince;
    private DateTime _deadSince;

    public static bool Enabled => _config.GenericZoneTimeout;
    private static readonly BossModuleConfig _config = Service.Config.Get<BossModuleConfig>();

    public Verdict Update(WorldState ws, Actor caster)
    {
        var cast = caster.CastInfo;
        if (cast == null || caster.IsDestroyed || !ReferenceEquals(ws.Actors.Find(caster.InstanceID), caster))
            return Verdict.Gone;
        if (!ReferenceEquals(_cast, cast))
        {
            _cast = cast;
            _fullSince = _deadSince = default;
        }
        var now = ws.CurrentTime;
        if (cast.ElapsedTime >= cast.TotalTime)
        {
            if (_fullSince == default)
                _fullSince = now;
            else if ((now - _fullSince).TotalSeconds > Grace + cast.Action.CastTimeExtra())
                return Verdict.Overdue;
        }
        else
            _fullSince = default;
        if (caster.IsDead)
        {
            if (_deadSince == default)
                _deadSince = now;
            else if ((now - _deadSince).TotalSeconds > Grace)
                return Verdict.Dead;
        }
        else
            _deadSince = default;
        return Verdict.None;
    }

    public static string Describe(Verdict v) => v switch
    {
        Verdict.Gone => "施放者或讀條已不在",
        Verdict.Overdue => $"讀條條滿後超過 {Grace:f0} 秒仍未結束",
        Verdict.Dead => $"施放者倒地超過 {Grace:f0} 秒",
        _ => ""
    };
}

// 逾時清除的使用者可回報彙總：每區第一次清除立即寫一行 Information，之後同一區最多每 60 秒一行（只在有新清除時寫）。只在主執行緒呼叫。
public sealed class CastGuessTimeoutReport(string owner)
{
    private const double Interval = 60d;
    private readonly string _owner = owner;
    private ushort _zone;
    private int _total, _pending, _gone, _overdue, _dead;
    private DateTime _lastWrite;
    private string _last = "";

    public void Record(WorldState ws, Actor caster, CastGuessTimeout.Verdict v)
    {
        var action = caster.CastInfo?.Action.ToString() ?? "-";
        Service.Logger.Debug($"[ZoneTTL] {_owner}：移除讀條猜測危險區（{CastGuessTimeout.Describe(v)}）：施放者 OID {caster.OID:X}、{action}");
        if (_zone != ws.CurrentZone)
        {
            _zone = ws.CurrentZone;
            _total = _pending = _gone = _overdue = _dead = 0;
            _lastWrite = default;
        }
        ++_total;
        ++_pending;
        switch (v)
        {
            case CastGuessTimeout.Verdict.Gone: ++_gone; break;
            case CastGuessTimeout.Verdict.Overdue: ++_overdue; break;
            case CastGuessTimeout.Verdict.Dead: ++_dead; break;
        }
        _last = $"OID {caster.OID:X}、{action}";
        Flush(ws.CurrentTime);
    }

    public void Flush(DateTime now)
    {
        if (_pending == 0 || _lastWrite != default && (now - _lastWrite).TotalSeconds < Interval)
            return;
        Service.Logger.Information($"[ZoneTTL] {_owner}：區域 {_zone} 已逾時清除讀條猜測危險區 {_total} 個（讀條卡住 {_overdue}、施放者倒地 {_dead}、施放者或讀條已不在 {_gone}），本段新增 {_pending} 個，最近一個 {_last}。設定「通用危險區逾時清除」可關閉。");
        _pending = 0;
        _lastWrite = now;
    }
}
