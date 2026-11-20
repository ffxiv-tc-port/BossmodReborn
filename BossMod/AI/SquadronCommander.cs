using FFXIVClientStructs.FFXIV.Client.Game.UI;

namespace BossMod.AI;

// 本檔假設與契約：
// - 分隊攻略任務的領地＝GcArmyCapture 第一欄（副本列號）對到的 ContentFinderCondition 領地；分隊員＝隊伍裡 ActorType.Buddy
//   且身上有 GcArmyCaptureTactics 的作戰狀態（支援者／親信戰友也是 Buddy，但沒有這個狀態）。兩張表讀不到＝整個功能停用。
// - 只下「號令：戰鬥開始」(BgcArmyAction 1)，目標 id 直接帶給 UseAction，不碰玩家目標。
// - Update 只在框架執行緒跑；原生讀取全部經建構時注入的委派，每次現取、不保存指標；任何一步讀不到或擲例外＝這一幀不下令。

/// <summary>分隊攻略任務兩張表推出來的資料：分隊攻略任務的領地、分隊員身上的作戰狀態。</summary>
public static class SquadronCommandTables
{
    public static bool TryLoad(out HashSet<uint> territories, out HashSet<uint> tacticStatuses, out string error)
    {
        territories = [];
        tacticStatuses = [];
        error = "";
        var capture = Service.LuminaSheet<Lumina.Excel.Sheets.GcArmyCapture>();
        var cfc = Service.LuminaSheet<Lumina.Excel.Sheets.ContentFinderCondition>();
        var tactics = Service.LuminaSheet<Lumina.Excel.Sheets.GcArmyCaptureTactics>();
        if (capture == null || cfc == null || tactics == null)
        {
            error = $"讀不到表：GcArmyCapture={capture != null} ContentFinderCondition={cfc != null} GcArmyCaptureTactics={tactics != null}";
            return false;
        }
        var contents = new HashSet<uint>();
        foreach (var row in capture)
            if (row.Unknown0 != 0)
                contents.Add(row.Unknown0);
        foreach (var row in cfc)
            if (row.ContentLinkType == 1 && contents.Contains(row.Content.RowId) && row.TerritoryType.RowId != 0)
                territories.Add(row.TerritoryType.RowId);
        foreach (var row in tactics)
            if (row.Name.RowId != 0)
                tacticStatuses.Add(row.Name.RowId);
        if (territories.Count == 0 || tacticStatuses.Count == 0)
        {
            error = $"表內容不對：副本 {contents.Count} 個、推出領地 {territories.Count} 個、作戰狀態 {tacticStatuses.Count} 個";
            return false;
        }
        return true;
    }
}

/// <summary>
/// 號令時機與節流的純邏輯（不碰原生、不寫 log），框架元件與離線探針共用。
/// 宣告目標裡優先度最高那隻沒有至少 need 名分隊員（活著的，最多 2 名）在打任何宣告目標、且持續 LagSeconds ⇒ 該下令。
/// </summary>
public sealed class SquadronCommandPlanner
{
    public const double LagSeconds = 1.5;
    public const double SameTargetGap = 6;
    public const double AnyGap = 2.5;
    public const double VerifyDelay = 3;
    public const int MaxPerTarget = 3;

    public enum Decision { None, Issue, GiveUp }

    public readonly record struct Verification(ulong Target, DateTime IssuedAt, int OnTarget, int Alive, double? Latency, bool TargetGone);

    private sealed class TargetState
    {
        public int Count;
        public DateTime LastAt;
        public bool GaveUp;
    }

    private sealed class Pending
    {
        public ulong Target;
        public DateTime IssuedAt;
        public DateTime? FollowedAt;
    }

    private readonly Dictionary<ulong, TargetState> _targets = [];
    private readonly List<Pending> _pending = [];
    private ulong _lagTarget;
    private DateTime _lagSince;
    private DateTime _lastAny;

    public readonly List<Verification> Verified = [];
    public int Commands;
    public int Followed;
    public double LatencySum;
    public int GaveUpTargets;
    public bool HadDeclaration;

    public void Reset()
    {
        _targets.Clear();
        _pending.Clear();
        Verified.Clear();
        _lagTarget = 0;
        _lagSince = _lastAny = default;
        Commands = Followed = GaveUpTargets = 0;
        LatencySum = 0;
        HadDeclaration = false;
    }

