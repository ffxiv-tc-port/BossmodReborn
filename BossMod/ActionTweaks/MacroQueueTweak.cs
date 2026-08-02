using FFXIVClientStructs.FFXIV.Client.Game;

namespace BossMod;

// Let actions used from macros participate in the native action queue (ported from DailyRoutines' MacroIntoActionQueue).
//
// Disassembly of ActionManager::UseAction (TC 7.20) reads the `mode` argument in exactly three places:
//   cmp [rsp+2A8], 1 ; je  -> when mode == Queue, skip the "ActionQueued != 0 -> return false" early-out
//   cmp [rsp+2A8], 2 ; je  -> when mode == Macro, jump over the whole queueing block
//   mov eax, [rsp+2A8] ; mov [rbp+84], eax -> QueueType = mode
// So Macro is the only mode that refuses to queue - the module's description is accurate. Note that the CS comment on
// UseActionMode.Queue ("will ignore queue") is about that first check only and reads as if it meant the opposite.
//
// Difference from DailyRoutines: it rewrote the mode to Queue for *every* UseAction call, which additionally bypassed the
// "something is already queued -> return false" early-out, so mashing any hotkey would silently overwrite the pending queue
// entry. Here the rewrite happens only when the incoming mode is Macro, and it maps to None rather than Queue, which is
// exactly "behave like a hotbar button press" - queueing allowed, existing queue entry still respected.
// This is safe with respect to how the queued action is later executed: ActionManager::Update re-issues it with a hardcoded
// mode=1 immediate and only reads QueueType to check for Combo(3), so None and Queue are indistinguishable afterwards.
//
// Nothing is sent to the server early or extra - the queued action still goes through the normal UseAction/UseActionLocation
// path when it becomes available. It is equivalent to the user pressing the button once more.
public sealed class MacroQueueTweak
{
    private readonly ActionTweaksConfig _config = Service.Config.Get<ActionTweaksConfig>();

    // note: this is applied only to the mode that is forwarded to the game, never to the value our own logic (manual queue etc) looks at
    public ActionManager.UseActionMode TransformMode(ActionManager.UseActionMode mode)
        => _config.QueueMacroActions && mode == ActionManager.UseActionMode.Macro ? ActionManager.UseActionMode.None : mode;
}
