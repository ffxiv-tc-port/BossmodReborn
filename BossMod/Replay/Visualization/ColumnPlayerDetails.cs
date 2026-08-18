using BossMod.Autorotation;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Bindings.ImGui;

namespace BossMod.ReplayVisualization;

// TODO: currently it assumes that there's only one instance that can edit db, it won't refresh if plan is edited and saved in a different instance...
public sealed class ColumnPlayerDetails : Timeline.ColumnGroup
{
    private readonly StateMachineTree _tree;
    private readonly List<int> _phaseBraches;
    private readonly Replay _replay;
    private readonly Replay.Encounter _enc;
    private readonly Replay.Participant _player;
    private readonly Class _playerClass;
    private readonly PlanDatabase _planDatabase;
    private readonly BossModuleRegistry.Info? _moduleInfo;

    private readonly ColumnPlayerActions _actions;
    private readonly ColumnActorStatuses _statuses;

    private readonly ColumnActorHP _hp;
    private readonly ColumnPlayerGauge? _gauge;
    private readonly ColumnSeparator _resourceSep;

    private int _selectedPlan = -1;
    private CooldownPlannerColumns? _planner;
    private readonly List<Replay.Action> _plannerActions = [];

    public bool PlanModified => _planner?.Modified ?? false;

    public ColumnPlayerDetails(Timeline timeline, StateMachineTree tree, List<int> phaseBranches, Replay replay, Replay.Encounter enc, Replay.Participant player, Class playerClass, PlanDatabase planDB)
        : base(timeline)
    {
        _tree = tree;
        _phaseBraches = phaseBranches;
        _replay = replay;
        _enc = enc;
        _player = player;
        _playerClass = playerClass;
        _planDatabase = planDB;
        _moduleInfo = BossModuleRegistry.FindByOID(enc.OID);

        _actions = Add(new ColumnPlayerActions(timeline, tree, phaseBranches, replay, enc, player, playerClass));
        _actions.Name = player.NameHistory.FirstOrDefault().Value.name;

        _statuses = Add(new ColumnActorStatuses(timeline, tree, phaseBranches, replay, enc, player));

        _hp = Add(new ColumnActorHP(timeline, tree, phaseBranches, replay, enc, player));
        _gauge = ColumnPlayerGauge.Create(timeline, tree, phaseBranches, replay, enc, player, playerClass);
        if (_gauge != null)
            Add(_gauge);
        _resourceSep = Add(new ColumnSeparator(timeline));

        if (_moduleInfo?.PlanLevel > 0)
        {
            var minTime = _enc.Time.Start.AddSeconds(Timeline.MinTime);
            _plannerActions = [.. _replay.Actions.SkipWhile(a => a.Timestamp < minTime).TakeWhile(a => a.Timestamp <= _enc.Time.End).Where(a => a.Source == _player)];
            var plans = _planDatabase.GetPlans(_moduleInfo.ModuleType, _playerClass);
            UpdateSelectedPlan(plans, plans.SelectedIndex);
        }
    }

    public void DrawConfig(UITree tree)
    {
        DrawConfigPlanner(tree);
        foreach (var _1 in tree.Node(Loc.T("Actions")))
            _actions.DrawConfig(tree);
        foreach (var _1 in tree.Node(Loc.T("Statuses")))
            _statuses.DrawConfig(tree);

        foreach (var _1 in tree.Node(Loc.T("Resources")))
        {
            DrawResourceColumnToggle(_hp, Loc.T("HP"));
            if (_gauge != null)
                DrawResourceColumnToggle(_gauge, Loc.T("Gauge"));
        }
    }

    public void SaveChanges()
    {
        if (_moduleInfo != null && _planner != null && _planner.Modified)
        {
            var plans = _planDatabase.GetPlans(_moduleInfo.ModuleType, _playerClass);
            _planDatabase.ModifyPlan(plans.Plans[_selectedPlan], _planner.Plan.MakeClone());
            _planner.Modified = false;
        }
    }

    private void DrawConfigPlanner(UITree tree)
    {
        if (_moduleInfo == null || _moduleInfo.PlanLevel <= 0)
        {
            tree.LeafNode(Loc.T("Planner: not supported for this encounter"));
            return;
        }

        foreach (var _1 in tree.Node(Loc.T("Planner")))
        {
            var plans = _planDatabase.GetPlans(_moduleInfo.ModuleType, _playerClass);
            UpdateSelectedPlan(plans, DrawPlanSelector(_moduleInfo.ModuleType, plans, _selectedPlan));
            if (_planner != null)
            {
                ImGui.TextUnformatted($"GUID: {_planner.Plan.Guid}");
                _planner.DrawCommonControls();

                bool haveDifferentPhaseTimes = false;
                for (int i = 0; i < _tree.Phases.Count; ++i)
                {
                    _planner.Modified |= ImGui.SliderFloat($"{_tree.Phases[i].Name}###phase-duration-{i}", ref _planner.Plan.PhaseDurations.Ref(i), 0, _tree.Phases[i].MaxTime, $"%.1f (replay: {_tree.Phases[i].Duration:f1} / {_tree.Phases[i].MaxTime:f1})");
                    haveDifferentPhaseTimes |= _planner.Plan.PhaseDurations[i] != _tree.Phases[i].Duration;
                }

                using (ImRaii.Disabled(!haveDifferentPhaseTimes))
                {
                    if (ImGui.Button(Loc.T("Sync phase durations to replay")))
                    {
                        for (int i = 0; i < _tree.Phases.Count; ++i)
                            _planner.Plan.PhaseDurations[i] = _tree.Phases[i].Duration;
                        _planner.Modified = true;
                    }
                }
            }
        }
    }

