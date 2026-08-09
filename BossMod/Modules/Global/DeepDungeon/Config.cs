using Dalamud.Bindings.ImGui;

namespace BossMod.Global.DeepDungeon;

[ConfigDisplay(Name = "Auto-DeepDungeon (Experimental)", Parent = typeof(ModuleConfig))]
public sealed class AutoDDConfig : ConfigNode
{
    private static readonly Vector4 PrereqOkColor = new(0.45f, 0.80f, 0.45f, 1f);
    private static readonly Vector4 PrereqBadColor = new(0.95f, 0.70f, 0.30f, 1f);
    private static readonly Vector4 PrereqNoteColor = new(0.72f, 0.72f, 0.72f, 1f);

    /// <summary>
    /// 這一整頁的兩個前置條件，以及它們<b>現在</b>的狀態。
    /// </summary>
    /// <remarks>
    /// 🔑 為什麼需要：兩個閘門都在別的地方，而且失敗形式都是
    /// 「設定頁看起來一切正常、選項全部勾好，但什麼都不會發生」。
    /// <list type="number">
    /// <item>
    /// <b>模組成熟度</b>：深牢的區域模組全部標成 <c>Maturity.WIP</c>，而
    /// <c>ZoneModuleConfig.MinMaturity</c> 的預設值是 <c>Contributed</c>。
    /// <c>ZoneModuleRegistry.CreateModule</c> 的條件是「模組成熟度 &gt;= 要求的最低成熟度」，
    /// 而 WIP 比 Contributed <b>低</b> ⇒ <b>預設值下整個深牢模組根本不會被建立</b>，
    /// 連小地圖都不會出現。設定頁卻照樣完整渲染 —— 設定節點與模組的生命週期是分開的。
    /// </item>
    /// <item>
    /// <b>BMR 的 AI</b>：這一頁大部分選項最後都是寫進 <c>AIHints</c> 的
    /// <c>GoalZones</c>／<c>ForbiddenZones</c>，而那兩者只有 AI 的導航在讀，
    /// AI 沒開就是把提示算出來丟掉。（例外是自動選怪，它走
    /// <c>Hints.ForcedTarget</c>，由 <c>Plugin.ExecuteHints</c> 無條件執行。）
    /// </item>
    /// </list>
    /// ⚠️ 這裡讓設定節點去讀 <c>AIManager</c> 的執行期狀態，是刻意接受的分層破壞：
    /// 前置條件寫成靜態文字沒有用，使用者要知道的是「我現在缺的是哪一個」。
    /// </remarks>
    public override void DrawHeader(UITree tree, WorldState ws)
    {
        var minMaturity = ZoneModuleManager.Config.MinMaturity;
        // 深牢模組全是 WIP，而 WIP 是最低的一級 ⇒ 只有把要求也放到 WIP 才載入得起來
        var maturityOk = minMaturity <= BossModuleInfo.Maturity.WIP;
        var aiOn = AI.AIManager.Instance?.Beh != null;
        var (maturityKey, maturityFallback) = MaturityName(minMaturity);
        var maturityName = Loc.T(maturityKey, maturityFallback);

        ImGui.TextColored(PrereqNoteColor, Loc.T("DD_PrereqTitle", "Preconditions for everything on this page:"));

        ImGui.TextColored(maturityOk ? PrereqOkColor : PrereqBadColor, string.Format(
            maturityOk
                ? Loc.T("DD_PrereqMaturityOk", "1. Zone module maturity is set to \"{0}\" - the deep dungeon module can load.")
                : Loc.T("DD_PrereqMaturityBad", "1. Zone module maturity is set to \"{0}\", but the deep dungeon module is work-in-progress and will NOT load. Lower it in the \"Full duty automation\" tab; until then nothing on this page does anything and the minimap will not appear either."),
            maturityName));

        ImGui.TextColored(aiOn ? PrereqOkColor : PrereqBadColor,
            aiOn
                ? Loc.T("DD_PrereqAIOn", "2. BMR's AI is running.")
                : Loc.T("DD_PrereqAIOff", "2. BMR's AI is off - options that move your character will not do anything until you turn it on."));

        ImGui.TextColored(PrereqNoteColor, Loc.T("DD_PrereqNote", "Automatic mob targeting works without the AI. Trap avoidance, walking to coffers and walking to the Cairn of Passage all need it."));

        ImGui.Separator();
    }

