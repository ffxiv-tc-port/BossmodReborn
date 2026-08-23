using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Bindings.ImGui;

namespace BossMod.Autorotation;

public sealed class UIRotationWindow : UIWindow
{
    private readonly RotationModuleManager _mgr;
    private readonly ActionManagerEx _amex;
    private readonly AutorotationConfig _config = Service.Config.Get<AutorotationConfig>();
    private readonly EventSubscriptions _subscriptions;

    public UIRotationWindow(RotationModuleManager mgr, ActionManagerEx amex, Action openConfig) : base("Autorotation", false, new(400, 400), ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoFocusOnAppearing)
    {
        _mgr = mgr;
        _amex = amex;
        _subscriptions = new
        (
            _config.Modified.ExecuteAndSubscribe(() => IsOpen = _config.ShowUI)
        );
        RespectCloseHotkey = false;
        TitleBarButtons.Add(new() { Icon = FontAwesomeIcon.Cog, IconOffset = new(1), Click = _ => openConfig() });
    }

    protected override void Dispose(bool disposing)
    {
        _subscriptions.Dispose();
        base.Dispose(disposing);
    }

    public void SetVisible(bool vis)
    {
        if (_config.ShowUI != vis)
        {
            _config.ShowUI = vis;
            _config.Modified.Fire();
        }
    }

    // ⚠️ 疊加層畫在 PreOpenCheck 而不是 Draw：Dalamud 的 Window.DrawInternal 會先呼叫 PreOpenCheck，
    // 之後才用 IsOpen 決定要不要畫視窗本身，所以就算「自動循環」視窗是關的，這裡照樣每幀執行。
    // （既有的 DrawPositional 就是靠這一點才能在視窗關閉時仍顯示站位提示。）
    public override void PreOpenCheck()
    {
        DrawPositional();
        DrawMovementPath();
    }

    public override bool DrawConditions() => _mgr.WorldState.Party.Player() != null;