    // squadTargets：活著的分隊員各自的 TargetID。目標死掉／消失／宣告撤掉時只是不再下令，不叫回首領（分隊會自己回去）。
    public Decision Update(DateTime now, ulong top, ReadOnlySpan<ulong> declared, ReadOnlySpan<ulong> squadTargets, Func<ulong, bool> targetAlive)
    {
        Verified.Clear();
        var need = Math.Min(2, squadTargets.Length);
        for (var i = _pending.Count - 1; i >= 0; --i)
        {
            var p = _pending[i];
            var on = Count(squadTargets, p.Target);
            if (p.FollowedAt == null && need > 0 && on >= need)
                p.FollowedAt = now;
            var gone = !targetAlive(p.Target);
            if (gone || (now - p.IssuedAt).TotalSeconds >= VerifyDelay)
            {
                double? latency = p.FollowedAt != null ? (p.FollowedAt.Value - p.IssuedAt).TotalSeconds : null;
                if (latency != null)
                {
                    ++Followed;
                    LatencySum += latency.Value;
                }
                Verified.Add(new(p.Target, p.IssuedAt, on, squadTargets.Length, latency, gone));
                _pending.RemoveAt(i);
            }
        }

        if (top == 0 || need == 0)
        {
            _lagTarget = 0;
            return Decision.None;
        }
        HadDeclaration = true;
        var following = 0;
        foreach (var t in squadTargets)
            if (declared.Contains(t))
                ++following;
        if (following >= need)
        {
            _lagTarget = 0;
            return Decision.None;
        }
        if (top != _lagTarget)
        {
            _lagTarget = top;
            _lagSince = now;
        }
        if ((now - _lagSince).TotalSeconds < LagSeconds)
            return Decision.None;

        if (!_targets.TryGetValue(top, out var st))
            _targets[top] = st = new();
        if (st.GaveUp || st.Count > 0 && (now - st.LastAt).TotalSeconds < SameTargetGap)
            return Decision.None;
        if (st.Count >= MaxPerTarget)
        {
            st.GaveUp = true;
            ++GaveUpTargets;
            return Decision.GiveUp;
        }
        if (_lastAny != default && (now - _lastAny).TotalSeconds < AnyGap)
            return Decision.None;
        return Decision.Issue;
    }

    public void Issued(DateTime now, ulong target)
    {
        if (!_targets.TryGetValue(target, out var st))
            _targets[target] = st = new();
        ++st.Count;
        st.LastAt = now;
        _lastAny = now;
        ++Commands;
        _pending.Add(new() { Target = target, IssuedAt = now });
    }

    public int IssuedTo(ulong target) => _targets.TryGetValue(target, out var st) ? st.Count : 0;
    public bool GaveUp(ulong target) => _targets.TryGetValue(target, out var st) && st.GaveUp;

    private static int Count(ReadOnlySpan<ulong> targets, ulong id)
    {
        var n = 0;
        foreach (var t in targets)
            if (t == id)
                ++n;
        return n;
    }
}

/// <summary>分隊攻略任務中，依首領模組的 <see cref="AIHints.Enemy.SquadronFocus"/> 宣告下「號令：戰鬥開始」。</summary>
public sealed class SquadronCommander
{
    public const uint CommandEngage = 1;

    /// <summary>回 false＝讀不到（這一幀不下令）。status 0＝遊戲說可以用；busy＝玩家動畫鎖／排隊／詠唱中。</summary>
    public delegate bool NativeGate(ulong target, out uint status, out bool busy, out bool hasHelpers);

    private readonly WorldState _ws;
    private readonly BossModuleManager _bmm;
    private readonly AIHints _hints;
    private readonly NativeGate _gate;
    private readonly Func<ulong, bool> _use;
    private readonly Action<string> _log;
    private readonly Func<bool> _enabled;
    private readonly Func<bool> _onFrameworkThread;
    public readonly SquadronCommandPlanner Planner = new();

    private HashSet<uint>? _territories;
    private HashSet<uint>? _tactics;
    private bool _tablesFailed;
    private ushort _zone;
    private DateTime _zoneSince;
    private BossModule? _encounter;
    private readonly HashSet<string> _loggedThisZone = [];
    private readonly ulong[] _squadTargets = new ulong[8];
    private readonly ulong[] _declared = new ulong[16];
    private readonly Func<ulong, bool> _isAlive;
    private DateTime _nextGateAt;

