namespace BossMod;

[ConfigDisplay(Name = "Action tweaks", Order = 4)]
public sealed class ActionTweaksConfig : ConfigNode
{
    // TODO: consider exposing max-delay to config; 0 would mean 'remove all delay', max-value would mean 'disable'
    [PropertyDisplay("Remove extra lag-induced animation lock delay from instant casts (read tooltip!)", tooltip: "Do NOT use with XivAlexander or NoClippy - this should automatically disable itself if they are detected, but double check first!")]
    public bool RemoveAnimationLockDelay = false;

    [PropertyDisplay("Animation lock max. simulated delay (read tooltip!)", tooltip: "Configures the maximum simulated delay in milliseconds when using animation lock removal - this is required and cannot be reduced to zero. Setting this to 20ms will enable triple-weaving when using autorotation. The minimum setting to remove triple-weaving is 26ms. The minimum of 20ms has been accepted by FFLogs and should not cause issues with your logs.")]
    [PropertySlider(20, 50, Speed = 0.1f)]
    public int AnimationLockDelayMax = 20;

    [PropertyDisplay("Remove extra framerate-induced cooldown delay", tooltip: "Dynamically adjusts cooldown and animation locks to ensure queued actions resolve immediately regardless of framerate limitations")]
    public bool RemoveCooldownDelay = false;

    [PropertyDisplay("Shorten long cast times (read tooltip!)", tooltip: "The server resolves a cast about 0.5s before the client's cast bar completes (that is what makes slidecasting possible), so the tail of the bar is pure local idle time. This reclaims part of it by shortening the client's cast timer, letting the next action be requested earlier.\n\nOnly applies when the cast time is longer than the recast time, so it never affects a normal GCD rotation - in practice only BLM Fire IV / Blizzard IV, Teleport/Return, raises and limit breaks. Nothing is sent to the server earlier and no packet is modified.\n\nDefault off; the reduction is hard-capped below the slidecast window.")]
    public bool ReduceLongCastTime = false;

    [PropertyDisplay("Long cast time reduction (ms)", tooltip: "How much to shorten the client's cast timer by, in milliseconds. Capped at 400ms so that it always stays below the ~500ms slidecast window.")]
    [PropertySlider(50, CastTimeReductionTweak.MaxReductionMS, Speed = 1)]
    public int LongCastTimeReductionMS = CastTimeReductionTweak.MaxReductionMS;

    [PropertyDisplay("Show slidecast marker on own cast bar", tooltip: "Highlights the tail of your own cast bar, from the point where moving no longer interrupts the cast up to the end of the bar. It turns green the instant the server's action effect for that cast arrives, which is the real moment you are free to move.\n\nDisplay only - the game's cast bar is never modified, and enemy cast bars are never touched. If 'Shorten long cast times' is enabled the band shrinks accordingly, since that setting reclaims part of the same window.")]
    public bool ShowSlidecastMarker = false;

    [PropertyDisplay("Prevent movement while casting")]
    public bool PreventMovingWhileCasting = false;

    public enum ModifierKey
    {
        [PropertyDisplay("None")]
        None,
        [PropertyDisplay("Control")]
        Ctrl,
        [PropertyDisplay("Alt")]
        Alt,
        [PropertyDisplay("Shift")]
        Shift,
        [PropertyDisplay("LMB + RMB")]
        M12
    }

    [PropertyDisplay("Key to hold to allow movement while casting", tooltip: "Requires the above setting checked as well")]
    public ModifierKey MoveEscapeHatch = ModifierKey.None;

    [PropertyDisplay("Automatically cancel a cast when target is dead")]
    public bool CancelCastOnDeadTarget = false;

    [PropertyDisplay("Prevent movement and action execution when pyretic-like mechanics are imminent (set to 0 to disable, otherwise increase threshold depending on your ping).")]
    [PropertySlider(0, 10, Speed = 0.01f)]
    public float PyreticThreshold = 1.0f;

