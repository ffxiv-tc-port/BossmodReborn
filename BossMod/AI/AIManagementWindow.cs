using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using System.Globalization;

namespace BossMod.AI;

sealed class AIManagementWindow : UIWindow
{
    private static readonly AIConfig _config = Service.Config.Get<AIConfig>();
    private readonly AIManager _manager;
    private readonly EventSubscriptions _subscriptions;
    private const string _title = $"AI: off{_windowID}";
    private const string _windowID = "###AI debug window";
    private static readonly string[] positionals = Enum.GetNames<Positional>();

    public AIManagementWindow(AIManager manager) : base(_windowID, false, new(100f, 100f))
    {
        WindowName = _title;
        _manager = manager;
        _subscriptions = new
        (
            _config.Modified.ExecuteAndSubscribe(() => IsOpen = _config.DrawUI)
        );
        RespectCloseHotkey = false;
    }

    protected override void Dispose(bool disposing)
    {
        _subscriptions.Dispose();
        base.Dispose(disposing);
    }

    public void SetVisible(bool vis)
    {
        if (_config.DrawUI != vis)
        {
            _config.DrawUI = vis;
            _config.Modified.Fire();
        }
    }

    public override void Draw()
    {
        var configModified = false;

        ImGui.TextUnformatted($"Navi={_manager.Controller.NaviTargetPos}");

        configModified |= ImGui.Checkbox(Loc.T("AI_ForbidActions", "Forbid actions"), ref _config.ForbidActions);
        ImGui.SameLine();
        configModified |= ImGui.Checkbox(Loc.T("AI_ForbidMovement", "Forbid movement"), ref _config.ForbidMovement);
        ImGui.SameLine();
        configModified |= ImGui.Checkbox(Loc.T("AI_IdleMounted", "Idle while mounted"), ref _config.ForbidAIMovementMounted);
        ImGui.SameLine();
        configModified |= ImGui.Checkbox(Loc.T("AI_FollowCombat", "Follow during combat"), ref _config.FollowDuringCombat);
        if (ImGui.IsItemHovered())
        {
            ImGui.BeginTooltip();
            ImGui.Text(Loc.T("AI_FollowCombat_Tip", "Must be enabled for follow target."));
            ImGui.EndTooltip();
        }
        ImGui.Spacing();
        configModified |= ImGui.Checkbox(Loc.T("AI_FollowBossModule", "Follow during active boss module"), ref _config.FollowDuringActiveBossModule);
        if (ImGui.IsItemHovered())
        {
            ImGui.BeginTooltip();
            ImGui.Text(Loc.T("AI_FollowBossModule_Tip", "Must be enabled for following targets during active boss modules."));
            ImGui.EndTooltip();
        }
        ImGui.SameLine();
        configModified |= ImGui.Checkbox(Loc.T("AI_FollowOutOfCombat", "Follow out of combat"), ref _config.FollowOutOfCombat);
        ImGui.SameLine();
        configModified |= ImGui.Checkbox(Loc.T("AI_FollowTarget", "Follow target"), ref _config.FollowTarget);
        if (ImGui.IsItemHovered())
        {
            ImGui.BeginTooltip();
            ImGui.Text(Loc.T("AI_FollowTarget_Tip", "Follow the target with your distance settings.\nFollow during combat and follow during active boss module need to be activated."));
            ImGui.EndTooltip();
        }
        ImGui.Spacing();
        configModified |= ImGui.Checkbox(Loc.T("AI_ManualTarget", "Manual targeting"), ref _config.ManualTarget);
        if (ImGui.IsItemHovered())
        {
            ImGui.BeginTooltip();
            ImGui.Text(Loc.T("AI_ManualTarget_Tip", "Allows manual targeting with an active AI autorotation."));
            ImGui.EndTooltip();
        }
        ImGui.SameLine();
        configModified |= ImGui.Checkbox(Loc.T("AI_DisableObstacleMaps", "Disable loading obstacle maps"), ref _config.DisableObstacleMaps);
        ImGui.Text(Loc.T("AI_ActivationTimeCushion", "Dodge timing safety cushion"));
        ImGui.SameLine();
        ImGui.SetNextItemWidth(100f);
        var activationTimeCushionStr = _config.ActivationTimeCushion.ToString(CultureInfo.InvariantCulture);
        if (ImGui.InputText("##ActivationTimeCushion", ref activationTimeCushionStr, 64))
        {
            activationTimeCushionStr = activationTimeCushionStr.Replace(',', '.');
            if (float.TryParse(activationTimeCushionStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var cushion))
            {
                _config.ActivationTimeCushion = cushion;
                configModified = true;
            }
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.BeginTooltip();
            ImGui.Text(Loc.T("AI_ActivationTimeCushion_Tip", "How many seconds before a mechanic actually resolves the AI still treats it as \"safe to be in\".\nLower = dodges later/closer to the last safe moment (more uptime, less margin for lag/hitching). Default is 1s; try 0.1-0.2 for aggressive uptime."));
            ImGui.EndTooltip();
        }
        ImGui.SameLine();
        configModified |= ImGui.Checkbox(Loc.T("AI_ReturnToPreDodgePosition", "Return to pre-dodge position"), ref _config.ReturnToPreDodgePosition);
        if (ImGui.IsItemHovered())
        {
            ImGui.BeginTooltip();
            ImGui.Text(Loc.T("AI_ReturnToPreDodgePosition_Tip", "After a forced dodge is over, try to walk back to the spot you were standing at right before it started, for better uptime/positioning.\nAbandoned immediately if that spot is currently inside a forbidden zone or outside the arena bounds."));
            ImGui.EndTooltip();
        }
        ImGui.SameLine();
        ImGui.Text(Loc.T("AI_ReturnToPreDodgePositionTimeout", "Timeout"));
        ImGui.SameLine();
        ImGui.SetNextItemWidth(80f);
        var preDodgeTimeoutStr = _config.ReturnToPreDodgePositionTimeout.ToString(CultureInfo.InvariantCulture);
        if (ImGui.InputText("##ReturnToPreDodgePositionTimeout", ref preDodgeTimeoutStr, 64))
        {
            preDodgeTimeoutStr = preDodgeTimeoutStr.Replace(',', '.');
            if (float.TryParse(preDodgeTimeoutStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var timeout))
            {
                _config.ReturnToPreDodgePositionTimeout = timeout;
                configModified = true;
            }
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.BeginTooltip();
            ImGui.Text(Loc.T("AI_ReturnToPreDodgePositionTimeout_Tip", "How long to keep trying to walk back to the pre-dodge position before giving up and letting normal positioning take over."));
            ImGui.EndTooltip();
        }
        ImGui.Text(Loc.T("AI_MovementUrgencyThreshold", "Movement urgency threshold"));
        ImGui.SameLine();
        ImGui.SetNextItemWidth(80f);
        var movementUrgencyStr = _config.MovementUrgencyThreshold.ToString(CultureInfo.InvariantCulture);
        if (ImGui.InputText("##MovementUrgencyThreshold", ref movementUrgencyStr, 64))
        {
            movementUrgencyStr = movementUrgencyStr.Replace(',', '.');
            if (float.TryParse(movementUrgencyStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var urgency))
            {
                _config.MovementUrgencyThreshold = urgency;
                configModified = true;
            }
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.BeginTooltip();
            ImGui.Text(Loc.T("AI_MovementUrgencyThreshold_Tip", "The pathfinder often finds a marginally \"safer\" spot the instant a new AOE telegraph appears, even though there's no need to move yet.\nSetting this above 0 keeps the AI standing still (for uptime) until fewer than this many seconds of safety margin remain, instead of relocating right away.\nDoes not delay movement that's actually needed to stay in range of your target/master. Default 0 = old behavior (move immediately)."));
            ImGui.EndTooltip();
        }

        ImGui.Text(Loc.T("AI_FollowSlot", "Follow party slot"));
        ImGui.SameLine();
        ImGui.SetNextItemWidth(250);
        ImGui.SetNextWindowSizeConstraints(default, new Vector2(float.MaxValue, ImGui.GetTextLineHeightWithSpacing() * 50f));
        if (ImRaii.Combo("##Leader", _manager.Beh == null ? Loc.T("AI_Idle", "<idle>") : _manager.WorldState.Party[_manager.MasterSlot]?.Name ?? Loc.T("AI_Unknown", "<unknown>")))
        {
            if (ImGui.Selectable(Loc.T("AI_Idle", "<idle>"), _manager.Beh == null))
                _manager.SwitchToIdle();
            foreach (var (i, p) in _manager.WorldState.Party.WithSlot(true))
            {
                if (ImGui.Selectable(p.Name, _manager.MasterSlot == i))
                {
                    _manager.SwitchToFollow(i);
                    _config.FollowSlot = i;
                    configModified = true;
                }
            }
            ImGui.EndCombo();
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.BeginTooltip();
            ImGui.Text(Loc.T("AI_FollowSlot_Tip", "Select player to follow to enable AI. Usually you select yourself for this."));
            ImGui.EndTooltip();
        }
        ImGui.Separator();
        ImGui.Text(Loc.T("AI_DesiredPositional", "Desired positional"));
        ImGui.SameLine();
        ImGui.SetNextItemWidth(100f);
        var positionalIndex = (int)_config.DesiredPositional;
        if (ImGui.Combo("##DesiredPositional", ref positionalIndex, positionals, 4))
        {
            _config.DesiredPositional = (Positional)positionalIndex;
            configModified = true;
        }
        ImGui.SameLine();
        ImGui.Text(Loc.T("AI_MaxDistTarget", "Max distance - to targets"));
        ImGui.SameLine();
        ImGui.SetNextItemWidth(100);
        var maxDistanceTargetStr = _config.MaxDistanceToTarget.ToString(CultureInfo.InvariantCulture);
        if (ImGui.InputText("##MaxDistanceToTarget", ref maxDistanceTargetStr, 64))
        {
            maxDistanceTargetStr = maxDistanceTargetStr.Replace(',', '.');
            if (float.TryParse(maxDistanceTargetStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var maxDistance))
            {
                _config.MaxDistanceToTarget = maxDistance;
                configModified = true;
            }
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.BeginTooltip();
            ImGui.Text(Loc.T("AI_MaxDistTarget_Tip", "Maximum distance in yalms to keep away from targets."));
            ImGui.EndTooltip();
        }
        ImGui.SameLine();
        ImGui.Text(Loc.T("AI_ToSlots", "- to slots"));
        ImGui.SameLine();
        ImGui.SetNextItemWidth(100f);
        var maxDistanceSlotStr = _config.MaxDistanceToSlot.ToString(CultureInfo.InvariantCulture);
        if (ImGui.InputText("##MaxDistanceToSlot", ref maxDistanceSlotStr, 64))
        {
            maxDistanceSlotStr = maxDistanceSlotStr.Replace(',', '.');
            if (float.TryParse(maxDistanceSlotStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var maxDistance))
            {
                _config.MaxDistanceToSlot = maxDistance;
                configModified = true;
            }
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.BeginTooltip();
            ImGui.Text(Loc.T("AI_MaxDistSlot_Tip", "Maximum distance in yalms to keep away from followed allies."));
            ImGui.EndTooltip();
        }
        ImGui.Text(Loc.T("AI_MinDistance", "Minimum distance"));
        ImGui.SameLine();
        ImGui.SetNextItemWidth(100f);
        var minDistanceStr = _config.MinDistance.ToString(CultureInfo.InvariantCulture);
        if (ImGui.InputText("##MinDistance", ref minDistanceStr, 64))
        {
            minDistanceStr = minDistanceStr.Replace(',', '.');
            if (float.TryParse(minDistanceStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var minDistance))
            {
                _config.MinDistance = minDistance;
                configModified = true;
            }
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.BeginTooltip();
            ImGui.Text(Loc.T("AI_MinDistance_Tip", "Distance in yalms to keep away from target hitbox."));
            ImGui.EndTooltip();
        }
        ImGui.SameLine();
        ImGui.Text(Loc.T("AI_PrefDistForbidden", "Pref distance to forbidden zones"));
        ImGui.SameLine();
        ImGui.SetNextItemWidth(100f);
        var prefDistanceStr = _config.PreferredDistance.ToString(CultureInfo.InvariantCulture);
        if (ImGui.InputText("##PrefDistance", ref prefDistanceStr, 64))
        {
            prefDistanceStr = prefDistanceStr.Replace(',', '.');
            if (float.TryParse(prefDistanceStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var prefDistance))
            {
                _config.PreferredDistance = prefDistance;
                configModified = true;
            }
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.BeginTooltip();
            ImGui.Text(Loc.T("AI_PrefDistForbidden_Tip", "Distance in yalms to keep away from forbidden zones."));
            ImGui.EndTooltip();
        }
        ImGui.Text(Loc.T("AI_MoveDelay", "Movement decision delay"));
        ImGui.SameLine();
        ImGui.SetNextItemWidth(100f);
        var movementDelayStr = _config.MoveDelay.ToString(CultureInfo.InvariantCulture);
        if (ImGui.InputText("##MovementDelay", ref movementDelayStr, 64))
        {
            movementDelayStr = movementDelayStr.Replace(',', '.');
            if (float.TryParse(movementDelayStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var delay))
            {
                _config.MoveDelay = delay;
                configModified = true;
            }
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.BeginTooltip();
            ImGui.Text(Loc.T("AI_MoveDelay_Tip", "Minimum time to start moving after movement decision has been made.\nAvoid setting this too high depending on the content."));
            ImGui.EndTooltip();
        }
        ImGui.SameLine();
        ImGui.Text(Loc.T("AI_AIPreset", "Autorotation AI preset"));
        ImGui.SameLine();
        ImGui.SetNextItemWidth(250f);
        ImGui.SetNextWindowSizeConstraints(default, new Vector2(float.MaxValue, ImGui.GetTextLineHeightWithSpacing() * 50f));
        var aipreset = _config.AIAutorotPresetName;
        var presets = _manager.Autorot.Database.Presets.AllPresets;

        var count = presets.Count;
        List<string> presetNames = new(count + 1);
        for (var i = 0; i < count; ++i)
        {
            presetNames.Add(presets[i].Name);
        }

        if (aipreset != null)
            presetNames.Add(Loc.T("AI_Deactivate", "Deactivate"));
        var countnames = presetNames.Count;
        var selectedIndex = presetNames.IndexOf(aipreset ?? "");

        if (ImGui.Combo("##AI preset", ref selectedIndex, [.. presetNames], countnames))
        {
            if (selectedIndex == countnames - 1 && aipreset != null)
            {
                _manager.SetAIPreset(null);
                configModified = true;
                selectedIndex = -1;
            }
            else if (selectedIndex >= 0 && selectedIndex < count)
            {
                var selectedPreset = presets[selectedIndex];
                _manager.SetAIPreset(selectedPreset);
                configModified = true;
            }
        }
        if (configModified)
            _config.Modified.Fire();
    }

    public override void OnClose() => SetVisible(false);

    public void UpdateTitle()
    {
        var masterSlot = _manager?.MasterSlot ?? -1;
        var masterName = _manager?.Autorot?.WorldState?.Party[masterSlot]?.Name ?? "unknown";
        var masterSlotNumber = masterSlot != -1 ? (masterSlot + 1).ToString() : "N/A";

        WindowName = $"AI: {(_manager?.Beh != null ? "on" : "off")}, master={masterName}[{masterSlotNumber}]{_windowID}";
    }
}