    public SquadronCommander(WorldState ws, BossModuleManager bmm, AIHints hints, NativeGate gate, Func<ulong, bool> use, Action<string> log, Func<bool> enabled, Func<bool> onFrameworkThread)
    {
        _ws = ws;
        _bmm = bmm;
        _hints = hints;
        _gate = gate;
        _use = use;
        _log = log;
        _enabled = enabled;
        _onFrameworkThread = onFrameworkThread;
        _isAlive = IsAlive;
    }

    // 實機用的原生閘門：UIState／ActionManager 每次現取；解析失敗擲的例外由 Update 接成「這一幀不下令」。
    public static unsafe bool ReadNativeGate(ulong target, out uint status, out bool busy, out bool hasHelpers)
    {
        hasHelpers = UIState.Instance()->Buddy.DutyHelperInfo.HasHelpers;
        status = ActionManagerEx.SquadronCommandStatus(CommandEngage, target, out busy);
        return true;
    }

    public void Update()
    {
        if (_tablesFailed || !_enabled())
            return;
        if (_territories == null)
        {
            if (!SquadronCommandTables.TryLoad(out var territories, out var tactics, out var error))
            {
                _tablesFailed = true;
                _log($"[分隊號令] 停用：{error}");
                return;
            }
            (_territories, _tactics) = (territories, tactics);
        }

        if (_ws.CurrentZone != _zone)
        {
            EndEncounter();
            _zone = _ws.CurrentZone;
            _zoneSince = _ws.CurrentTime;
            _loggedThisZone.Clear();
        }
        if (!_territories.Contains(_zone))
            return;
        if (!_onFrameworkThread())
        {
            Once("thread", "不在框架執行緒，不下令");
            return;
        }

        var squadCount = 0;
        var alive = 0;
        var tactic = false;
        for (var i = 0; i < PartyState.MaxAllies; ++i)
        {
            var a = _ws.Party[i];
            if (a == null || a.Type != ActorType.Buddy)
                continue;
            ++squadCount;
            tactic |= HasAnyStatus(a, _tactics!);
            if (!a.IsDead && alive < _squadTargets.Length)
                _squadTargets[alive++] = a.TargetID;
        }
        // 進場頭幾秒隊員與狀態還在陸續同步，原因行與啟用行等它穩定再印；啟用過之後隊員離隊＝副本結束，不再印原因
        var settled = (_ws.CurrentTime - _zoneSince).TotalSeconds >= 5 && !_loggedThisZone.Contains("enabled");
        if (squadCount == 0)
        {
            if (settled && !_loggedThisZone.Contains("notactic") && FirstTime("nobuddy"))
                _log($"[分隊號令] 領地 {_zone} 是分隊攻略任務的副本，但隊伍裡沒有 NPC 隊員：不下令");
            return;
        }
        if (!tactic)
        {
            if (settled && FirstTime("notactic"))
                _log($"[分隊號令] 領地 {_zone} 的 NPC 隊員身上都沒有分隊作戰狀態（支援者／親信戰友）：不下令");
            return;
        }
        if ((_ws.CurrentTime - _zoneSince).TotalSeconds >= 5 && FirstTime("enabled"))
            _log($"[分隊號令] 已啟用：領地={_zone}，分隊員 {squadCount} 人");

        var module = _bmm.ActiveModule?.StateMachine.ActivePhase != null ? _bmm.ActiveModule : null;
        if (!ReferenceEquals(module, _encounter))
        {
            EndEncounter();
            _encounter = module;
        }

        var nDeclared = 0;
        ulong top = 0;
        var topFocus = 0;
        foreach (var e in _hints.PotentialTargets)
        {
            if (module == null || e.SquadronFocus <= 0 || nDeclared >= _declared.Length)
                continue;
            _declared[nDeclared++] = e.Actor.InstanceID;
            if (e.SquadronFocus > topFocus && !Planner.GaveUp(e.Actor.InstanceID))
            {
                topFocus = e.SquadronFocus;
                top = e.Actor.InstanceID;
            }
        }

        var now = _ws.CurrentTime;
        var decision = Planner.Update(now, top, _declared.AsSpan(0, nDeclared), _squadTargets.AsSpan(0, alive), _isAlive);
        foreach (var v in Planner.Verified)
            _log($"[分隊號令] 「{NameOf(v.Target)}」：分隊員跟上 {v.OnTarget}/{v.Alive}，" + (v.Latency is { } l ? $"延遲 {l:f1} 秒" : "沒有跟上") + (v.TargetGone ? "（目標已死亡或消失）" : ""));
        if (decision == SquadronCommandPlanner.Decision.GiveUp)
            _log($"[分隊號令] 「{NameOf(top)}」下了 {SquadronCommandPlanner.MaxPerTarget} 次號令分隊員都沒有跟上，號令沒有效果，對這隻停手");
        if (decision != SquadronCommandPlanner.Decision.Issue || now < _nextGateAt)
            return;
        // 閘門沒過時隔一小段再問，不每幀讀原生
        _nextGateAt = now.AddSeconds(0.25);

        uint status;
        bool busy, hasHelpers;
        try
        {
            if (!_gate(top, out status, out busy, out hasHelpers))
                return;
        }
        catch (Exception ex)
        {
            Once("gateex", $"讀不到號令狀態，不下令：{ex.GetType().Name}: {ex.Message}");
            return;
        }
        if (!hasHelpers)
        {
            Once("nohelpers", "遊戲說目前沒有 NPC 隊員（DutyHelperInfo.HasHelpers=false）：不下令");
            return;
        }
        if (status != 0)
        {
            Once($"status{status}", $"號令不可用（GetActionStatus={status}）：不下令");
            return;
        }
        if (busy)
            return;

        bool ok;
        try
        {
            ok = _use(top);
        }
        catch (Exception ex)
        {
            ok = false;
            Once("useex", $"下令失敗：{ex.GetType().Name}: {ex.Message}");
        }
        Planner.Issued(now, top);
        var t = _ws.Actors.Find(top);
        _log($"[分隊號令] 對「{t?.Name}」({t?.OID:X}#{top & 0xFFFF:X}) 下「號令：戰鬥開始」（第 {Planner.IssuedTo(top)} 次）：規則={module?.GetType().Name}，分隊員目前目標＝{SquadTargetsText()}，UseAction 回傳={ok}");
    }

