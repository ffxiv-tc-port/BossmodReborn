using FFXIVClientStructs.FFXIV.Client.Game;
using CSActionType = FFXIVClientStructs.FFXIV.Client.Game.ActionType;

namespace BossMod;

// Long-cast reduction tweak (ported from DailyRoutines' OptimizedLongCastTimeAction).
// UseActionLocation stores GetAdjustedCastTime into ActionManager.CastTimeTotal, which is exactly 'when may I request the next action'; the
// server computes the duration itself and resolves ~0.5s before the client's bar completes (slidecast window), so shortening the client's
// timer only reclaims local idle time - nothing is sent early. The `recast <= cast` guard means it only triggers when the cast is the bottleneck.
// Scoping: GetAdjustedCastTime is a global static also called by game UI, BossMod itself every frame and other plugins, so the detour only applies the reduction while _gameExecutionDepth > 0, which ActionManagerEx sets exclusively around the original UseActionLocation call.
public sealed unsafe class CastTimeReductionTweak : IDisposable
{
    // hard cap: the reduction must stay strictly below the ~0.5s slidecast window, otherwise the client would finish the cast before the server released it
    public const int MaxReductionMS = 400;
    // never shorten a cast to less than this; below the slidecast window the server has effectively already resolved on request, and a near-zero
    // cast time would make the game treat the action as an instant instead
    public const int MinResultingCastMS = 500;

    private readonly ActionTweaksConfig _config = Service.Config.Get<ActionTweaksConfig>();
    private readonly HookAddress<ActionManager.Delegates.GetAdjustedCastTime> _hook;
    private readonly ConfigListener<ActionTweaksConfig> _listener;
    private int _gameExecutionDepth; // >0 only while the game's own UseActionLocation is executing
    private ActionID _lastReducedAction;
    private float _lastReductionSeconds;

    public CastTimeReductionTweak()
    {
        _hook = new(ActionManager.Addresses.GetAdjustedCastTime, GetAdjustedCastTimeDetour, false);
        _listener = Service.Config.GetAndSubscribe<ActionTweaksConfig>(_ => _hook.Enabled = ReductionMS > 0);
    }

    public void Dispose()
    {
        _listener.Dispose();
        _hook.Dispose();
    }

    // configured reduction in milliseconds, 0 when the feature is off; clamped here so that a hand-edited config can't exceed the slidecast window
    public int ReductionMS => _config.ReduceLongCastTime ? Math.Clamp(_config.LongCastTimeReductionMS, 0, MaxReductionMS) : 0;

    // called by ActionManagerEx around the original UseActionLocation - the only path where the reduction is allowed to take effect
    public void EnterGameExecution() => ++_gameExecutionDepth;
    public void LeaveGameExecution() => --_gameExecutionDepth;

    // how much the currently running cast of a given action was shortened by, in seconds (0 if the tweak is off or did not apply to it)
    // used to correct the slidecast window: the client's total is now 'reduction' shorter than what the server used, so the window shrinks by the same amount
    // note the ReductionMS check: the record survives the tweak being switched off mid-session, and without it a later cast of the same action would
    // still be told it was shortened and would get a slidecast window that is too short by exactly the old reduction
    public float ReductionSeconds(ActionID action) => ReductionMS > 0 && action && action == _lastReducedAction ? _lastReductionSeconds : 0;

    // fail-closed 約定（見 Util/DetourGuard.cs）：自訂邏輯進 try、Original 留在 try 外（它本來就在最前面）。
    // 受管理例外來源是 ActionManager.GetAdjustedRecastTime —— CS 的 [MemberFunction]，特徵碼失效時走
    // ThrowHelper.ThrowNullAddress 擲 InvalidOperationException（不是回 0）。這支 hook 是全域的（遊戲 UI、BossMod 每幀、其他外掛都會呼叫，
    // 見檔頭 Scoping），受管理例外逸出的影響面比其他 detour 廣。outOptProc 是裸指標，從不解參考、原封不動轉交 Original。
    private int GetAdjustedCastTimeDetour(CSActionType actionType, uint actionId, bool applyProcs, ActionManager.CastTimeProc* outOptProc)
    {
        var castTimeMS = _hook.Original(actionType, actionId, applyProcs, outOptProc);
        // 純欄位讀取，不會擲例外；留在 try 外，讓「其他消費端一律拿到未改動的值」這件事在語法上就看得出來
        if (_gameExecutionDepth <= 0)
            return castTimeMS; // not the game's action execution path - every other consumer sees the unmodified value

        try
        {
            // past this point we know the game is setting up a cast for this exact action, so this is also the moment the previous cast's record stops
            // being true: every path that does not reduce has to clear it, otherwise an unreduced cast of a previously reduced action would keep
            // reporting a stale reduction to ReductionSeconds and shrink its slidecast window for no reason
            var reduction = ReductionMS;
            // second condition: recast (GCD) is the bottleneck, not the cast - shortening would gain nothing and could outrun the cooldown
            if (reduction <= 0 || castTimeMS - reduction < MinResultingCastMS || ActionManager.GetAdjustedRecastTime(actionType, actionId) > castTimeMS)
            {
                _lastReducedAction = default;
                return castTimeMS;
            }

            _lastReducedAction = new((ActionType)actionType, actionId);
            _lastReductionSeconds = reduction * 0.001f;
            return castTimeMS - reduction;
        }
        catch (Exception ex)
        {
            // 退化行為：這一次詠唱不縮短，遊戲拿到未改動的詠唱時間（＝「縮短長詠唱」關掉時的行為）。
            // _lastReducedAction 一定要清掉 —— 這是上面那段註解講的不變量：**沒有縮短的路徑都必須清**，
            // 否則 SlidecastMarkerTweak / CalculateDesiredOrientation 會拿舊的縮短量去算，
            // 把滑步窗口縮短成一個不存在的值（而且畫面上看不出來）。
            _lastReducedAction = default;
            DetourGuard.Report(nameof(GetAdjustedCastTimeDetour), ex);
            return castTimeMS;
        }
    }
}
