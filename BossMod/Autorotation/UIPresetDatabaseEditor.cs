using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace BossMod.Autorotation;

// note: the editor assumes it's the only thing that modifies the database instance; having multiple editors or editing database externally will break things
public sealed class UIPresetDatabaseEditor(RotationDatabase rotationDB)
{
    private readonly PresetDatabase PresetDB = rotationDB.Presets;

    private int _selectedPresetIndex = -1;
    private bool _selectedPresetDefault;
    private int _pendingSelectPresetIndex = -1; // if >= 0, we want to select different preset, but current one has modifications
    private bool _pendingSelectPresetDefault;
    private Type? _selectedModuleType; // we want module selection to be persistent when changing presets
    private UIPresetEditor? _selectedPreset;

    private readonly AutorotationConfig _cfg = Service.Config.Get<AutorotationConfig>();

    private bool HaveUnsavedModifications => _selectedPreset?.Modified ?? false;

    public void Draw()
    {
        if (_pendingSelectPresetIndex >= 0)
            DrawPendingSwitch();
        DrawPresetSelector();
        if (_selectedPreset != null)
        {
            _selectedPreset.Draw();
            _selectedModuleType = _selectedPreset.SelectedModuleType ?? _selectedModuleType;
        }
        else
        {
            ImGui.TextUnformatted(Loc.T("PRESETDB_SelectOrCreate", "Select preset to edit or create a new one."));
        }
    }

    private void DrawPendingSwitch()
    {
        if (_pendingSelectPresetIndex < 0)
            return;
        if (!HaveUnsavedModifications)
        {
            CompleteChangeCurrentPreset();
            return;
        }

        ImGui.OpenPopup("Unsaved modifications"); // TODO: why do i have to do it every frame???
        var modalOpen = true;
        using var modal = ImRaii.PopupModal("Unsaved modifications", ref modalOpen, ImGuiWindowFlags.AlwaysAutoResize);
        if (!modal)
            return;
        ImGui.TextUnformatted(string.Format(Loc.T("PRESETDB_UnsavedTitle", "Currently opened preset {0} has unsaved modifications."), _selectedPreset?.Preset.Name));
        ImGui.TextUnformatted(Loc.T("PRESETDB_UnsavedMsg", "To select a new preset, you need to either save or discard them."));
        ImGui.TextUnformatted(Loc.T("PRESETDB_HowToProceed", "How do you want to proceed?"));
        if (DrawSaveCurrentPresetButton())
        {
            SaveCurrentPreset();
            CompleteChangeCurrentPreset();
        }
        ImGui.SameLine();
        if (UIMisc.Button(Loc.T("PRESETDB_SaveAsCopy", "Save as copy"), _selectedPresetIndex < 0, Loc.T("PRESETDB_CantSaveAsCopy", "Can't save new preset as copy")))
        {
            SaveCurrentPresetAsCopy();
            CompleteChangeCurrentPreset();
        }
        ImGui.SameLine();
        if (ImGui.Button(Loc.T("PRESETDB_Discard", "Discard")))
        {
            CompleteChangeCurrentPreset();
        }
        ImGui.SameLine();
        if (ImGui.Button(Loc.T("PRESETDB_Cancel", "Cancel")) || !modalOpen)
        {
            _pendingSelectPresetIndex = -1;
        }
        if (_pendingSelectPresetIndex < 0)
            ImGui.CloseCurrentPopup();
    }

