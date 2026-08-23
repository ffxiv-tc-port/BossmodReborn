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

    // 📌 預設 false 是刻意的:這是新行為(不掛 preset 也會冒出方位錐),既有使用者不該被它改到。
    //    BMR 的 ConfigNode.Deserialize 只遍歷存檔 JSON 裡「已存在」的鍵,新欄位在既有存檔裡不存在,
    //    所以會保留這裡的初始值 —— 也就是既有使用者拿到的就是 false。
    // 🔴 這個選項只寫 AIHints.PositionalHintDisplayOnly(純顯示欄位),
    //    絕不寫 RecommendedPositional —— 後者會被 AIBehaviour 讀去設 PreferredPosition,
    //    那會讓 AI 真的開始繞到目標側背。顯示與走位必須維持解耦。
    [PropertyDisplay("Derive positional hints without an active preset",
        tooltip: "Normally the positional cones only appear while an autorotation preset that provides positional guidance is active.\n\nWith this enabled, BMR derives the next positional for melee jobs (MNK/DRG/NIN/SAM/RPR/VPR) straight from your job gauge, combo state and buffs, so the cones show up with no preset at all.\n\nRequires \"Show positional hints in world\" to be enabled as well.\n\nPurely visual - it never moves your character and never changes what the AI targets or where it stands.")]
    public bool ShowPositionalsWithoutPreset = false;

    // 📌 預設 true 是刻意的：BMR 的 ConfigNode.Deserialize 只遍歷存檔 JSON 裡「已存在」的鍵，
    //    新欄位在既有使用者的存檔裡不存在，所以會保留這裡的初始值 —— 也就是既有使用者也直接看得到。
    //    這是純顯示的疊加層，不改變任何移動行為，開著沒有風險。
    [PropertyDisplay("Show dodge movement path in world",
        tooltip: "Draws a line in the game world from your character to the spot the automatic movement module is steering towards this instant, plus the next waypoint after it when the path bends.\n\nThe line switches to the danger color when there is less than 1 second of safety margin left to reach that spot.\n\nPurely visual - it does not change where or how the AI moves.")]
    public bool ShowMovementPath = true;

    [PropertyDisplay("Automatically disable autorotation when exiting combat")]
    public bool ClearPresetOnCombatEnd = false;

    [PropertyDisplay("Automatically reenable force-disabled autorotation when exiting combat")]
    public bool ClearForceDisableOnCombatEnd = true;

    [PropertyDisplay("Early pull threshold", tooltip: "If someone enters combat with a boss when the countdown is longer than this value, it's consider a ninja-pull and autorotation is force disabled")]
    [PropertySlider(0, 30, Speed = 1)]
    public float EarlyPullThreshold = 1.5f;
}
