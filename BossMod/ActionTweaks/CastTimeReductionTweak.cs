using FFXIVClientStructs.FFXIV.Client.Game;
using CSActionType = FFXIVClientStructs.FFXIV.Client.Game.ActionType;

namespace BossMod;

// Long-cast reduction tweak (ported from DailyRoutines' OptimizedLongCastTimeAction).
//
// What the game does (verified by disassembly of TC 7.20):
// * ActionManager::UseActionLocation calls ActionManager::GetAdjustedCastTime once, converts the result (ms) to seconds and stores it into
//   ActionManager.CastTimeTotal (+0x34) and into the character's CastInfo (BaseCastTime +0x38 / TotalCastTime +0x3C).
// * ActionManager::Update, while CastSpellId != 0, only ticks CastTimeElapsed; animation lock does NOT tick down and the action queue is NOT
//   processed at all until CastTimeElapsed >= CastTimeTotal. So CastTimeTotal is exactly 'when may I request the next action'.
// * The action request packet is sent when the cast *starts* and carries no cast time field - the server computes the duration itself and
//   resolves the cast approximately 0.5s before the client's bar completes (this is the slidecast window, see AnimationLockTweak header).
// Therefore shortening the client's cast timer only reclaims local idle time that the server has already released; nothing is sent early and
// nothing lies to the server. The `recast <= cast` guard means it only triggers when the cast (rather than the GCD) is the bottleneck, so it
// can never produce a 'still on cooldown' rejection. In practice this only affects BLM F4/B4, Teleport/Return, raises and limit breaks.
//
// Scoping (this is the whole reason the tweak lives in BossMod rather than in a generic utility plugin):
// GetAdjustedCastTime is a global static, and it has 7 call sites in the game - 1 in UseActionLocation (the execution path we want to change)
// and 6 in cast-bar/hotbar/tooltip UI code. On top of that BossMod itself calls it every frame (movement block prediction) and feeds it to
// ManualActionQueueTweak.Push, and other plugins call it too. Blindly hooking it would silently hand all of them a value that is 400ms short,
// and AnimationLockTweak's sanity check only covers animation lock, not cast time - there would be no safety net.
// So the detour only applies the reduction while _gameExecutionDepth > 0, which ActionManagerEx sets exclusively around the original
// UseActionLocation call. Everything else - BossMod's own wrapper, the game's UI, other plugins - keeps observing the unmodified value.
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

    // fail-closed 約定（見 Util/DetourGuard.cs）：自訂邏輯進 try、**Original 一律留在 try 外**（它本來就在最前面）。
    // 受管理例外來源是 ActionManager.GetAdjustedRecastTime —— CS 的 [MemberFunction]，特徵碼失效時走
    // ThrowHelper.ThrowNullAddress 擲 InvalidOperationException（不是回 0）。
    // ⚠️ 這支 hook 是**全域**的：遊戲自己有 7 個呼叫點、BossMod 每幀會呼叫、其他外掛也會呼叫（見上面的 Scoping 段），
    //    所以受管理例外從這裡逸出的影響面比其他 detour 都廣，不是只有詠唱那一瞬間。
    // 🔴 outOptProc 是裸指標，我們一次都沒有解參考它，原封不動轉交 Original —— 這裡沒有裸指標問題。
    private int GetAdjustedCastTimeDetour(CSActionType actionType, uint actionId, bool applyProcs, ActionManager.CastTimeProc* outOptProc)
    {
        // note: outOptProc is a native pointer that we never dereference - it is passed straight through to the original
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