    private void EndEncounter()
    {
        if (_encounter != null && (Planner.HadDeclaration || Planner.Commands > 0))
        {
            var avg = Planner.Followed > 0 ? $"{Planner.LatencySum / Planner.Followed:f1} 秒" : "-";
            _log($"[分隊號令] 首領戰結束（{_encounter.GetType().Name}）：下令 {Planner.Commands} 次，3 秒內跟上 {Planner.Followed} 次，平均延遲 {avg}，放棄的目標 {Planner.GaveUpTargets} 隻");
        }
        _encounter = null;
        Planner.Reset();
    }

    // 每幀路徑先判 key 再組字串，已印過就不配置
    private bool FirstTime(string key) => _loggedThisZone.Add(key);

    private void Once(string key, string msg)
    {
        if (FirstTime(key))
            _log("[分隊號令] " + msg);
    }

    private bool IsAlive(ulong id) => _ws.Actors.Find(id) is { IsDead: false, IsTargetable: true };

    private static bool HasAnyStatus(Actor a, HashSet<uint> ids)
    {
        foreach (ref readonly var s in a.Statuses.AsSpan())
            if (s.ID != 0 && ids.Contains(s.ID))
                return true;
        return false;
    }

    private string NameOf(ulong id) => _ws.Actors.Find(id) is { } a ? $"{a.Name}({a.OID:X}#{id & 0xFFFF:X})" : $"{id:X}";

    // 不印分隊員與玩家的名字：分隊員用職能代稱，玩家寫「玩家」。
    private string SquadTargetsText()
    {
        var sb = new System.Text.StringBuilder();
        for (var i = 0; i < PartyState.MaxAllies; ++i)
        {
            var a = _ws.Party[i];
            if (a == null || a.Type != ActorType.Buddy)
                continue;
            if (sb.Length > 0)
                sb.Append('/');
            sb.Append(RoleLabel(a)).Append(':').Append(a.IsDead ? "陣亡" : TargetLabel(a.TargetID));
        }
        return sb.ToString();
    }

    private string TargetLabel(ulong id)
    {
        if (id is 0 or 0xE0000000)
            return "-";
        if (_ws.Party.Player()?.InstanceID == id)
            return "玩家";
        var a = _ws.Actors.Find(id);
        return a == null ? $"{id:X}" : a.Type == ActorType.Buddy ? RoleLabel(a) : $"{a.Name}";
    }

    private static string RoleLabel(Actor a) => a.Role switch
    {
        Role.Tank => "坦",
        Role.Healer => "補",
        Role.Melee => "近戰",
        Role.Ranged => "遠程",
        _ => "?"
    };
}
