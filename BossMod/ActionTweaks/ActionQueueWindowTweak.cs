using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.System.Framework;
using CSActionType = FFXIVClientStructs.FFXIV.Client.Game.ActionType;

namespace BossMod;

// Custom native action queue window (ported from DailyRoutines' CustomActionQueueTime).
// The game queues an action when ActionManager::IsActionOffCooldown sees at most 0.5s of cooldown left; that 0.5 is a shared .rdata
// constant and cannot be patched. We keep the game's own implementation (a reimplementation drifts silently on every patch) and add
// (window - 0.5) to Elapsed on the two recast details it is about to read, which turns its comparisons into exact tests against the
// desired window. The original values are restored right after the original returns (main thread, leaf call into native code).
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

    // fail-closed 約定（見 Util/DetourGuard.cs）：自訂邏輯進 try、Original 留在 try 外。
    // 下半段包住 Original 的是 try/finally、沒有 catch：只保證位移過的 Elapsed 一定還原（不還原＝把玩家的冷卻永久偏移 delta 秒），刻意不動它。
    // 上半段的 GetRecastGroup / GetAdditionalRecastGroup / GetRecastGroupDetail 是 CS 的 [MemberFunction]，特徵碼失效時擲 InvalidOperationException（不是回 null）。
    // main/additional 是裸指標，一律判空、不靠 try（AVE 攔不到）。
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
                // 逐一「讀→寫」而不是兩個都先讀完才寫：上面那行保證
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