    [PropertyDisplay("Auto misdirection: prevent movement under misdirection if angle between normal movement and misdirected is greater than this threshold (set to 180 to disable).")]
    [PropertySlider(0, 180)]
    public float MisdirectionThreshold = 180f;

    [PropertyDisplay("Restore character orientation after action use")]
    public bool RestoreRotation = false;

    [PropertyDisplay("Use actions on mouseover target")]
    public bool PreferMouseover = false;

    [PropertyDisplay("Smart ability targeting", tooltip: "If the usual (mouseover/primary) target is not valid for an action, select the next best target automatically (e.g. co-tank for Shirk)")]
    public bool SmartTargets = true;

    [PropertyDisplay("Use custom queueing for manually pressed actions", tooltip: "This setting allows better integration with autorotations and will prevent you from triple-weaving or drifting GCDs if you press a healing ability while autorotation is going on")]
    public bool UseManualQueue = false;

    [PropertyDisplay("Use a custom action queue window", tooltip: "The game accepts an action into its native queue when at most 0.5s of cooldown remains. This lets you change that threshold.\n\nA larger window makes early presses stick instead of being dropped, at the cost of committing to an action further ahead of time; a smaller one does the opposite. This only changes how early the client accepts input - the action is still sent when the cooldown actually expires, so the server sees no difference.")]
    public bool CustomActionQueueWindow = false;

    [PropertyDisplay("Action queue window (seconds)", tooltip: "The game's built-in value is 0.5s.")]
    [PropertySlider(ActionQueueWindowTweak.MinWindow, ActionQueueWindowTweak.MaxWindow, Speed = 0.01f)]
    public float ActionQueueWindow = ActionQueueWindowTweak.GameWindow;

    [PropertyDisplay("Derive the queue window from current framerate instead", tooltip: "Ignores the slider and widens the window as the framerate drops (20ms per 5fps below 90fps), to compensate for input being sampled less often. Note that \"Remove extra framerate-induced cooldown delay\" above addresses the same problem in a more precise way.")]
    public bool ActionQueueWindowFromFramerate = false;

    [PropertyDisplay("Allow actions used from macros to be queued", tooltip: "By default the game refuses to queue anything executed from a macro, so a macro pressed slightly too early is simply dropped. This makes such actions behave like a normal hotbar press instead: they get queued and fire as soon as they become available.\n\nAn already queued action is still not overwritten, and nothing is sent to the server any earlier.")]
    public bool QueueMacroActions = false;

    [PropertyDisplay("Try to prevent dashing into AOEs", tooltip: "Prevent automatic use of targeted dashes (like WAR Onslaught) if they would move you into a dangerous area. May not work as expected in instances that do not have modules.\n\nThis option will also apply to manually pressed dashes if you have \"Use custom queueing for manually pressed actions\" enabled.")]
    public bool DashSafety = true;

    [PropertyDisplay("Apply the previous option to all dashes, not just gap closers", tooltip: "Includes backdashes (e.g. SAM Yaten), teleports (e.g. NIN Shukuchi), and fixed-length dashes (e.g. DRG Elusive Jump)")]
    public bool DashSafetyExtra = true;

    [PropertyDisplay("Automatically manage auto attacks", tooltip: "This setting prevents starting autos early during countdown, starts them automatically at pull, when switching targets and when using any actions that don't explicitly cancel autos.")]
    public bool AutoAutos = false;

    [PropertyDisplay("Automatically dismount to execute actions")]
    public bool AutoDismount = true;

    public enum GroundTargetingMode
    {
        [PropertyDisplay("Manually select position by extra click (normal game behaviour)")]
        Manual,

        [PropertyDisplay("Cast at current mouse position")]
        AtCursor,

        [PropertyDisplay("Cast at selected target's position")]
        AtTarget
    }
    [PropertyDisplay("Automatic target selection for ground-targeted abilities")]
    public GroundTargetingMode GTMode = GroundTargetingMode.Manual;

    public bool ActivateAnticheat = true;
}
