using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.System.Framework;
using CSActionType = FFXIVClientStructs.FFXIV.Client.Game.ActionType;

namespace BossMod;

// Custom native action queue window (ported from DailyRoutines' CustomActionQueueTime).
//
// The game accepts an action into its native queue when the remaining cooldown is at most 0.5s. That decision is made
// entirely inside ActionManager::IsActionOffCooldown, which is called from exactly two places (verified by xref scan of
// TC 7.20): the queueing block of UseAction and the dequeue block of ActionManager::Update. Its logic is:
//   detail = GetRecastGroupDetail(GetRecastGroup(type, id)); if (detail == null) return true;
//   add = GetRecastGroupDetail(GetAdditionalRecastGroup(type, id));
//   if (add != null && add->IsActive && add->Total - add->Elapsed > 0.5f) return false;
//   if (!detail->IsActive) return true;
//   charges = GetMaxCharges(GetSpellIdForAction(type, id), 100);
//   total = usesCharges(detail->ActionId) ? detail->Total / charges : detail->Total;
//   return total - detail->Elapsed <= 0.5f;
// The 0.5f it compares against lives in .rdata and is a shared generic constant with thousands of referents, so it cannot
// be patched - the window can only be changed by changing what the function sees.
//
// Difference from DailyRoutines: it reimplemented the whole function and never called the original, which drifts silently on
// every game patch. Its copy already diverges twice from TC 7.20 - the game returns *true* when the main recast detail is
// null (DR returns false), and the game's "if (!detail->IsActive) return true" early-out is missing entirely - plus it
// divides by GetMaxCharges without guarding against zero.
// Instead we keep the game's own implementation and shift what it measures: adding (window - 0.5) to Elapsed on the recast
// details it is about to read turns each of its two comparisons into an exact test against the desired window
//   (total - (elapsed + d) <= 0.5) <=> (total - elapsed <= 0.5 + d)
// and the same holds for the charge-divided branch, since only Elapsed is shifted. The original values are restored verbatim
// immediately after the original returns; nothing else runs in between (it is a leaf call into native code on the main thread).
public sealed unsafe class ActionQueueWindowTweak : IDisposable
{
    public const float GameWindow = 0.5f; // the game's built-in queue window, in seconds
    public const float MinWindow = 0.3f;
    public const float MaxWindow = 0.8f;

    private readonly ActionTweaksConfig _config = Service.Config.Get<ActionTweaksConfig>();
    private readonly HookAddress<ActionManager.Delegates.IsActionOffCooldown> _hook;
    private readonly ConfigListener<ActionTweaksConfig> _listener;

    public ActionQueueWindowTweak()
    {
        _hook = new(ActionManager.Addresses.IsActionOffCooldown, IsActionOffCooldownDetour, false);
        _listener = Service.Config.GetAndSubscribe<ActionTweaksConfig>(cfg => _hook.Enabled = cfg.CustomActionQueueWindow);
    }

    public void Dispose()
    {
        _listener.Dispose();
        _hook.Dispose();
    }

    public float DesiredWindow
    {
        get
        {
            if (!_config.CustomActionQueueWindow)
                return GameWindow;
            if (!_config.ActionQueueWindowFromFramerate)
                return Math.Clamp(_config.ActionQueueWindow, MinWindow, MaxWindow);
            var fwk = Framework.Instance();
            var dt = fwk != null ? fwk->RealFrameDeltaTime : 0;
            var fps = dt > 0 ? 1f / dt : 90f;
            return Math.Clamp(GameWindow + (90f - fps) * 0.004f, MinWindow, MaxWindow); // 20ms per 5fps below 90, same shape as the original module
        }
    }

    // fail-closed 約定（見 Util/DetourGuard.cs）：自訂邏輯進 try、**Original 一律留在 try 外**。
    // 📌 下半段包住 Original 的那個 try 是 try/**finally**、沒有 catch —— 它一個例外都不吞，存在的理由是保證
    //    我們暫時位移過的 Elapsed 一定會被還原（不還原＝把玩家的冷卻永久偏移 delta 秒）。
    //    稽核把它標成 ORIG_IN_TRY 是誤判，刻意不動它。
    // 受管理例外來源在**上半段**：GetRecastGroup / GetAdditionalRecastGroup / GetRecastGroupDetail 三支都是
    // CS 的 [MemberFunction]，特徵碼失效時走 ThrowHelper.ThrowNullAddress 擲 InvalidOperationException
    // （不是回 null）。台服是特徵碼漂移最容易發生的地方，而這支 detour 掛在按鍵路徑上，逸出就是整個遊戲沒了。
    // 🔴 main/additional 是裸指標，一律判空、不靠 try（AVE 攔不到）—— 原本就是這樣寫的，維持不變。
    private bool IsActionOffCooldownDetour(ActionManager* self, CSActionType actionType, uint actionId)
    {
        // 只有「真的被我們改過」的指標才會進還原名單：位移與記錄成對寫在一起，所以 finally 絕不會拿
        // 一個沒讀成功的 0 去覆蓋玩家真正的 Elapsed。
        RecastDetail* restoreMain = null, restoreAdditional = null;
        float mainElapsed = 0, additionalElapsed = 0;
        try
        {
            var delta = DesiredWindow - GameWindow;
            if (self != null && delta != 0)
            {
                // resolve exactly the same two recast details the original is about to read; GetRecastGroupDetail returns null for negative/unknown groups
                var main = self->GetRecastGroupDetail(self->GetRecastGroup((int)actionType, actionId));
                var additional = self->GetRecastGroupDetail(self->GetAdditionalRecastGroup(actionType, actionId));
                if (additional == main)
                    additional = null;
                // 註：原本是「兩個 Elapsed 都先讀完，才開始寫」，這裡改成逐一「讀→寫」。上面那行保證
                // main != additional，兩個 RecastDetail 是陣列裡不同的元素、不重疊，所以交錯順序完全等價。
                if (main != null)
                {
                    mainElapsed = main->Elapsed;
                    main->Elapsed = mainElapsed + delta;
                    restoreMain = main;
                }
                if (additional != null)
                {
                    additionalElapsed = additional->Elapsed;
                    additional->Elapsed = additionalElapsed + delta;
                    restoreAdditional = additional;
                }
            }
        }
        catch (Exception ex)
        {
            // 退化行為：這一次查詢用遊戲原本的 0.5 秒佇列窗口（＝「自訂技能佇列窗口」關掉時的行為）。
            // 已經位移過的部分仍然在還原名單裡，下面的 finally 照樣會還原，不會留下偏移的冷卻。
            DetourGuard.Report(nameof(IsActionOffCooldownDetour), ex);
        }

        try
        {
            return _hook.Original(self, actionType, actionId);
        }
        finally
        {
            if (restoreMain != null)
                restoreMain->Elapsed = mainElapsed;
            if (restoreAdditional != null)
                restoreAdditional->Elapsed = additionalElapsed;
        }
    }
}
