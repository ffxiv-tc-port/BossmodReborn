namespace BossMod.Autorotation;

[ConfigDisplay(Name = "Autorotation", Order = 5)]
public sealed class AutorotationConfig : ConfigNode
{
    [PropertyDisplay("Show in-game UI")]
    public bool ShowUI = false;

    public enum DtrStatus
    {
        [PropertyDisplay("Disabled")]
        None,
        [PropertyDisplay("Text only")]
        TextOnly,
        [PropertyDisplay("With icon")]
        Icon
    }

    [PropertyDisplay("Show autorotation preset in the server info bar")]
    public DtrStatus ShowDTR = DtrStatus.None;

    [PropertyDisplay("Hide VBM Default preset", tooltip: "If you've created your own presets and no longer need the included default, this option will prevent it from being shown in the Autorotation and Preset Editor windows.")]
    public bool HideDefaultPreset = false;

    // 沒有 PropertyDisplay：這不是設定頁上的選項，是「別再顯示這個建議」的一次性狀態
    public bool SuggestHealerAI = true;

    [PropertyDisplay("Show positional hints in world", tooltip: "Show tips for positional abilities, indicating to move to the flank or rear of your target")]
    public bool ShowPositionals = false;

    [PropertyDisplay("Automatically disable autorotation when exiting combat")]
    public bool ClearPresetOnCombatEnd = false;

    [PropertyDisplay("Automatically reenable force-disabled autorotation when exiting combat")]
    public bool ClearForceDisableOnCombatEnd = true;

    [PropertyDisplay("Early pull threshold", tooltip: "If someone enters combat with a boss when the countdown is longer than this value, it's consider a ninja-pull and autorotation is force disabled")]
    [PropertySlider(0, 30, Speed = 1)]
    public float EarlyPullThreshold = 1.5f;
}
