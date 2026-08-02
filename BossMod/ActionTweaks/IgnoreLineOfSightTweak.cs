using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Object;

namespace BossMod;

// Ignore the client-side line-of-sight rejection when executing an action (ported from DailyRoutines' IgnoreActionTargetBlocked).
//
// What the game does (verified by disassembly of TC 7.20, addresses below are from that build):
// * ActionManager::GetActionInRangeOrLoS (0x140894420, resolved from the CS signature "E8 ?? ?? ?? ?? 85 C0 75 02 33 C0", which matches
//   exactly once in .text) returns 0 when the action may be used against the target, or a LogMessage row id when it may not:
//     562 "看不到目標。"     - written at 0x1408947D9, right after the BGCollision raycast at 0x1405C0200 reports something in the way
//     565 "目標在範圍之外。" - written at 0x140894669 (cone/arc check)
//     566 "目標在射程之外。" - written at 0x14089484F (distance check)
//   The LoS branch is entered only when bit 0x08 of byte +0x3C of the action's Excel row is set (0x140894673), so actions the game never
//   LoS-checks in the first place are untouched no matter what this tweak does.
// * The function has exactly two xrefs in the whole executable: 0x1408989CA inside ActionManager::UseActionLocation, and 0x140894804,
//   which is its own tail recursion for action 7415. So it is neither an inlined orphan nor a shared helper - it is the single gate.
// * That call site is:
//     call GetActionInRangeOrLoS ; mov [rbp-0x71], eax ; test eax, eax
//     je  <continue>             ; 0 -> the action is executed and sent
//     mov ecx, eax ; jmp <error> ; non-zero -> print LogMessage[eax] and return false without sending anything
//   Returning 0 for the LoS code only, and passing 565/566 through untouched, therefore removes the line-of-sight restriction and nothing
//   else - range and arc still reject exactly as before.
// * ActionManager::GetActionStatus never produces 562 (no 0x232 immediate exists anywhere inside it; every occurrence in .text was
//   enumerated). That is why hotbar icons never grey out for line of sight, and why no other function needs to be touched.
//
// Deliberately not covered: the second 562 at 0x140898ACC inside UseActionLocation. It is a different destination check (0x1408A7AA0),
// reached only for actions 7419/24403/29551/41507 - i.e. dashes and backsteps refusing to path through geometry. That is a separate
// feature (DailyRoutines splits it off into LimitlessTargetDashAction) and is left alone.
//
// Difference from DailyRoutines: it overwrote a single conditional-jump byte with 0xEB (MemoryPatch(sig, [0xEB]) applied from a static
// ctor). A byte patch has no notion of "call the original": if the signature ever resolves to a different site it silently rewrites
// unrelated code, and there is no way to tell whether the game logic it jumped over mattered. Here the original always runs and only its
// return value is rewritten, so no game logic is skipped; and if the signature drifts, HookAddress logs it and installs nothing, which
// degrades to "the feature does not work" instead of "the game is corrupted".
//
// Safety: the detour never dereferences sourceObject/targetObject - both are forwarded to the original verbatim and only the returned
// uint is inspected. It stores no native pointer, allocates nothing and runs no per-frame code; it executes only inside the game's own
// UseActionLocation, on the main thread.
//
// Blast radius on other callers (a hook is global, so this matters): the function is also called from C# by other plugins in the fleet -
// Avarice and WrathCombo both do `GetActionInRangeOrLoS(...) is 566`, i.e. they test for the *range* code. Since only 562 is rewritten,
// what they observe is unchanged. This is why the detour matches one exact value instead of collapsing every non-zero result to 0.
//
// Not wired on purpose: ActionManagerEx.CheckActionLoS (a C# reimplementation of the same raycast, gated on the Action sheet's
// RequiresLineOfSight column - the managed counterpart of the +0x3C bit above) is used solely to decide whether to block movement before a
// cast starts, and it deliberately does *not* block when the target is out of LoS so that AI cannot get stuck behind a wall forever. Making
// it agree with this tweak would reintroduce exactly that hang whenever the request does not actually go through, in exchange for one frame
// of movement blocking; ActionManagerEx already sets MoveMightInterruptCast authoritatively once the cast really starts.
//
// Caveat (stated in the tooltip as well): this removes the *client's* refusal to send the request. Whether the server performs its own
// line-of-sight validation could not be determined from the client binary. If it does, the action simply fails the way any rejected
// action does - an error message and no effect - which is why this ships default off.
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

    private uint GetActionInRangeOrLoSDetour(uint actionId, GameObject* sourceObject, GameObject* targetObject)
    {
        // always let the game do the whole check, then reinterpret only its verdict; the config is re-read here because a detour can still
        // be in flight for one call after the hook is disabled
        var result = _hook.Original(actionId, sourceObject, targetObject);
        return result == TargetNotInLineOfSight && _config.IgnoreLineOfSight ? 0 : result;
    }
}
