namespace BossMod;

// 其他插件經 IPC 設的執行期設定覆寫：只活在記憶體，從不寫回設定欄位 ⇒ 存檔寫出的一律是使用者的值。
// 只收白名單內的欄位，不是通用的改設定入口；同一欄位多個擁有者時以最後一次設定的為準。
// 執行緒：Set／Clear／卸載清除可來自任何執行緒（表在鎖內）；讀取端每幀無鎖讀 volatile 快照。
// 鎖內不呼叫 ImGui、不做檔案 I/O、不取 Dalamud 的插件清單（呼叫端在鎖外先拿好）。
public static class TransientConfigOverrides
{
    public const string KeyEnableQuestBattles = "ZoneModuleConfig.EnableQuestBattles";
    public const string KeyClearPresetOnCombatEnd = "AutorotationConfig.ClearPresetOnCombatEnd";
    public const string KeyAIAutorotPresetName = "AIConfig.AIAutorotPresetName";

    // 字串覆寫的快照。每次 Set 都是新物件：讀取端用參考比較判斷「有沒有新的一次設定」，同值重設也算。
    // Value 為 null＝覆寫成「不選」。
    public sealed class StringOverride(string owner, string? value)
    {
        public readonly string Owner = owner;
        public readonly string? Value = value;
    }

    public readonly record struct Entry(string Owner, string Key, bool Flag, StringOverride? Text);

    private const int None = -1;

    private static readonly object _lock = new();
    private static readonly List<Entry> _entries = [];
    private static volatile int _enableQuestBattles = None;
    private static volatile int _clearPresetOnCombatEnd = None;
    private static volatile StringOverride? _aiAutorotPresetName;

    public static bool IsKnownBoolKey(string key) => key is KeyEnableQuestBattles or KeyClearPresetOnCombatEnd;
    public static bool IsKnownStringKey(string key) => key is KeyAIAutorotPresetName;
    public static bool IsKnownKey(string key) => IsKnownBoolKey(key) || IsKnownStringKey(key);

    public static bool? EnableQuestBattles => ToNullable(_enableQuestBattles);
    public static bool? ClearPresetOnCombatEnd => ToNullable(_clearPresetOnCombatEnd);
    public static StringOverride? AIAutorotPresetName => _aiAutorotPresetName;

    // ownerIsLoaded 在鎖內求值：與卸載清除互斥，避免「判定還在 → 卸載清完 → 才寫入」留下沒人清的覆寫。
    public static bool Set(string owner, string key, bool value, Func<bool> ownerIsLoaded)
        => !string.IsNullOrWhiteSpace(owner) && IsKnownBoolKey(key) && Add(new(owner, key, value, null), ownerIsLoaded);

    // 空白或 null 一律正規化成 null（覆寫成「不選」）；其餘去掉頭尾空白。
    public static bool SetString(string owner, string key, string? value, Func<bool> ownerIsLoaded)
    {
        if (string.IsNullOrWhiteSpace(owner) || !IsKnownStringKey(key))
            return false;
        var v = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        return Add(new(owner, key, false, new(owner, v)), ownerIsLoaded);
    }

    private static bool Add(Entry e, Func<bool> ownerIsLoaded)
    {
        lock (_lock)
        {
            if (!ownerIsLoaded())
                return false;
            _entries.RemoveAll(x => x.Owner == e.Owner && x.Key == e.Key);
            _entries.Add(e);
            Publish();
        }
        return true;
    }

    public static bool Clear(string owner, string key)
    {
        lock (_lock)
        {
            var removed = _entries.RemoveAll(e => e.Owner == owner && e.Key == key);
            if (removed > 0)
                Publish();
            return removed > 0;
        }
    }

    public static int ClearOwner(string owner)
    {
        lock (_lock)
        {
            var removed = _entries.RemoveAll(e => e.Owner == owner);
            if (removed > 0)
                Publish();
            return removed;
        }
    }

    public static List<Entry> Snapshot()
    {
        lock (_lock)
            return [.. _entries];
    }

    private static void Publish()
    {
        _enableQuestBattles = LatestFlag(KeyEnableQuestBattles);
        _clearPresetOnCombatEnd = LatestFlag(KeyClearPresetOnCombatEnd);
        _aiAutorotPresetName = Latest(KeyAIAutorotPresetName)?.Text;
    }

    private static Entry? Latest(string key)
    {
        for (var i = _entries.Count - 1; i >= 0; --i)
            if (_entries[i].Key == key)
                return _entries[i];
        return null;
    }

    private static int LatestFlag(string key) => Latest(key) is { } e ? (e.Flag ? 1 : 0) : None;

    private static bool? ToNullable(int v) => v == None ? null : v == 1;
}
