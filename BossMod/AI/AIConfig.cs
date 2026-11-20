namespace BossMod.AI;

[ConfigDisplay(Name = "AI configuration (AI is very experimental, use at your own risk!)", Order = 7)]
sealed class AIConfig : ConfigNode
{
    [PropertyDisplay("Enable AI automatically when the plugin loads", tooltip: "Starts AI in follow mode (using the configured follow slot) as soon as party/world data is available after plugin load, instead of starting idle and requiring a manual /bmrai on or UI toggle every session.")]
    public bool AutoEnableOnLoad = true;

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

    // 🔴 預設 false（opt-in）。BMR 的新設定欄位預設值會直接落到既有使用者身上，
    //    而有人是靠 BMR 的自動循環打一般副本的 —— 預設 true 會讓他們在深牢以外靜默停火。
    [PropertyDisplay("Only use the autorotation preset inside deep dungeons",
        tooltip: "The AI keeps doing movement, dodging and targeting everywhere, but only presses rotation actions while you are inside a deep dungeon.\n\nUse this when another plugin handles your rotation in normal content and you only want BMR to take over in deep dungeons.")]
    public bool AutorotOnlyInDeepDungeon = false;

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

    [PropertyDisplay("Plan dodges for the next AOEs of a chain", tooltip: "When a module announces what comes next (for example a circle followed by a donut a couple of seconds later), prefer safe spots from which the next safe area can still be reached in time. Turn off to only consider AOEs that are already marked dangerous.")]
    public bool DodgeLookahead = true;

    [PropertyDisplay("Dodge timing safety cushion (seconds)", tooltip: "How many seconds before a mechanic actually resolves the AI still treats it as \"safe to be in\" for pathfinding purposes. Lower = dodges later/closer to the last safe moment (more uptime, less margin for lag/hitching). Higher = dodges earlier/more conservatively. Default is 1s; try 0.1-0.2 for aggressive uptime, only if you trust your connection and this fight's pathfinding.")]
    [PropertySlider(0f, 2f)]
    public float ActivationTimeCushion = Pathfinding.NavigationDecision.ActivationTimeCushion;

    [PropertyDisplay("Return to pre-dodge position", tooltip: "After a forced dodge is over, try to walk back to the spot you were standing at right before it started, for better uptime/positioning. Abandoned immediately if that spot is currently inside a forbidden zone or outside the arena bounds.")]
    public bool ReturnToPreDodgePosition = false;

    [PropertyDisplay("Return to pre-dodge position timeout (seconds)", tooltip: "How long to keep trying to walk back to the pre-dodge position before giving up and letting normal positioning take over.")]
    [PropertySlider(0.5f, 15f)]
    public float ReturnToPreDodgePositionTimeout = 4f;

    [PropertyDisplay("Movement urgency threshold (seconds)", tooltip: "The pathfinder often finds a marginally \"safer\" spot to stand at the moment a new AOE telegraph appears, even though there's no need to move yet. This keeps the AI standing still (for uptime) until fewer than this many seconds of safety margin remain, instead of relocating right away. Does not delay movement that's actually needed to stay in range of your target/master.")]
    [PropertySlider(0f, 3f)]
    public float MovementUrgencyThreshold = 0f;

    [PropertyDisplay("Pathfinding reversal debounce", tooltip: "When the pathfinder suddenly wants to go the opposite way (for example around the left side of a danger zone instead of the right), keep going the previous way until the new direction has held for the time below. Switches immediately whenever the previous way, or the path to it, is no longer safe.\nHelps with standing in place while the route flips between two sides. It does not fix running into walls or wobbling on the spot after arriving.")]
    public bool NavigationReversalDebounce = false;

    [PropertyDisplay("Pathfinding reversal debounce: delay (seconds)", tooltip: "How long a route in the opposite direction has to keep winning before the AI switches to it.")]
    [PropertySlider(0.1f, 1f)]
    public float NavigationReversalDelay = 0.3f;

    [PropertyDisplay("Arrival hold (no wobbling on the spot)", tooltip: "Automatic movement only gives the game a direction and the game always walks at full speed, so near the destination a frame can overshoot it and the next frame walks back - the character wobbles on the spot. With this on, once the destination is reached (or is within two frames of walking) the AI stops, and only starts again when the destination moves further away than the distance below.\nNever holds while you are inside or within 0.5 yalms of any danger zone; then it moves exactly as with this off. Applies to the AI and to the \"automatic movement\" module of a preset.")]
    public bool ArrivalHold = false;

    [PropertyDisplay("Arrival hold: distance before moving again (yalms)", tooltip: "After arriving, the destination has to be further away than this before the AI walks again. This is also the most you can end up standing away from the exact spot.")]
    [PropertySlider(0.2f, 1f)]
    public float ArrivalHoldDistance = 0.3f;

    // 預設開是使用者裁的；只發提示、不改移動。
    [PropertyDisplay("Pause key: warn when the danger under you is about to go off", tooltip: "While the \"pause automatic movement\" key (Action tweaks) is held and the AI works out that you have to start moving out of the danger zone under you within the time below, prints one short line in chat. Once per key press: let go and press again to be warned again.\nOnly a warning - nothing about movement changes. Needs the AI to be on.")]
    public bool PauseKeyDangerNotice = true;

    [PropertyDisplay("Pause key: danger threshold (seconds)", tooltip: "How soon you have to start moving out of the danger zone under you, following the AI's own escape route, before the pause key options above react. It is the same deadline the AI uses to decide whether a cast still fits before you have to move.")]
    [PropertySlider(0.2f, 3f)]
    public float PauseKeyDangerSeconds = 1f;

    [PropertyDisplay("Squadron commands: order squadron members onto mechanic targets", tooltip: "Only in Squadron Command Missions, and only for bosses whose module names a target for the squadron (for example adds that explode if left alive). When fewer than two squadron members attack that target for 1.5 seconds, issues the \"Engage\" order on it. Never changes your own target.\nWorks whether or not the AI is enabled. Never issues Disengage, Re-engage or the squadron limit break.")]
    public bool SquadronCommands = true;

    public string? AIAutorotPresetName;

    // 執行期解析用這個：其他插件的覆寫優先；存檔、AI 視窗的下拉選單、/bmrai 看的仍是上面的使用者值。
    public string? AIAutorotPresetNameEffective => TransientConfigOverrides.AIAutorotPresetName is { } o ? o.Value : AIAutorotPresetName;
}