    private void DrawPresetSelector()
    {
        UIMisc.HelpMarker("""
            To start using autorotation, create a *preset*.
            Preset configures rotation *modules* and their *strategies*.
            Module is a piece of code that evaluates game state and fills prioritized list of candidate actions.
            The autorotation framework selects the highest priority action from the list to execute on next opportunity.
            Each module can be further configured by a set of *strategies*, which customize different aspects of its behaviour.
            For example, you might want to create a 'single target' and 'aoe' presets, which would use the same modules, but would configure their strategies differently.
            You could optionally assign keyboard modifiers to each strategy value; such value would only be applied if modifier is held.
            This allows you, for example, to set up preset so that it delays 2-minute burst if shift is held.
            """);
        ImGui.SameLine();

        ImGui.SetNextItemWidth(200);
        using (var combo = ImRaii.Combo(Loc.T("PRESETDB_Preset", "Preset"), _selectedPreset == null ? "" : _selectedPresetIndex < 0 ? "<new>" : (_selectedPresetDefault ? PresetDB.DefaultPresets : PresetDB.UserPresets)[_selectedPresetIndex].Name))
        {
            if (combo)
            {
                if (!_cfg.HideDefaultPreset)
                    DrawPresetListElements(true);
                DrawPresetListElements(false);
            }
        }

        ImGui.SameLine();
        if (DrawSaveCurrentPresetButton())
            SaveCurrentPreset();
        ImGui.SameLine();
        if (UIMisc.Button(Loc.T("PRESETDB_SaveAsCopy", "Save as copy"), _selectedPresetIndex < 0, Loc.T("PRESETDB_CantSaveAsCopy", "Can't save new preset as copy")))
            SaveCurrentPresetAsCopy();
        ImGui.SameLine();
        if (UIMisc.Button(Loc.T("PRESETDB_Revert", "Revert"), 0, (!HaveUnsavedModifications, Loc.T("PRESETDB_NotModified", "Current preset is not modified")), (_selectedPresetIndex < 0, Loc.T("PRESETDB_NoneSelected", "No preset is selected"))))
            RevertCurrentPreset();
        ImGui.SameLine();
        if (UIMisc.Button(Loc.T("PRESETDB_New", "New"), HaveUnsavedModifications, Loc.T("PRESETDB_ModifiedSaveOrDiscard", "Current preset is modified, save or discard changes")))
            CreateNewPreset(-1, false);
        ImGui.SameLine();
        if (UIMisc.Button(Loc.T("PRESETDB_Copy", "Copy"), 0, (HaveUnsavedModifications, Loc.T("PRESETDB_ModifiedSaveOrDiscard", "Current preset is modified, save or discard changes")), (_selectedPresetIndex < 0, Loc.T("PRESETDB_NoneSelected", "No preset is selected"))))
            CreateNewPreset(_selectedPresetIndex, _selectedPresetDefault);
        ImGui.SameLine();
        if (UIMisc.Button(Loc.T("PRESETDB_Delete", "Delete"), 0, (_selectedPresetDefault, Loc.T("PRESETDB_CantDeleteDefault", "The default preset can't be deleted. If you would like to hide it, you can do so in Settings -> Autorotation.")), (!ImGui.GetIO().KeyShift, Loc.T("PRESETDB_HoldShift", "Hold shift to delete")), (_selectedPresetIndex < 0, Loc.T("PRESETDB_NoneSelected", "No preset is selected"))))
            DeleteCurrentPreset();
        ImGui.SameLine();
        if (UIMisc.Button(Loc.T("PRESETDB_Export", "Export"), _selectedPreset == null, Loc.T("PRESETDB_NoneSelected", "No preset is selected")))
            ExportToClipboard();
        ImGui.SameLine();
        if (UIMisc.Button(Loc.T("PRESETDB_Import", "Import"), HaveUnsavedModifications, Loc.T("PRESETDB_ModifiedSaveOrDiscard", "Current preset is modified, save or discard changes")))
            ImportNewPresetFromClipboard();
    }

    private void DrawPresetListElements(bool defaultPresets)
    {
        var presets = defaultPresets ? PresetDB.DefaultPresets : PresetDB.UserPresets;
        for (int i = 0; i < presets.Count; ++i)
        {
            var preset = presets[i];
            if (ImGui.Selectable(preset.Name, _selectedPresetDefault == defaultPresets && _selectedPresetIndex == i))
            {
                _pendingSelectPresetIndex = i;
                _pendingSelectPresetDefault = defaultPresets;
            }

            if (!defaultPresets && ImGui.IsItemActive() && !ImGui.IsItemHovered())
            {
                var j = ImGui.GetMouseDragDelta().Y < 0 ? i - 1 : i + 1;
                if (j >= 0 && j < presets.Count)
                {
                    (presets[i], presets[j]) = (presets[j], presets[i]);
                    if (_selectedPresetIndex == i && _selectedPresetDefault == defaultPresets)
                        _selectedPresetIndex = j;
                    else if (_selectedPresetIndex == j && _selectedPresetDefault == defaultPresets)
                        _selectedPresetIndex = i;
                    PresetDB.Modify(-1, null);
                    ImGui.ResetMouseDragDelta();
                }
            }
        }
    }

    private bool DrawSaveCurrentPresetButton() => UIMisc.Button(Loc.T("PRESETDB_Save", "Save"), 0, (!HaveUnsavedModifications, Loc.T("PRESETDB_NotModified", "Current preset is not modified")), (_selectedPreset?.NameConflict ?? false, Loc.T("PRESETDB_NameConflict", "Current preset name is empty or duplicates name of other existing preset")));