    public override void Draw()
    {
        var player = _mgr.Player;
        if (player == null)
            return;

        DrawRotationSelector(_mgr);

        var activeModule = _mgr.Bossmods.ActiveModule;
        if (activeModule != null)
        {
            ImGui.TextUnformatted($"CD Plan:");

            if (activeModule.Info?.PlanLevel > 0)
            {
                ImGui.SameLine();
                var plans = _mgr.Database.Plans.GetPlans(activeModule.GetType(), player.Class);
                var newSel = UIPlanDatabaseEditor.DrawPlanCombo(plans, plans.SelectedIndex, "");
                if (newSel != plans.SelectedIndex)
                {
                    plans.SelectedIndex = newSel;
                    _mgr.Database.Plans.ModifyManifest(activeModule.GetType(), player.Class);
                }

                ImGui.SameLine();
                if (ImGui.Button(plans.SelectedIndex >= 0 ? "Edit" : "New"))
                {
                    if (plans.SelectedIndex < 0)
                    {
                        var plan = new Plan($"New {plans.Plans.Count + 1}", activeModule.GetType()) { Guid = Guid.NewGuid().ToString(), Class = player.Class, Level = activeModule.Info.PlanLevel };
                        plans.SelectedIndex = plans.Plans.Count;
                        _mgr.Database.Plans.ModifyPlan(null, plan);
                    }
                    UIPlanDatabaseEditor.StartPlanEditor(_mgr.Database.Plans, plans.Plans[plans.SelectedIndex], activeModule.StateMachine);
                }

                if (newSel >= 0 && _mgr.Preset != null)
                {
                    ImGui.SameLine();
                    using var style = ImRaii.PushColor(ImGuiCol.Text, Colors.TextColor2);
                    // 🔴 觸發條件是 Preset != null，而「停用」鈕塞進去的哨兵 ForceDisable 也是一個 Preset
                    //    （RotationModuleManager.ForceDisable = new Preset("")），所以按下同一個視窗裡的
                    //    「停用」之後也會跳這個警告 —— 原本的單一句子卻寫「你啟用了一個預設」，
                    //    使用者明明什麼預設都沒選。兩種狀態抑制計劃的機制相同
                    //    （RotationModuleManager.Update 只在 Preset == null 時才會用計劃的模組），
                    //    但原因不同，訊息要分開講。順帶把這句接上在地化，原本是寫死英文。
                    UIMisc.HelpMarker(() => _mgr.Preset == RotationModuleManager.ForceDisable
                        ? Loc.T("ROT_PlanBlockedByDisable", "Autorotation is force-disabled, which also stops the CD plan from running!")
                        : Loc.T("ROT_PlanBlockedByPreset", "You have a preset activated, which fully overrides the CD plan!"),
                        FontAwesomeIcon.ExclamationTriangle);
                }
            }
        }

        // TODO: more fancy action history/queue...
        ImGui.TextUnformatted($"Modules: {_mgr}");
        if (_mgr.Preset?.Modules.Any(m => m.TransientSettings.Count > 0) ?? false)
        {
            ImGui.SameLine();
            using (ImRaii.PushColor(ImGuiCol.Text, 0xff00ff00))
                UIMisc.IconText(FontAwesomeIcon.BoltLightning, "(4)");
            if (ImGui.IsItemHovered())
            {
                using var tooltip = ImRaii.Tooltip();
                ImGui.TextUnformatted(Loc.T("ROT_TransientStrategies", "Transient strategies:"));
                foreach (var m in _mgr.Preset.Modules.Where(m => m.TransientSettings.Count > 0))
                {
                    ImGui.TextUnformatted($"> {m.Type.FullName}");
                    using var indent = ImRaii.PushIndent();
                    foreach (var s in m.TransientSettings)
                    {
                        var track = m.Definition.Configs[s.Track];
                        ImGui.TextUnformatted($"{track.InternalName} = {track.ToDisplayString(s.Value)}");
                    }
                }
            }
        }

        ImGui.TextUnformatted($"GCD={_mgr.WorldState.Client.Cooldowns[ActionDefinitions.GCDGroup].Remaining:f3}, AnimLock={_amex.EffectiveAnimationLock:f3}+{_amex.AnimationLockDelayEstimate:f3}, Combo={_amex.ComboTimeLeft:f3}, RBIn={_mgr.Bossmods.RaidCooldowns.NextDamageBuffIn():f3}");
        foreach (var a in _mgr.Hints.ActionsToExecute.Entries)
        {
            ImGui.TextUnformatted($"> {a.Action} ({a.Priority:f2}) @ ({a.Target?.Name ?? "<none>"})");
        }
    }

    public override void OnClose() => SetVisible(false);

    public static bool DrawRotationSelector(RotationModuleManager mgr)
    {
        var modified = false;
        if (mgr.Player == null)
            return modified;

        ImGui.TextUnformatted(Loc.T("ROT_Presets", "Presets:"));

        ImGui.SameLine();

        using (ImRaii.PushColor(ImGuiCol.Button, Colors.ButtonPushColor1, mgr.Preset == RotationModuleManager.ForceDisable))
        using (ImRaii.PushColor(ImGuiCol.ButtonHovered, Colors.ButtonPushColor3, mgr.Preset == RotationModuleManager.ForceDisable))
        using (ImRaii.PushColor(ImGuiCol.ButtonActive, Colors.ButtonPushColor4, mgr.Preset == RotationModuleManager.ForceDisable))
        {
            if (ImGui.Button(Loc.T("ROT_Disabled", "Disabled")))
            {
                mgr.Preset = mgr.Preset == RotationModuleManager.ForceDisable ? null : RotationModuleManager.ForceDisable;
                modified |= true;
            }
        }

        foreach (var p in mgr.Database.Presets.PresetsForClass(mgr.Player.Class))
        {
            ImGui.SameLine();
            using var col = ImRaii.PushColor(ImGuiCol.Button, Colors.ButtonPushColor2, mgr.Preset == p);
            using var colHovered = ImRaii.PushColor(ImGuiCol.ButtonHovered, Colors.ButtonPushColor5, mgr.Preset == p);
            using var colActive = ImRaii.PushColor(ImGuiCol.ButtonActive, Colors.ButtonPushColor6, mgr.Preset == p);
            if (ImGui.Button(p.Name))
            {
                mgr.Preset = mgr.Preset == p ? null : p;
                modified |= true;
            }
        }

        return modified;
    }