    /// <summary>
    /// 成熟度的短名稱（loc key ＋ 英文 fallback）。
    /// </summary>
    /// <remarks>
    /// 列舉本身的 <c>PropertyDisplay</c> 是整句說明，塞進這一行太長，所以另外給短名稱。
    /// ⚠️ fallback 必須是真的英文字，<b>不能拿 key 自己當 fallback</b> ——
    /// 那樣譯文一旦缺漏，使用者看到的會是 <c>DD_MaturityWIP</c> 這種原始鍵名。
    /// </remarks>
    private static (string Key, string Fallback) MaturityName(BossModuleInfo.Maturity m) => m switch
    {
        BossModuleInfo.Maturity.WIP => ("DD_MaturityWIP", "work in progress"),
        BossModuleInfo.Maturity.Contributed => ("DD_MaturityContributed", "contributed"),
        _ => ("DD_MaturityVerified", "verified"),
    };

    public enum ClearBehavior
    {
        [PropertyDisplay("Do not auto target")]
        None,
        [PropertyDisplay("Stop when passage opens")]
        Passage,
        [PropertyDisplay("Target everything if not at level cap, otherwise stop when passage opens")]
        Leveling,
        [PropertyDisplay("Target everything")]
        All,
    }

    [PropertyDisplay("Enable module", tooltip: "WARNING: This feature is very experimental and most likely will contain bugs or unintended behavior.\nTo enable this feature in its current state, you must activate 'Work-in-Progress' maturity modules in the `Full Duty Automation` tab.")]
    public bool Enable = true;
    [PropertyDisplay("Enable minimap")]
    public bool EnableMinimap = true;
    [PropertyDisplay("Player marker size", tooltip: "Size of the arrow marking your own position on the minimap, relative to its original size. The arrow is 64px wide inside an 88px cell, which makes it spill over the room you are standing in; shrink it if it hides the coffer icons.")]
    [PropertySlider(0.4f, 1.5f, Speed = 0.01f)]
    public float PlayerMarkerScale = 0.7f;
    [PropertyDisplay("Try to avoid traps", tooltip: "Avoid known trap locations sourced from PalacePal data. Does not need PalacePal installed since data is included in BMR. (Traps revealed by a Pomander of Sight will always be avoided regardless of this setting.)")]
    public bool TrapHints = true;
    [PropertyDisplay("Automatically navigate to Cairn of Passage")]
    public bool AutoPassage = true;

    [PropertyDisplay("Automatic mob targeting behavior")]
    public ClearBehavior AutoClear = ClearBehavior.Leveling;

    // ⚠️ 舊文案是「暫停導航前可拉取的最大怪物數」，讀起來像是「戰鬥中會不會走位」，
    //    但它其實只管「要不要繼續趕往目標房間」——閃避與戰鬥走位永遠是開著的。
    [PropertyDisplay("Keep travelling until this many mobs have aggro (0 = stop travelling as soon as you are in combat)",
        tooltip: "Only controls travelling towards the destination room. Dodging and combat positioning are always active regardless of this value.\n\n0 means you stop heading for the room the moment anything aggros you. Higher values let you keep moving while that many mobs are already on you - useful for pulling several packs at once.")]
    [PropertySlider(0, 15)]
    public int MaxPull = 0;

