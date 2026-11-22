using System.Reflection;
using System.Text.Json;

namespace BossMod;

// 🔴 契約：數值就是 IPC Hints.NextRaidwideSeverity／NextTankbusterSeverity 的回傳值（外部插件照數字寫），不可重排或改值。
public enum DamageSeverity
{
    Unknown = 0,
    Low = 1,
    Medium = 2,
    High = 3
}

/// <summary>全體傷害／死刑的危險度：先查內嵌資料檔 <c>Data/DamageSeverity.json</c>，查不到依副本類型給預設。</summary>
/// <remarks>
/// 資料檔由 <c>~/.claude/tools/bmr-replay/audit/rwsev/gen_bmr_severity.py</c> 產生；鍵是 (CFC, AID)，CFC 0＝不分副本。
/// 查表順序：(本副本, AID) → (0, AID) → 副本類型預設。AID 0（元件沒填來源）直接走預設。
/// 只讀、無鎖：資料表載入一次後不再改；預設值只快取「上一個副本」一筆，以參考整筆替換。
/// </remarks>
public static class DamageSeverityTable
{
    public readonly record struct Entry(DamageSeverity Severity, int Hits, float Duration);

    private sealed record class DefaultRule(ushort Cfc, DamageSeverity Raidwide, DamageSeverity Tankbuster);

    private static readonly Lazy<Dictionary<(ushort Cfc, uint Aid), Entry>> _entries = new(Load);
    private static DefaultRule? _lastDefault;

    public static int Count => _entries.Value.Count;

    public static DamageSeverity Get(ushort cfc, uint aid, AIHints.PredictedDamageType type)
        => aid != 0 && TryGetEntry(cfc, aid, out var e) ? e.Severity : GetDefault(cfc, type);

    public static bool TryGetEntry(ushort cfc, uint aid, out Entry entry)
    {
        var t = _entries.Value;
        return t.TryGetValue((cfc, aid), out entry) || t.TryGetValue((0, aid), out entry);
    }

    public static DamageSeverity GetDefault(ushort cfc, AIHints.PredictedDamageType type)
    {
        var rule = _lastDefault;
        if (rule == null || rule.Cfc != cfc)
        {
            var (rw, tb) = ComputeDefault(cfc);
            _lastDefault = rule = new(cfc, rw, tb);
        }
        return type == AIHints.PredictedDamageType.Tankbuster ? rule.Tankbuster : rule.Raidwide;
    }

    // 零式／極／絕／滅／異聞的全體＝高是推定（沒有逐場證據）；死刑在這些副本仍給中。
    private static (DamageSeverity Raidwide, DamageSeverity Tankbuster) ComputeDefault(ushort cfc)
    {
        if (cfc == 0)
            return (DamageSeverity.Low, DamageSeverity.Low); // 野外：狩獵、F.A.T.E.
        try
        {
            if (Service.LuminaRow<Lumina.Excel.Sheets.ContentFinderCondition>(cfc) is not { } row)
                return (DamageSeverity.Unknown, DamageSeverity.Unknown);
            var type = row.ContentType.RowId;
            if (IsHighEnd(row, type))
                return (DamageSeverity.High, DamageSeverity.Medium);
            return type switch
            {
                2 or 4 or 5 or 30 => (DamageSeverity.Medium, DamageSeverity.Medium), // 迷宮、討伐、大型／團隊任務、多變迷宮
                7 or 9 or 26 or 29 or 38 => (DamageSeverity.Low, DamageSeverity.Low), // 任務戰鬥、尋寶、優雷卡、天佑女王、新月島
                _ => (DamageSeverity.Unknown, DamageSeverity.Unknown)
            };
        }
        catch (Exception ex)
        {
            Service.Logger.Warning(ex, $"[DamageSeverity] 查不到副本 {cfc} 的類型，危險度當未知。");
            return (DamageSeverity.Unknown, DamageSeverity.Unknown);
        }
    }

    // 舊版本的零式／極不帶 HighEndDuty 旗標，所以看介面分類名稱；字串是台服用字（本分支只跑台服）。
    private static bool IsHighEnd(Lumina.Excel.Sheets.ContentFinderCondition row, uint type)
    {
        if (type is 28 or 37 || row.HighEndDuty)
            return true;
        var ui = row.ContentUICategory.ValueNullable?.Name.ExtractText() ?? "";
        if (ui.Contains("零式", StringComparison.Ordinal) || ui.Contains("高難度", StringComparison.Ordinal))
            return true;
        if (type is 29 or 30)
        {
            var name = row.Name.ExtractText();
            return name.Contains("零式", StringComparison.Ordinal) || name.Contains("異聞", StringComparison.Ordinal);
        }
        return false;
    }

    private static Dictionary<(ushort Cfc, uint Aid), Entry> Load()
    {
        Dictionary<(ushort, uint), Entry> res = [];
        try
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("BossModReborn.Data.DamageSeverity.json");
            if (stream == null)
            {
                Service.Logger.Warning("[DamageSeverity] 找不到內嵌的危險度資料檔，全部走副本類型預設。");
                return res;
            }
            using var doc = JsonDocument.Parse(stream);
            foreach (var r in doc.RootElement.GetProperty("rows").EnumerateArray())
            {
                var cfc = r.GetProperty("cfc") is { ValueKind: JsonValueKind.Number } c ? c.GetUInt16() : (ushort)0;
                var sev = r.GetProperty("sev").GetInt32();
                if (sev is < 1 or > 3)
                    continue;
                res[(cfc, r.GetProperty("aid").GetUInt32())] = new((DamageSeverity)sev, r.GetProperty("hits").GetInt32(), r.GetProperty("dur").GetSingle());
            }
        }
        catch (Exception ex)
        {
            Service.Logger.Warning(ex, "[DamageSeverity] 危險度資料檔讀取失敗，全部走副本類型預設。");
            res.Clear();
        }
        return res;
    }
}
