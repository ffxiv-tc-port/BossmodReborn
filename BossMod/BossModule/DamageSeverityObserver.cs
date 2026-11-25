namespace BossMod;

/// <summary>
/// 本場實測：敵方技能打中本隊夠多人時，記下打掉的最大 HP 中位比例，之後同一招的危險度只升不降（<see cref="Adjust"/>）。
/// </summary>
/// <remarks>
/// 只讀 <see cref="ActorState.CastEvent"/>（BMR 已解析好的技能效果），不碰封包。換區域就清空。
/// 只升不降：實測值已經被當下的減傷與盾壓低過，拿它降級會讓下一次少開減傷。
/// 只在主執行緒（WorldState 事件與 AIHints 消費端）使用，無鎖。
/// </remarks>
public sealed class DamageSeverityObserver : IDisposable
{
    public const float HighFraction = 0.5f;
    public const float MediumFraction = 0.25f;

    private readonly WorldState _ws;
    private readonly EventSubscriptions _subscriptions;
    private readonly Dictionary<uint, DamageSeverity> _observed = [];
    private ushort _cfc;

    public DamageSeverityObserver(WorldState ws)
    {
        _ws = ws;
        _cfc = ws.CurrentCFCID;
        _subscriptions = new
        (
            ws.Actors.CastEvent.Subscribe(OnCastEvent),
            ws.CurrentZoneChanged.Subscribe(_ => Reset())
        );
    }

    public void Dispose() => _subscriptions.Dispose();

    public int Count => _observed.Count;

    /// <summary><paramref name="baseSeverity"/> 與本場實測取較危險者；不同副本、AID 0 或沒實測過原樣回傳。</summary>
    public DamageSeverity Adjust(ushort cfc, uint aid, DamageSeverity baseSeverity)
    {
        if (aid == 0 || cfc != _cfc || !_observed.TryGetValue(aid, out var seen))
            return baseSeverity;
        return DamageSeverityTable.Rank(seen) > DamageSeverityTable.Rank(baseSeverity) ? seen : baseSeverity;
    }

    private static string Name(DamageSeverity s) => s switch
    {
        DamageSeverity.Low => "低",
        DamageSeverity.Medium => "中",
        DamageSeverity.High => "高",
        _ => "未知"
    };

    private void Reset()
    {
        _observed.Clear();
        _cfc = _ws.CurrentCFCID;
    }

    private void OnCastEvent(Actor source, ActorCastEvent ev)
    {
        if (source.IsAlly || !ev.IsSpell() || ev.Targets.Count == 0)
            return;
        if (_cfc != _ws.CurrentCFCID)
            Reset();

        var party = _ws.Party;
        var alive = 0;
        for (var i = 0; i < PartyState.MaxPartySize; ++i)
        {
            if (party[i] is { IsDead: false })
                ++alive;
        }
        if (alive < 2)
            return;

        Span<float> fractions = stackalloc float[PartyState.MaxPartySize];
        var hits = 0;
        var count = ev.Targets.Count;
        for (var i = 0; i < count && hits < fractions.Length; ++i)
        {
            var t = ev.Targets[i];
            var slot = party.FindSlot(t.ID);
            if (slot is < 0 or >= PartyState.MaxPartySize || party[slot] is not { } target || target.HPMP.MaxHP == 0)
                continue;
            var damage = 0;
            foreach (var eff in t.Effects)
            {
                if (eff.Type is ActionEffectType.Damage or ActionEffectType.BlockedDamage or ActionEffectType.ParriedDamage && !eff.AtSource)
                    damage += eff.DamageHealValue;
            }
            if (damage > 0)
                fractions[hits++] = (float)damage / target.HPMP.MaxHP;
        }
        // 打中不到存活隊員的一半＝不是全體傷害（點名、分攤、站錯位置），不拿來調分級
        if (hits < 2 || hits * 2 < alive)
            return;

        var hit = fractions[..hits];
        hit.Sort();
        var median = (hits & 1) == 1 ? hit[hits >> 1] : 0.5f * (hit[(hits >> 1) - 1] + hit[hits >> 1]);
        var seen = median >= HighFraction ? DamageSeverity.High : median >= MediumFraction ? DamageSeverity.Medium : DamageSeverity.Unknown;
        if (seen == DamageSeverity.Unknown)
            return;

        var aid = ev.Action.ID;
        var prev = _observed.TryGetValue(aid, out var p) ? p : DamageSeverity.Unknown;
        if (DamageSeverityTable.Rank(seen) <= DamageSeverityTable.Rank(prev))
            return;
        _observed[aid] = seen;

        var table = DamageSeverityTable.Get(_cfc, aid, AIHints.PredictedDamageType.Raidwide);
        if (DamageSeverityTable.Rank(seen) > DamageSeverityTable.Rank(table))
            Service.Logger.Information($"[DamageSeverity] 本場實測 {ev.Action} 打中本隊 {hits}/{alive} 人、中位 {median:P0} 最大 HP：危險度 {Name(table)}→{Name(seen)}，同一招後續照新分級（副本 {_cfc}）");
    }
}
