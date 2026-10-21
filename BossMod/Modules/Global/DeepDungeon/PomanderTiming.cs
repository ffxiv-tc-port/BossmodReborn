namespace BossMod.Global.DeepDungeon;

// 本檔假設與契約：
// - 純判斷，不碰遊戲狀態與 Dalamud，離線可測；呼叫端（AutoClear，框架執行緒）負責把當幀狀態抄成值型別交進來。
// - 只決定「現在該不該用哪一個」，節流、重試上限、手動暫停、組隊判斷都在呼叫端。

/// <summary>魔陶器「拿滿 3 個時用一個」的時機分類。<see cref="None"/>＝不提供自動使用。</summary>
public enum PomanderTiming : byte
{
    None,
    /// <summary>非 BOSS 層、進層後的時段內。</summary>
    FloorStart,
    /// <summary>BOSS 層、進層後的時段內。</summary>
    BossFloorStart,
    /// <summary>自動前往傳送裝置已經開始走（效果作用在下一層）。</summary>
    ApproachPassage,
    /// <summary>戰鬥外、本層還沒接過戰。</summary>
    BeforeFirstPull,
}

/// <summary>判斷當下的樓層狀態；全部是呼叫端當幀抄好的值。</summary>
public readonly record struct PomanderTimingSituation(
    bool BossFloor,
    bool InEntryWindow,
    bool WalkingToPassage,
    bool InCombat,
    bool EngagedThisFloor,
    bool HarmfulEnchantment,
    bool BeneficialEnchantment,
    bool ItemPenalty);

/// <summary>一個魔陶器槽位的當幀內容。</summary>
public readonly record struct PomanderSlotView(PomanderID Id, byte Count, bool Usable, bool Active);

public static class PomanderTimingRules
{
    public const int FullStack = 3;

    // 形態變化、蠍尾獅化、基路伯化、改變敵人是保命或改變樓層的按鈕，刻意不給時機（＝永遠不自動）。
    public static PomanderTiming TimingOf(PomanderID p) => p switch
    {
        PomanderID.Lust => PomanderTiming.BossFloorStart,
        PomanderID.Safety or PomanderID.Sight or PomanderID.Intuition or PomanderID.Serenity
            or PomanderID.ProtoSafety or PomanderID.ProtoSight or PomanderID.ProtoIntuition or PomanderID.ProtoSerenity => PomanderTiming.FloorStart,
        PomanderID.Affluence or PomanderID.Flight or PomanderID.ProtoAffluence or PomanderID.ProtoFlight => PomanderTiming.ApproachPassage,
        PomanderID.Strength or PomanderID.Steel or PomanderID.Fortune
            or PomanderID.ProtoStrength or PomanderID.ProtoSteel or PomanderID.ProtoFortune => PomanderTiming.BeforeFirstPull,
        _ => PomanderTiming.None,
    };

    public static bool IsSerenity(PomanderID p) => p is PomanderID.Serenity or PomanderID.ProtoSerenity;

    // 運氣上升只影響擊敗一般敵人掉的寶箱，BOSS 層用了沒意義。
    private static bool IsFortune(PomanderID p) => p is PomanderID.Fortune or PomanderID.ProtoFortune;

    /// <summary>樓層「魔法效果」在玩家身上的狀態（Status 1088～1097、1549），與 DeepDungeonFloorEffectUI 的順序一一對應。</summary>
    public static bool IsHarmfulEnchantment(uint statusId) => statusId is 1088 or 1089 or 1090 or 1092 or 1094 or 1095 or 1097 or 1549;

    /// <summary>加速、最大 HP 與 MP 提升：魔法效果解除會連這兩個一起消掉。</summary>
    public static bool IsBeneficialEnchantment(uint statusId) => statusId is 1091 or 1093;

    public static bool TimingMet(PomanderID p, in PomanderTimingSituation s) => TimingOf(p) switch
    {
        PomanderTiming.BossFloorStart => s.BossFloor && s.InEntryWindow,
        PomanderTiming.FloorStart => !s.BossFloor && s.InEntryWindow
            && (!IsSerenity(p) || s.HarmfulEnchantment && !s.BeneficialEnchantment),
        PomanderTiming.ApproachPassage => !s.BossFloor && s.WalkingToPassage,
        PomanderTiming.BeforeFirstPull => !s.InCombat && !s.EngagedThisFloor && !(s.BossFloor && IsFortune(p)),
        _ => false,
    };

    /// <summary>
    /// 依槽位順序找第一個「已滿 3 個、使用者有勾、遊戲說可用、效果沒在生效、時機對」的魔陶器。
    /// </summary>
    /// <param name="enabled">索引＝<see cref="PomanderID"/>；越界視為沒勾。</param>
    /// <returns>沒有就回 <see cref="PomanderID.None"/>。</returns>
    /// <remarks>禁止使用道具時只剩魔法效果解除可用（它正是用來解這個的）。</remarks>
    public static PomanderID Pick(in PomanderTimingSituation s, ReadOnlySpan<PomanderSlotView> slots, ReadOnlySpan<bool> enabled)
    {
        foreach (ref readonly var slot in slots)
        {
            var p = slot.Id;
            if (p == PomanderID.None || slot.Count < FullStack || !slot.Usable || slot.Active)
                continue;
            if ((uint)p >= (uint)enabled.Length || !enabled[(int)p])
                continue;
            if (s.ItemPenalty && !IsSerenity(p))
                continue;
            if (TimingMet(p, s))
                return p;
        }
        return PomanderID.None;
    }
}