    private void RevertCurrentPreset() => _selectedPreset = new(PresetDB, _selectedPresetIndex, _selectedPresetDefault, _selectedModuleType);

    private void SaveCurrentPreset()
    {
        if (!_selectedPresetDefault && _selectedPreset != null && _selectedPreset.Modified && !_selectedPreset.NameConflict)
        {
            PresetDB.Modify(_selectedPresetIndex, _selectedPreset.Preset);
            if (_selectedPresetIndex < 0)
                _selectedPresetIndex = PresetDB.UserPresets.Count - 1;
            RevertCurrentPreset();
        }
        else
        {
            Service.Log($"[PD] Save called when current preset #{_selectedPresetIndex} (default={_selectedPresetDefault}) is not modified or has bad name '{_selectedPreset?.Preset.Name}'");
        }
    }

    private void SaveCurrentPresetAsCopy()
    {
        if (_selectedPresetIndex >= 0 && _selectedPreset != null)
        {
            _selectedPreset.DetachFromSource();
            _selectedPreset.MakeNameUnique();
            _selectedPresetIndex = PresetDB.UserPresets.Count;
            _selectedPresetDefault = false;
            PresetDB.Modify(-1, _selectedPreset.Preset);
            RevertCurrentPreset();
        }
        else
        {
            Service.Log($"[PD] Save-as called when no preset is selected");
        }
    }

    private void CreateNewPreset(int referenceIndex, bool referenceDefault)
    {
        _selectedPresetIndex = -1;
        _selectedPresetDefault = false;
        _selectedPreset = new(PresetDB, referenceIndex, referenceDefault, _selectedModuleType);
        _selectedPreset.DetachFromSource();
        _selectedPreset.MakeNameUnique();
    }

    private void DeleteCurrentPreset()
    {
        if (!_selectedPresetDefault && _selectedPresetIndex >= 0)
        {
            PresetDB.Modify(_selectedPresetIndex, null);
            _selectedPresetIndex = -1;
            _selectedPreset = null;
        }
        else
        {
            Service.Log($"[PD] Delete called default or no preset is selected (index={_selectedPresetIndex}, default={_selectedPresetDefault})");
        }
    }

    private void CompleteChangeCurrentPreset()
    {
        _selectedPresetIndex = _pendingSelectPresetIndex;
        _selectedPresetDefault = _pendingSelectPresetDefault;
        _pendingSelectPresetIndex = -1;
        _pendingSelectPresetDefault = false;
        RevertCurrentPreset();
    }

    private void ExportToClipboard()
    {
        if (_selectedPreset != null)
        {
            ImGui.SetClipboardText(JsonSerializer.Serialize(_selectedPreset.Preset, Serialization.BuildSerializationOptions()));
        }
        else
        {
            Service.Log($"[PD] Export called no preset is selected");
        }
    }

    private void ImportNewPresetFromClipboard()
    {
        try
        {
            var finfo = new FileInfo("<import from clipboard>");

            // let users import encounter-specific plans from here for convenience
            var json = JsonNode.Parse(ImGui.GetClipboardText());
            if (json?.AsObject()?.ContainsKey("Encounter") == true)
            {
                foreach (var conv in PlanPresetConverter.PlanSchema.Converters)
                    json = conv(json, 0, finfo);

                var plan = JsonSerializer.Deserialize<Plan>(json, Serialization.BuildSerializationOptions())!;
                plan.Guid = Guid.NewGuid().ToString();

                rotationDB.Plans.ModifyPlan(null, plan);

                Service.Notifications.AddNotification(new()
                {
                    Content = $"Imported plan '{plan.Name}' for L{plan.Level} {plan.Class}"
                });

                return;
            }

            json = new JsonArray(json);

            foreach (var conv in PlanPresetConverter.PresetSchema.Converters)
                json = conv(json, 0, finfo);

            var preset = JsonSerializer.Deserialize<Preset>(json.AsArray()[0], Serialization.BuildSerializationOptions())!;
            _selectedPresetIndex = -1;
            _selectedPresetDefault = false;
            _selectedPreset = new(PresetDB, preset, _selectedModuleType);
        }
        catch (Exception ex)
        {
            Service.Log($"Failed to parse preset: {ex}");
        }
    }
}