    private int DrawPlanSelector(Type moduleType, PlanDatabase.PlanList list, int selection)
    {
        using (ImRaii.Disabled(_planner?.Modified ?? false))
            selection = UIPlanDatabaseEditor.DrawPlanCombo(list, selection, "###planner");

        var isDefault = selection == list.SelectedIndex;
        ImGui.SameLine();
        if (ImGui.Checkbox(Loc.T("Default"), ref isDefault))
        {
            list.SelectedIndex = isDefault ? selection : -1;
            _planDatabase.ModifyManifest(moduleType, _playerClass);
        }
        ImGui.SameLine();
        if (UIMisc.Button(Loc.T("Save"), _planner == null || !_planner.Modified, Loc.T("Current plan was not modified")))
            SaveChanges();
        ImGui.SameLine();
        if (UIMisc.Button(Loc.T("Copy"), _planner == null, Loc.T("No plan selected")) && _planner != null && _moduleInfo != null)
        {
            _planner.Plan.Guid = Guid.NewGuid().ToString();
            _planner.Plan.Name += " Copy";
            var plans = _planDatabase.GetPlans(_moduleInfo.ModuleType, _playerClass);
            selection = _selectedPlan = plans.Plans.Count;
            _planDatabase.ModifyPlan(null, _planner.Plan.MakeClone());
            _planner.Modified = false;
        }
        ImGui.SameLine();
        if (UIMisc.Button(Loc.T("Revert"), _planner == null || !_planner.Modified, Loc.T("Current plan was not modified")) && _planner != null && _moduleInfo != null)
        {
            var plans = _planDatabase.GetPlans(_moduleInfo.ModuleType, _playerClass);
            _planner.Plan = plans.Plans[_selectedPlan].MakeClone();
            _planner.SyncCreateImport();
            _planner.Modified = false;
        }
        ImGui.SameLine();
        // 🔴 這裡選的是「計劃」不是「預設」：閘門條件是 _planner.Modified（計劃有未存變更）。
        //    上游的原句寫成 "Current preset is modified..."，名詞用錯——這個視窗裡根本沒有預設，
        //    而同一個函式的儲存／還原鈕（下面幾行）用的是 "Current plan was not modified"。
        if (UIMisc.Button(Loc.T("New"), _planner != null && _planner.Modified, Loc.T("Current plan is modified, save or discard changes")) && _moduleInfo != null)
        {
            var plans = _planDatabase.GetPlans(_moduleInfo.ModuleType, _playerClass);
            var plan = new Plan($"New {plans.Plans.Count + 1}", _moduleInfo.ModuleType) { Guid = Guid.NewGuid().ToString(), Class = _playerClass, Level = _moduleInfo.PlanLevel };
            _planDatabase.ModifyPlan(null, plan);
            selection = plans.Plans.Count - 1;
        }
        ImGui.SameLine();
        // 🔴 同上：_planner == null 是「沒有選取計劃」，上游卻寫 "No preset is selected"。
        //    改用同一個函式第 143 行既有的 "No plan selected"（閘門條件逐字相同）。
        //    "Hold shift to delete" 不帶名詞，維持共用 PRESETDB_HoldShift。
        if (UIMisc.Button(Loc.T("Delete"), 0, (!ImGui.GetIO().KeyShift, Loc.T("PRESETDB_HoldShift", "Hold shift to delete")), (_planner == null, Loc.T("No plan selected"))) && _moduleInfo != null && _selectedPlan >= 0)
        {
            var plans = _planDatabase.GetPlans(_moduleInfo.ModuleType, _playerClass);
            _planDatabase.ModifyPlan(plans.Plans[_selectedPlan], null);
            selection = -1;
        }

        return selection;
    }

    private void UpdateSelectedPlan(PlanDatabase.PlanList list, int newSelection)
    {
        if (_selectedPlan == newSelection)
            return;

        if (_planner != null)
        {
            Columns.Remove(_planner);
            _planner = null;
        }
        _selectedPlan = newSelection;
        if (_selectedPlan >= 0)
        {
            _planner = AddBefore(new CooldownPlannerColumns(list.Plans[newSelection].MakeClone(), Timeline, _tree, _phaseBraches, false, _plannerActions, _enc.Time.Start), _actions);
        }
    }

    private void DrawResourceColumnToggle(IToggleableColumn col, string name)
    {
        var visible = col.Visible;
        if (ImGui.Checkbox(name, ref visible))
        {
            col.Visible = visible;
            _resourceSep.Width = _hp.Visible || (_gauge?.Visible ?? false) ? 1 : 0;
        }
    }
}
