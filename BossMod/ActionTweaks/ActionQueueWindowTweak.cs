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

    private bool IsActionOffCooldownDetour(ActionManager* self, CSActionType actionType, uint actionId)
    {
        var delta = DesiredWindow - GameWindow;
        if (self == null || delta == 0)
            return _hook.Original(self, actionType, actionId);

        // resolve exactly the same two recast details the original is about to read; GetRecastGroupDetail returns null for negative/unknown groups
        var main = self->GetRecastGroupDetail(self->GetRecastGroup((int)actionType, actionId));
        var additional = self->GetRecastGroupDetail(self->GetAdditionalRecastGroup(actionType, actionId));
        if (additional == main)
            additional = null;
        var mainElapsed = main != null ? main->Elapsed : 0;
        var additionalElapsed = additional != null ? additional->Elapsed : 0;
        if (main != null)
            main->Elapsed = mainElapsed + delta;
        if (additional != null)
            additional->Elapsed = additionalElapsed + delta;
        try
        {
            return _hook.Original(self, actionType, actionId);
        }
        finally
        {
            if (main != null)
                main->Elapsed = mainElapsed;
            if (additional != null)
                additional->Elapsed = additionalElapsed;
        }
    }
}
