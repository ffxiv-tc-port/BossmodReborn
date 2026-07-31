using BossMod.AI;
using BossMod.Autorotation;
using Dalamud.Game.Gui.Dtr;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Bindings.ImGui;

namespace BossMod;

internal sealed class DTRProvider : IDisposable
{
    private readonly RotationModuleManager _mgr;
    private readonly AIManager _ai;
    private readonly IDtrBarEntry _autorotationEntry = Service.DtrBar.Get("bmr-autorotation");
    private readonly IDtrBarEntry _aiEntry = Service.DtrBar.Get("bmr-ai");
    private static readonly AIConfig _aiConfig = Service.Config.Get<AIConfig>();
    private bool _wantOpenPopup;

    public DTRProvider(RotationModuleManager manager, AIManager ai)
    {
        _mgr = manager;
        _ai = ai;
        _autorotationEntry.OnClick = _ => _wantOpenPopup = true;
        _aiEntry.OnClick = _ =>
        {
            if (_ai.Beh == null)
                _ai.SwitchToFollow(_aiConfig.FollowSlot);
            else
                _ai.SwitchToIdle();
        };
    }

    public void Dispose()
    {
        _autorotationEntry.Remove();
        _aiEntry.Remove();
    }

    public void Update()
    {
        _autorotationEntry.Shown = RotationModuleManager.Config.ShowDTR != AutorotationConfig.DtrStatus.None;
        var (icon, name) = _mgr.Preset == null ? (BitmapFontIcon.SwordSheathed, "Idle") : _mgr.Preset == RotationModuleManager.ForceDisable ? (BitmapFontIcon.SwordSheathed, "Disabled") : (BitmapFontIcon.SwordUnsheathed, _mgr.Preset.Name);
        Payload prefix = RotationModuleManager.Config.ShowDTR == AutorotationConfig.DtrStatus.TextOnly ? new TextPayload("bmr: ") : new IconPayload(icon);
        _autorotationEntry.Text = new SeString(prefix, new TextPayload(name));

        _aiEntry.Shown = _aiConfig.ShowDTR;
        // DTR 空間很擠：開關狀態用成對圖示表達，不再寫「AI: On/Off」。
        // Mentor（導師冠）＝ AI 正在替你行動；NoCircle ＝ 關閉。
        _aiEntry.Text = new SeString(new IconPayload(
            _ai.Beh == null ? BitmapFontIcon.NoCircle : BitmapFontIcon.Mentor));

        if (_wantOpenPopup && _mgr.Player != null)
        {
            ImGui.OpenPopup("bmr_dtr_menu");
            _wantOpenPopup = false;
        }

        using var popup = ImRaii.Popup("bmr_dtr_menu");
        if (popup)
            if (UIRotationWindow.DrawRotationSelector(_mgr))
                ImGui.CloseCurrentPopup();
    }
}
