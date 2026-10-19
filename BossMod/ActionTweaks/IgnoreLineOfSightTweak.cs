using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Object;

namespace BossMod;

// Ignore the client-side line-of-sight rejection when executing an action (ported from DailyRoutines' IgnoreActionTargetBlocked).
// ActionManager::GetActionInRangeOrLoS returns 0 or a LogMessage row id (562 line of sight / 565 arc / 566 range); its only caller in the game is UseActionLocation. The detour lets the original run and rewrites only 562 to 0, so range and arc still reject and other plugins that test `is 566` (Avarice, WrathCombo) observe no change.
// Safety: it never dereferences sourceObject/targetObject and stores no native pointer; if the signature drifts, HookAddress logs it and installs nothing (unlike DailyRoutines' byte patch, which would silently rewrite unrelated code).
// Deliberately not covered: the second 562 inside UseActionLocation (dashes/backsteps refusing to path through geometry). Not wired on purpose: ActionManagerEx.CheckActionLoS, which decides movement blocking before a cast and must keep treating out-of-LoS as 'do not block' so that AI cannot get stuck behind a wall.
// Caveat (stated in the tooltip as well): whether the server performs its own line-of-sight validation is unknown; if it does, the action simply fails the way any rejected action does, which is why this ships default off.
public sealed unsafe class IgnoreLineOfSightTweak : IDisposable
{
    // LogMessage row 562 ("看不到目標。") - the line-of-sight rejection, and the only value this tweak is allowed to swallow
    private const uint TargetNotInLineOfSight = 562;

    private readonly ActionTweaksConfig _config = Service.Config.Get<ActionTweaksConfig>();
    private readonly HookAddress<ActionManager.Delegates.GetActionInRangeOrLoS> _hook;
    private readonly ConfigListener<ActionTweaksConfig> _listener;

    public IgnoreLineOfSightTweak()
    {
        _hook = new(ActionManager.Addresses.GetActionInRangeOrLoS, GetActionInRangeOrLoSDetour, false);
        _listener = Service.Config.GetAndSubscribe<ActionTweaksConfig>(cfg => _hook.Enabled = cfg.IgnoreLineOfSight);
    }

    public void Dispose()
    {
        _listener.Dispose();
        _hook.Dispose();
    }

    // 刻意**不**包 try（見 Util/DetourGuard.cs 的 fail-closed 約定）：Original 之後的自訂邏輯只有一個 uint 比較加一個 bool 欄位讀取，
    // 不會擲受管理例外 —— _config 是建構時就取好的 readonly 參考，_hook.Original 只在 hook 沒安裝時擲例外，兩者都走不到。
    // sourceObject/targetObject 是裸指標，從不解參考、原封不動轉交 Original。
    private uint GetActionInRangeOrLoSDetour(uint actionId, GameObject* sourceObject, GameObject* targetObject)
    {
        // always let the game do the whole check, then reinterpret only its verdict; the config is re-read here because a detour can still
        // be in flight for one call after the hook is disabled
        var result = _hook.Original(actionId, sourceObject, targetObject);
        return result == TargetNotInLineOfSight && _config.IgnoreLineOfSight ? 0 : result;
    }
}