    private const float PositionalConeThickness = 2f;       // 主線粗細
    private const float PositionalConeOutlineExtra = 2f;    // 外框比主線粗這麼多（等於每邊 1px 深色邊）

    private void DrawPositional()
    {
        // 🔴 兩個來源、優先序固定:
        //   ① PositionalHintDisplayOnly —— 「不需 preset」的純顯示推導(Plugin.UpdatePositionalHintDisplay 寫的)
        //   ② RecommendedPositional —— 循環模組寫的,同時也是 AI 走位的輸入
        // 新欄位有值才用新欄位,否則退回舊欄位 ⇒ 既有掛 preset 的使用者行為完全不變。
        //(寫入端在 ① 有值時本來就會讓給 ②,所以實務上兩者不會同時有值;這裡的優先序是第二道保險。)
        var pos = _mgr.Hints.PositionalHintDisplayOnly;
        if (pos.Target == null)
            pos = _mgr.Hints.RecommendedPositional;

        if (_config.ShowPositionals && pos.Target != null && !pos.Target.Omnidirectional)
        {
            var color = PositionalColor(pos.Imminent, pos.Correct);
            var center = pos.Target.PosRot.XYZ();
            var radius = pos.Target.HitboxRadius + 3.5f;
            switch (pos.Pos)
            {
                case Positional.Flank:
                    DrawPositionalCone(center, radius, pos.Target.Rotation + 90.Degrees(), color);
                    DrawPositionalCone(center, radius, pos.Target.Rotation - 90.Degrees(), color);
                    break;
                case Positional.Rear:
                    DrawPositionalCone(center, radius, pos.Target.Rotation + 180.Degrees(), color);
                    break;
            }
        }
    }

    // 先畫深色粗線當外框、再畫亮色細線 —— 與 DrawPathSegment／MiniArena.AddLine 同一套做法。
    // 疊加層底下是 3D 場景，單色細線在亮地板或白色特效上會整條看不見；外框解決的是那個。
    // 🔑 對齊 NecroLens 基準：有方向（錐體本身就指向該站的位置）、有外框、**不疊顏色**
    //   （從容/急迫、站對/站錯仍然只靠 PositionalColor 換色表達，這裡不額外加半透明填色）。
    private static void DrawPositionalCone(Vector3 center, float radius, Angle direction, uint color)
    {
        if (Camera.Instance is not { } camera)
            return;
        camera.DrawWorldCone(center, radius, direction, 45.Degrees(), Colors.Shadows, PositionalConeThickness + PositionalConeOutlineExtra);
        camera.DrawWorldCone(center, radius, direction, 45.Degrees(), color, PositionalConeThickness);
    }

    private static uint PositionalColor(bool imminent, bool correct) => imminent
        ? (correct ? Colors.PositionalColor1 : Colors.PositionalColor2)
        : (correct ? Colors.PositionalColor3 : Colors.PositionalColor4);

    // ---- 閃避移動路徑疊加層 ----
    // 顯示風格對齊 NecroLens：有方向（箭頭）、有外框（深色底線 + 亮色上線兩次畫），
    // 從容/急迫用「換色」表達而不是疊色。

