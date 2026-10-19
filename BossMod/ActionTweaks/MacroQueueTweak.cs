using FFXIVClientStructs.FFXIV.Client.Game;

namespace BossMod;

// Let actions used from macros participate in the native action queue (ported from DailyRoutines' MacroIntoActionQueue).
// ActionManager::UseAction refuses to queue only when mode == Macro. We rewrite Macro (and only Macro) to None rather than Queue: that is
// exactly "behave like a hotbar button press" - queueing allowed, existing queue entry still respected (Queue would overwrite it).
// ActionManager::Update re-issues the queued action with a hardcoded mode and only reads QueueType to check for Combo, so None and Queue
// are indistinguishable afterwards. Nothing is sent to the server early or extra.
public sealed class MacroQueueTweak
{
    private readonly ActionTweaksConfig _config = Service.Config.Get<ActionTweaksConfig>();

    // note: this is applied only to the mode that is forwarded to the game, never to the value our own logic (manual queue etc) looks at
    public ActionManager.UseActionMode TransformMode(ActionManager.UseActionMode mode)
        => _config.QueueMacroActions && mode == ActionManager.UseActionMode.Macro ? ActionManager.UseActionMode.None : mode;
}