    [PropertyDisplay("Stop travelling below this much HP (%)",
        tooltip: "Pauses travelling to the destination room while your HP is below this percentage, so you do not walk into the next pack at low health. Dodging and combat positioning are unaffected.\n\n0 disables this.")]
    [PropertySlider(0, 90)]
    public int StopTravelBelowHPPercent = 0;
    // ⚠️ 標籤上的「僅厄運迷宮」不是保守說法，是實測結果：這個旗標唯一的讀取點是
    //    AutoClear.AddLOS()，而 AddLOS() 全庫只有 EOFloorModule 呼叫（五處）。
    //    PalaceFloorModule 與 HoHFloorModule 零呼叫 ⇒ 在死者宮殿與天之逆焰是死鍵。
    [PropertyDisplay("Try to use terrain to LOS attacks (Eureka Orthos only)",
        tooltip: "Only has any effect in Eureka Orthos: it is the only deep dungeon whose floor module marks casts as line-of-sight-able. In Palace of the Dead and Heaven-on-High this setting does nothing at all.\n\nWhen it does apply, it replaces the simple \"stay out of a circle around the caster\" hint with one computed from the actual terrain, so you can break line of sight instead of just running away. Falls back to the simple circle if no obstacle map is available for the floor.")]
    public bool AutoLOS = false;

    [PropertyDisplay("Automatically navigate to coffers",
        tooltip: "Walking only. Whether a coffer you have reached is actually opened is the separate setting below.")]
    public bool AutoMoveTreasure = true;

    // 🔴 預設 true＝維持既有行為。這個開關是把原本綁在 AutoMoveTreasure 上的「開箱」語意拆出來的，
    //    不是新功能：拆分前 `AutoMoveTreasure` 關掉之後，只要人走到寶箱 3.5y 內仍然會自動開，
    //    因為判斷式是 `(AutoMoveTreasure && canNavigate) || 距離 < 3.5f`（&& 比 || 優先），
    //    與「自動移動至寶箱」這個標籤的字面意思不符。
    [PropertyDisplay("Automatically open coffers you have reached",
        tooltip: "Opens a coffer once you are next to it. This is separate from walking to it: with this on and \"automatically navigate to coffers\" off, nothing drags you anywhere, but a coffer you walked up to yourself still gets opened.\n\nNote this needs BMR's AI (or the Normal Movement autorotation module) to be running - it is what actually sends the interact.")]
    public bool AutoOpenTreasure = true;
    [PropertyDisplay("Prioritize opening coffers over Cairn of Passage")]
    public bool OpenChestsFirst = false;
    [PropertyDisplay("Open gold coffers")]
    public bool GoldCoffer = true;
    // ⚠️ tooltip 寫的是 OpenSilver（AutoClear.cs）真正的判斷式，不是概略描述：
    //    HP <= 70% 直接 false（爆炸銀箱打 70% 最大 HP）；武器+防具 < 198 才無條件 true；
    //    到 198 之後死者宮殿一律 false，天之逆焰／厄運迷宮則放寬成「樓層 >= 7」。
    [PropertyDisplay("Open silver coffers",
        tooltip: "Ticking this is necessary but not sufficient - silver coffers can explode for 70% of your max HP, so they are also skipped when:\n- your current HP is at or below 70% of maximum;\n- your weapon + armour levels add up to 198 or more, AND you are in Palace of the Dead (there is nothing left to gain there).\n\nIn Heaven-on-High and Eureka Orthos, once you are at 198 they are only opened from floor 7 onwards, where magicite/demiclones start dropping.")]
    public bool SilverCoffer = true;
    [PropertyDisplay("Open bronze coffers")]
    public bool BronzeCoffer = true;

    // 🔴 預設 true＝維持既有行為。埋藏的寶藏以前完全沒有閘門：候選判斷式裡它是
    //    `oid == (uint)OID.BandedCoffer`，後面沒有接任何 `&& Config.X`，
    //    所以銅銀金三個框全部關掉它照樣會去開。
    [PropertyDisplay("Open Accursed Hoard coffers",
        tooltip: "The banded coffers dug up from the Accursed Hoard. Until now these had no setting at all and were always handled, even with all three coffer types above unticked.\n\nAlso controls whether the glowing spot revealed by a Pomander of Intuition is walked to.")]
    public bool BandedCoffer = true;

