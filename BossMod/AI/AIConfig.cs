namespace BossMod.AI;

[ConfigDisplay(Name = "AI configuration (AI is very experimental, use at your own risk!)", Order = 7)]
sealed class AIConfig : ConfigNode
{
    [PropertyDisplay("Show status in DTR bar")]
    public bool ShowDTR = false;

    [PropertyDisplay("Show AI interface")]
    public bool DrawUI = false;

    [PropertyDisplay("Focus target master")]
    public bool FocusTargetMaster = false;

    [PropertyDisplay("Broadcast keypresses to other windows", tooltip: "Can cause hitching on some computers. Only enable it if it is actually needed! It is only useful for multiboxers.")]
    public bool BroadcastToSlaves = false;

    [PropertyDisplay("Follow party slot")]
    public int FollowSlot = 0;

    [PropertyDisplay("Forbid actions")]
    public bool ForbidActions = false;

    [PropertyDisplay("Manual targeting")]
    public bool ManualTarget = false;

    [PropertyDisplay("Forbid movement")]
    public bool ForbidMovement = false;

    [PropertyDisplay("Follow during combat")]
    public bool FollowDuringCombat = false;

    [PropertyDisplay("Follow during active boss module")]
    public bool FollowDuringActiveBossModule = false;

    [PropertyDisplay("Follow out of combat")]
    public bool FollowOutOfCombat = false;

    [PropertyDisplay("Follow target")]
    public bool FollowTarget = false;

    [PropertyDisplay("Desired positional when following target")]
    [PropertyCombo(["Any", "Flank", "Rear", "Front"])]
    public Positional DesiredPositional = Positional.Any;

    [PropertyDisplay("Max distance to slot")]
    public float MaxDistanceToSlot = 1f;

    [PropertyDisplay("Max distance to target")]
    public float MaxDistanceToTarget = 2.6f;

    [PropertyDisplay("Minimum distance to hitbox")]
    public float MinDistance = default;

    [PropertyDisplay("Preferred distance to forbidden zones")]
    public float PreferredDistance = default;

    [PropertyDisplay("Extra safety margin in Gold Saucer minigames", tooltip: "Adds extra distance-from-danger buffer on top of \"Preferred distance to forbidden zones\", so the AI stands further into a safe zone when there's room to spare instead of hugging the edge of danger.\nOnly applies in Gold Saucer minigames - never in dungeons/trials/raids, where positioning still needs to stay precise.")]
    public float CasualSafetyMargin = default;

    [PropertyDisplay("Enable auto AFK", tooltip: "Enables auto AFK if out of combat. While AFK AI will not use autorotation or target anything")]
    public bool AutoAFK = false;

    [PropertyDisplay("Auto AFK timer", tooltip: "Time in seconds out of combat until AFK mode enables. Any movement will reset timer or disable AFK mode if already active.")]
    public float AFKModeTimer = 10f;

    [PropertyDisplay("Disable loading obstacle maps", tooltip: "Might be required to be enabled for some some content such as deep dungeons.")]
    public bool DisableObstacleMaps = false;

    [PropertyDisplay("Movement decision delay", tooltip: "Only change this at your own risk and keep this value low! Too high and it won't move in time for some mechanics. Make sure to readjust the value for different content.")]
    public double MoveDelay = default;

    [PropertyDisplay("Idle while mounted")]
    public bool ForbidAIMovementMounted = false;

    [PropertyDisplay("Stay within arena bounds", tooltip: "When following, do not move outside the current arena/pathfind map boundary (useful in boss modules to prevent walking out of the arena)")]
    public bool StayWithinArenaBounds = true;

    [PropertyDisplay("Treat all forbidden zones as immediate", tooltip: "Never move into an AOE zone even if you could pass through before it activates; safer but may reduce uptime")]
    public bool AvoidFutureAOEs = false;

    [PropertyDisplay("Prioritize uptime (aggressive dodge timing)", tooltip: "Keep attacking as long as possible before a mechanic resolves, moving out only at the very last safe moment instead of leaving a 1s safety cushion. Improves uptime, but leaves no margin for lag/hitching - only enable if you trust your connection and this specific fight's pathfinding.")]
    public bool AggressiveUptime = false;

    [PropertyDisplay("Return to pre-dodge position", tooltip: "After a forced dodge is over, try to walk back to the spot you were standing at right before it started, for better uptime/positioning. Abandoned immediately if that spot is currently inside a forbidden zone or outside the arena bounds.")]
    public bool ReturnToPreDodgePosition = false;

    public string? AIAutorotPresetName;
}