    private const float PathThickness = 2.5f;          // 主線粗細
    private const float PathOutlineExtra = 2f;         // 外框比主線粗這麼多（等於每邊 1px 深色邊）
    private const float ContinuationThickness = 1.5f;  // 第二段（再下一個路徑點）細一點，視覺上次要
    private const float DestinationMarkerRadius = 0.5f;
    private const float ArrowLength = 0.8f;
    private const float ArrowHalfWidth = 0.25f;
    private const float MinDrawDistanceSq = 0.25f;     // 玩家離目的點不到 0.5y 就不畫
    private const float MinSegmentLengthSq = 0.0625f;  // 短於 0.25y 的線段不畫箭頭

    private void DrawMovementPath()
    {
        // 🔴 無條件先消費掉，再做其他檢查：ConsumeVisualization 是「讀取後清空」的一次性交接，
        // 任何提早 return 而沒消費的路徑都會把這一幀的決策留到下一幀，被畫在早已過期的位置上。
        // （例如在有值待畫時關掉設定、之後又打開，而那時模組已不在啟用的預設集裡。）
        var vis = MiscAI.NormalMovement.ConsumeVisualization();
        if (!_config.ShowMovementPath)
            return;

        if (vis is not { } v || Camera.Instance is not { } camera)
            return;

        var player = _mgr.Player;
        if (player == null)
            return;

        var start = player.Position;
        // 太近就不畫：只有幾公分長的線在畫面上純粹是雜訊。
        // （移動模組自己的門檻是 0.1y，這裡刻意設得更大一點。）
        if ((v.Destination - start).LengthSq() < MinDrawDistanceSq)
            return;

        var y = player.PosRot.Y;
        var color = v.Urgent ? Colors.Danger : Colors.Safe;
        var pStart = start.ToVec3(y);
        var pDest = v.Destination.ToVec3(y);

        DrawPathSegment(camera, pStart, pDest, color, PathThickness);
        DrawDestinationMarker(camera, pDest, color);

        if (v.NextWaypoint is { } next && (next - v.Destination).LengthSq() >= MinDrawDistanceSq)
            DrawPathSegment(camera, pDest, next.ToVec3(y), color, ContinuationThickness);
    }

    // 先畫深色粗線再畫亮色細線 —— 與 MiniArena.AddLine 的外框做法相同，
    // 差別只在這裡是世界座標而且外框加得更粗（疊加層底下是 3D 場景，對比要求比雷達高）。
    private static void DrawPathSegment(Camera camera, Vector3 from, Vector3 to, uint color, float thickness)
    {
        var outline = thickness + PathOutlineExtra;
        camera.DrawWorldLine(from, to, Colors.Shadows, outline);
        camera.DrawWorldLine(from, to, color, thickness);

        var delta = to - from;
        var lenSq = delta.LengthSquared();
        if (lenSq < MinSegmentLengthSq)
            return; // 太短，正規化出來的方向會亂跳，箭頭不畫

        var dir = delta / MathF.Sqrt(lenSq);
        var side = Vector3.Cross(Vector3.UnitY, dir);
        var sideLenSq = side.LengthSquared();
        if (sideLenSq < 1e-6f)
            return; // 幾乎垂直向上/下時畫不出有意義的箭頭（兩端點同高時不會發生，純防呆）

        side = ArrowHalfWidth * (side / MathF.Sqrt(sideLenSq));
        var tail = to - ArrowLength * dir;
        camera.DrawWorldLine(tail + side, to, Colors.Shadows, outline);
        camera.DrawWorldLine(tail - side, to, Colors.Shadows, outline);
        camera.DrawWorldLine(tail + side, to, color, thickness);
        camera.DrawWorldLine(tail - side, to, color, thickness);
    }

    private static void DrawDestinationMarker(Camera camera, Vector3 center, uint color)
    {
        camera.DrawWorldCircle(center, DestinationMarkerRadius, Colors.Shadows, PathThickness + PathOutlineExtra);
        camera.DrawWorldCircle(center, DestinationMarkerRadius, color, PathThickness);
    }
}