    [PropertyDisplay("Manual \"walk to room\" button on the minimap (requires vnavmesh)",
        tooltip: "Adds a button under the minimap that walks you to the room you picked, in one go. You press it, it walks, and it stops on arrival - it never opens coffers, never uses the Cairn of Passage, and never starts the next leg by itself. The route does not avoid mobs or trap hints. Off by default.")]
    public bool ManualRoomWalk = false;

    // ── 風箏 ──────────────────────────────────────────────────────────
    // 📌 這幾個是「Deep Dungeon AI」自動循環模組的風箏參數。放在這一頁而不是模組的 track 選項，
    //    是因為 track 選項只吃列舉、給不了數值滑桿，而使用者要調的正是數值。
    //    預設值就是拆出來之前寫死的 9 / 25 / 0.05，拆分前後行為完全相同。
    [PropertyDisplay("Kite: stay at least this far from the target",
        tooltip: "The \"Deep Dungeon AI\" autorotation module's \"kite enemies\" option keeps ranged jobs and healers inside a ring around the target; this is the inner edge of that ring.\n\nMeasured hitbox to hitbox, so 0 would be touching.")]
    [PropertySlider(3f, 20f, Speed = 0.1f)]
    public float KiteMinDistance = 9f;

    [PropertyDisplay("Kite: but no further away than this",
        tooltip: "Outer edge of the kiting ring. Keep it inside your attack range, or you will kite yourself out of the fight.")]
    [PropertySlider(10f, 30f, Speed = 0.1f)]
    public float KiteMaxDistance = 25f;

    [PropertyDisplay("Kite: how strongly to prefer that ring",
        tooltip: "How much weight kiting gets in the AI's positioning. It competes with everything else the AI wants - dodging, positionals, following - and the default is deliberately small so that dodging always wins.\n\nRaise it if the character ignores kiting, but a large value will start fighting AOE avoidance.")]
    [PropertySlider(0.01f, 1f, Speed = 0.01f)]
    public float KiteWeight = 0.05f;

    // 🔴 預設 true。這個開關修的是一個靜默失效，完整說明見 AIHints.WantKiting。
    [PropertyDisplay("Kite: allow backing away while dodging",
        tooltip: "Whenever anything dangerous is telegraphed, the AI normally penalises any spot further from your target than where you stand now, so it does not drift away. That penalty is far stronger than the kiting preference, so without this, kiting silently does nothing while an AOE is up - which in a deep dungeon is most of the time.\n\nTicked, that one penalty is skipped while kiting is actually active. Dodging itself is completely unaffected - no dangerous spot ever becomes acceptable.\n\nUntick for the old behaviour.")]
    public bool KiteAllowRetreatWhileDodging = true;

    // 🔴 預設 false（opt-in）。BMR 的新設定欄位預設值會直接生效在既有使用者身上，
    //    而不是每個人都裝了 WrathCombo、也不是每個人都希望我們去動它。
    [PropertyDisplay("Pause WrathCombo's auto-rotation while in a deep dungeon",
        tooltip: "Uses WrathCombo's own lease mechanism to ask it to stop auto-rotating while you are inside a deep dungeon, so it does not fight BMR's rotation, and hands control back when you leave.\n\nReleasing the lease makes WrathCombo restore your settings itself, so nothing is left changed if the game or a plugin crashes.\n\nDoes nothing if WrathCombo is not installed.")]
    public bool SuspendWrathCombo = false;

    [PropertyDisplay("Reveal all rooms before proceeding to next floor")]
    public bool FullClear = false;
    [PropertyDisplay("Allow automatic pomander use")]
    public bool AllowPomander = false;
}
