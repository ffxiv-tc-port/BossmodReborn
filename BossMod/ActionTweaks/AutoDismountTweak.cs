namespace BossMod;

// Tweak to automatically dismount when trying to use an action and failing due to being mounted.
public sealed class AutoDismountTweak(WorldState ws)
{
    private readonly ActionTweaksConfig _config = Service.Config.Get<ActionTweaksConfig>();

    public bool AutoDismountEnabled => _config.AutoDismount;

    public bool IsMountPreventingAction(ActionID action) => IsMountPreventingAction(ws, action);

    public static bool IsMountPreventingAction(WorldState ws, ActionID action)
    {
        var player = ws.Party.Player();
        if (player == null || player.MountId == 0)
            return false;

        var canUseWhileMounted = action.Type switch
        {
            ActionType.Spell => Service.LuminaRow<Lumina.Excel.Sheets.Action>(action.ID) is var data && data != null && (data.Value.CanUseWhileMounted || data.Value.SecondaryCostType == 25),
            ActionType.General => action.ID == 20, // dig
            _ => false
        };
        return !canUseWhileMounted;
    }

    public bool AllowDismount()
    {
        var player = ws.Party.Player();
        var mountData = player != null && player.MountId != 0 ? Service.LuminaRow<Lumina.Excel.Sheets.Mount>(player.MountId) : null;
        return mountData != null && mountData.Value.Order >= 0;
    }

    /// <summary>
    /// 「技能被坐騎擋住」觸發的自動下坐騎要不要讓位給玩家；模組自己設的 <c>AIHints.WantDismount</c> 不經過這裡。
    /// 飛行中下坐騎＝強制降落：玩家自己在飛（AI 沒開）就不送。自動循環／AI 推的技能在玩家按著移動鍵時不送；手動按的照舊送。
    /// </summary>
    public static DismountSkip AutoDismountGate(bool enabled, bool manual, bool inFlight, bool aiActive, bool userSteering)
        => !enabled ? DismountSkip.AutoDismountOff
        : inFlight && !aiActive ? DismountSkip.InFlight
        : !manual && userSteering ? DismountSkip.UserSteering
        : DismountSkip.None;
}

// 這一幀為什麼沒有送「下坐騎」；None＝送了
public enum DismountSkip
{
    None,
    AutoDismountOff,
    InFlight,
    UserSteering,
    FollowPath,
    MountNotDismountable,
    AlreadyDismounted,
    Count
}

/// <summary>
/// 自動下坐騎的診斷：被坐騎擋住時每幀重試，所以一段只印開頭、每 <see cref="ProgressSeconds"/> 秒一行進度、結束一行總結。
/// 結束＝連續 <see cref="GraceSeconds"/> 秒沒有再被擋；持續超過 <see cref="LongSeconds"/> 秒的總結走 info（要使用者回報用）。
/// 只在框架執行緒呼叫。
/// </summary>
public sealed class DismountEpisodeLog(Action<string> debug, Action<string> info)
{
    public const double GraceSeconds = 0.5, ProgressSeconds = 10, LongSeconds = 5;

    private readonly int[] _skipFrames = new int[(int)DismountSkip.Count];
    private ActionID _first, _last;
    private DateTime _start, _lastSeen, _lastLog;
    private int _frames;

    public bool Open => _first != default;

    public void Blocked(DateTime now, ActionID action, bool manual, uint status, uint mountId, bool inFlight, bool inCombat, DismountSkip skip)
    {
        if (!Open)
        {
            _first = action;
            _start = _lastLog = now;
            _frames = 0;
            Array.Clear(_skipFrames);
            debug($"[AMEx][下坐騎] 開始：{action}（{(manual ? "手動" : "自動循環／AI")}）被坐騎擋住，status {status}；坐騎={mountId} 飛行={inFlight} 戰鬥={inCombat}；這一幀{SkipText(skip)}");
        }
        else if ((now - _lastLog).TotalSeconds >= ProgressSeconds)
        {
            _lastLog = now;
            debug($"[AMEx][下坐騎] 仍在等：{(now - _start).TotalSeconds:f1}s、{_frames} 幀，{Breakdown()}；目前 {action}，飛行={inFlight}，這一幀{SkipText(skip)}");
        }
        _last = action;
        _lastSeen = now;
        ++_frames;
        ++_skipFrames[(int)skip];
    }

    public void NotBlocked(DateTime now, bool stillMounted)
    {
        if (!Open || (now - _lastSeen).TotalSeconds < GraceSeconds)
            return;
        var seconds = (_lastSeen - _start).TotalSeconds;
        var msg = $"[AMEx][下坐騎] 結束：持續 {seconds:f1}s、{_frames} 幀，{Breakdown()}；第一個 {_first}，最後一個 {_last}；現在{(stillMounted ? "仍在坐騎上（技能已不再等）" : "已下坐騎")}";
        if (seconds >= LongSeconds)
            info(msg);
        else
            debug(msg);
        _first = _last = default;
    }

    private string Breakdown()
    {
        var sb = new StringBuilder($"送出下坐騎 {_skipFrames[(int)DismountSkip.None]} 幀");
        for (var i = 1; i < _skipFrames.Length; ++i)
            if (_skipFrames[i] > 0)
                sb.Append($"、{SkipText((DismountSkip)i)} {_skipFrames[i]} 幀");
        return sb.ToString();
    }

    private static string SkipText(DismountSkip skip) => skip switch
    {
        DismountSkip.None => "送出下坐騎",
        DismountSkip.AutoDismountOff => "沒送（自動下坐騎關著）",
        DismountSkip.InFlight => "沒送（飛行中、AI 沒開）",
        DismountSkip.UserSteering => "沒送（自動循環的技能，玩家正在操作移動）",
        DismountSkip.FollowPath => "沒送（vnavmesh 正在走路線）",
        DismountSkip.MountNotDismountable => "沒送（這種坐騎不自動下）",
        DismountSkip.AlreadyDismounted => "沒送（這一刻已經下坐騎了）",
        _ => "沒送（?）",
    };
}
