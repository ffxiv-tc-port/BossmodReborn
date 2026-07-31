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
    private readonly Action _openConfig;

    public DTRProvider(RotationModuleManager manager, AIManager ai, Action openConfig)
    {
        _mgr = manager;
        _ai = ai;
        _openConfig = openConfig;

        // 左鍵＝輪流切下一個 preset（不跳選單，要挑特定 preset 直接開 BMR 視窗比較快）；
        // 右鍵＝開設定視窗。
        _autorotationEntry.OnClick = ev =>
        {
            if (ev.ClickType == MouseClickType.Right)
                _openConfig();
            else
                CyclePreset();
        };

        _aiEntry.OnClick = ev =>
        {
            if (ev.ClickType == MouseClickType.Right)
            {
                _openConfig();
                return;
            }
            if (_ai.Beh == null)
                _ai.SwitchToFollow(_aiConfig.FollowSlot);
            else
                _ai.SwitchToIdle();
        };
    }

    /// <summary>
    /// 依序輪流：關閉 → preset 1 → preset 2 → … → 關閉。
    /// 找不到目前 preset（例如剛被刪掉）就從頭開始。
    /// </summary>
    private void CyclePreset()
    {
        var presets = _mgr.Database.Presets.VisiblePresets;
        if (presets.Count == 0)
        {
            _mgr.Preset = null;
            return;
        }

        // null 代表「關閉」，排在輪替序列的第 0 位。
        var current = _mgr.Preset == null || _mgr.Preset == RotationModuleManager.ForceDisable
            ? -1
            : presets.IndexOf(_mgr.Preset);

        var next = current + 1;
        _mgr.Preset = next >= presets.Count ? null : presets[next];
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

        // 圖示化之後光看畫面認不出是哪個外掛，所以提示要把「這是什麼」講完整。
        _autorotationEntry.Tooltip = new SeString(new TextPayload(
            $"BossMod Reborn — 自動輪換\n目前：{name}\n\n左鍵：輪流切換下一個 preset\n右鍵：開啟設定視窗"));

        _aiEntry.Shown = _aiConfig.ShowDTR;
        // DTR 空間很擠：開關狀態用成對圖示表達，不再寫「AI: On/Off」。
        // Mentor（導師冠）＝ AI 正在替你行動；NoCircle ＝ 關閉。
        var aiOn = _ai.Beh != null;
        _aiEntry.Text = new SeString(new IconPayload(
            aiOn ? BitmapFontIcon.Mentor : BitmapFontIcon.NoCircle));
        _aiEntry.Tooltip = new SeString(new TextPayload(
            $"BossMod Reborn — AI 自動操作\n目前：{(aiOn ? "開啟（導師冠圖示）" : "關閉（禁止圖示）")}\n\n左鍵：開啟／關閉 AI\n右鍵：開啟設定視窗"));
    }
}
