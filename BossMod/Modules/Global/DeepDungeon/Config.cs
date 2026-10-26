using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;

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
    /// 🔑 兩個閘門都在別的地方，失敗形式都是「設定頁看起來一切正常、選項全部勾好，但什麼都不會發生」：
    /// <list type="number">
    /// <item><b>模組成熟度</b>：深牢的區域模組全部標成 <c>Maturity.WIP</c>，而 <c>ZoneModuleConfig.MinMaturity</c> 的預設值是 <c>Contributed</c>（比 WIP 高）⇒ <b>預設值下整個深牢模組根本不會被建立</b>，設定頁卻照樣完整渲染。</item>
    /// <item><b>BMR 的 AI</b>：這一頁大部分選項最後都是寫進 <c>AIHints</c> 的 <c>GoalZones</c>／<c>ForbiddenZones</c>，只有 AI 的導航在讀（例外是自動選怪，它走 <c>Hints.ForcedTarget</c>）。</item>
    /// </list>
    /// ⚠️ 這裡讓設定節點去讀 <c>AIManager</c> 的執行期狀態，是刻意接受的分層破壞：使用者要知道的是「我現在缺的是哪一個」。
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

        // 🔑 第三個閘門，而且是最容易讓人以為「設定壞了」的那一個：五個設定其實是被
        //    autorotation 模組讀走的，模組沒進 preset 就完全不會執行，而設定頁上看不出來。
        //    （模組本身還有一層：AIBehaviour 的 `Preset = target.Target != null ? … : null`
        //    ——沒有目標時整條管線是關的。）
        ImGui.TextColored(PrereqNoteColor, Loc.T("DD_PrereqRotationModule",
            "Note: the four \"Kite\" settings are consumed by the \"Deep Dungeon AI\" autorotation module, not by this module (so is \"also drink the deep dungeon's own potions\" while the potion advisor is off). They do nothing unless that module is part of your active preset with the matching track switched on - and that module only runs while you have a target."));

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

    // 📌 預設開：純顯示、零自動化，而且只在「偵測到怪」的格子寫一個數字——
    //    沒偵測到的格子完全不動，所以不會製造持續性的視覺噪音；
    //    通道石開啟後還會自動淡化（那時候找剩怪已經沒有價值了）。
    [PropertyDisplay("Show how many enemies are in each room on the minimap",
        tooltip: "Writes a small count in the corner of every room where enemies are currently detected. Mainly for finding the last few mobs while the Cairn of Passage has not unlocked yet - the count is highlighted while it is still locked, and fades once it opens.\n\nIMPORTANT: only enemies loaded around you exist as far as any plugin is concerned, so a room with no number is NOT necessarily clear - it may simply be too far away. Rooms are therefore never marked as empty.")]
    public bool ShowRoomEnemies = true;

    [PropertyDisplay("Try to avoid traps", tooltip: "Avoid known trap locations sourced from PalacePal data. Does not need PalacePal installed since data is included in BMR. (Traps revealed by a Pomander of Sight will always be avoided regardless of this setting.)")]
    public bool TrapHints = true;

    // 🔴 預設 true。純資料增益：拿不到就退回 BMR 內建的那份表，也就是今天的行為，
    //    所以預設開不可能讓任何人變差。合約版本對不上一樣整條停用。
    // 🔴 「PalacePal 記載過的埋藏寶藏」顯示已經拿掉，這個開關只剩陷阱資料一個作用，文案必須準確描述（不能再提埋藏寶藏）。
    //    ⚠️ BMR 的 Loc key 就是英文原文：改英文＝loc/tw.json 的鍵要一起搬。
    [PropertyDisplay("Also use PalacePal's own trap data when it is installed",
        tooltip: "BMR ships a snapshot of PalacePal's trap data, but PalacePal itself keeps learning new locations. With this on, whatever PalacePal currently knows for this deep dungeon is merged in on top of the built-in table (duplicates within 1y are dropped).\n\nIf PalacePal is not installed, is disabled, or speaks a version of the interface this build does not know, everything silently falls back to the built-in table - nothing breaks and nothing changes from before.\n\nTraps only. PalacePal's Accursed Hoard records are deliberately not drawn: that list covers all ten floors of the deep dungeon at once, so on a single floor's 5x5 map it lights up almost every room - that is noise, not information. PalacePal also draws its own hoard markers in the world already. The hoards BMR does show come from the objects the game itself places, and are unaffected by this setting.\n\nRead-only - BMR never writes anything back to PalacePal.")]
    public bool UsePalacePal = true;

    // 🔴 預設 true：使用者裁定社群回報的陷阱（內建表＝PalacePal 社群資料的快照，加上 PalacePal 現有的）也要確實閃避。
    //    關掉＝只有我方確認點用完整的圓，其餘只輕微繞開（AutoClear.RebuildTrapTiers）。
    //    ⚠️ BMR 的 Loc key 就是英文原文：改英文＝loc/tw.json 的鍵要一起搬。
    [PropertyDisplay("Avoid community-reported traps as firmly as confirmed ones",
        tooltip: "Community-reported traps are the built-in table (a snapshot of PalacePal's community data) plus whatever PalacePal currently knows for this deep dungeon. With this on, every one of them gets the same full avoidance circle as the traps you confirmed yourself (stepped on, or revealed with a Pomander of Sight).\n\nThe one exception is a community point within 3y of the Cairn of Passage, a treasure coffer or a buried-hoard light: it keeps the smaller circle, because a full circle there would cover the whole spot you need to reach and the route would walk straight onto the point instead.\n\nWith this off, only your own confirmed traps get the full circle; community points are only nudged around with a smaller circle and are ignored once you stand right next to one, so a route can still pass over them.\n\nThese lists cover every floor of the deep dungeon at once, so on any single floor many points have no trap at all; with this on, a narrow corridor may need a longer way round.\n\nOnly matters while 'Try to avoid traps' is on and the floor's traps have not been revealed by a Pomander of Sight.")]
    public bool CommunityTrapsHard = true;

    // ⚠️ BMR 的 Loc key 就是英文原文：改英文＝loc/tw.json 的鍵要一起搬。
    [PropertyDisplay("Stop avoiding a trap once it has gone off on this floor",
        tooltip: "A trap in a deep dungeon only goes off once per floor. With this on, as soon as one goes off (you or a party member walked into it), every trap marker within 2y of it - from the built-in table, PalacePal and your own confirmed traps - stops being avoided for the rest of this floor: the AI walks over that spot again, \"walk to room\" no longer refuses routes through it, and \"try the possible hoard spots\" no longer skips spots it covered.\n\nThe markers come back on the next floor. Nothing is saved. Traps you can actually see (for example after a Pomander of Sight) are not affected.\n\nWith this off, a trap that has already gone off keeps being avoided until you leave the floor, as before.")]
    public bool SkipTriggeredTraps = true;

    // 🔴 預設關（opt-in）。使用者已裁決過「不畫 PalacePal 來源的埋藏寶藏標記」；這裡接的是
    //    語意不同的新端點（只回當下看得到的實體），但要不要重新亮起來屬於新的裁決，不自己做。
    //    關著的時候 _palVisibleHoards 恆為空，行為與加這個開關之前逐字相同。
    [PropertyDisplay("Also show Accursed Hoard that only PalacePal can currently see",
        tooltip: "BMR already finds the Accursed Hoard itself, straight from the object the game places at the spot, and that is not affected by this setting. This only adds entities that PalacePal can see right now and BMR's own scan did not pick up; anything within 5y of something BMR already found is dropped, so while everything works normally you will see no difference at all.\n\nWhy it exists: BMR skips game objects without a valid network id, while PalacePal reads the object table unfiltered. Whether the unnamed buried-hoard object always carries one cannot be checked outside the game, so this is the fallback for the case where it does not.\n\nThis is NOT PalacePal's location database. That list covers all ten floors of the deep dungeon at once and lights up almost every room, and is still deliberately not drawn. Only what PalacePal can see on this floor, this run, is used, and only while its snapshot is less than a second old.\n\nNeeds 'show the buried Accursed Hoard' above, and does nothing unless PalacePal is installed and speaks its newer 'visible locations' interface. Off by default.\n\nRead-only - BMR never writes anything back to PalacePal.")]
    public bool UsePalacePalVisibleHoard = false;
    [PropertyDisplay("Automatically navigate to Cairn of Passage")]
    public bool AutoPassage = true;

    // 🔴 預設 false（opt-in），刻意的裁決：使用者要的只有「走到通道石旁邊」，「自動下一層」是多出來的一步，由他自己勾。
    // 📌 只加「最後那一下」，不加任何移動來源：走過去仍然是 AutoPassage 那兩個 GoalZone 的事，
    //    這個開關只在人已經站到互動距離內時才把傳送裝置設成互動目標。
    // ⚠️ 遊戲自己的「要前往下一層嗎」確認視窗<b>不</b>由 BMR 回答：BMR 沒有任何 SelectYesno／addon callback 的處理管線，
    //    不為了這個功能新增一條盲按。所以最終那一下「是」仍然是使用者按的，tooltip 必須講清楚。
    [PropertyDisplay("Also interact with the Cairn of Passage once you have reached it",
        tooltip: "Needs \"automatically navigate to Cairn of Passage\" above - this setting only adds the final click, it never walks you anywhere by itself.\n\nOff by default: navigating there walks you next to it and stops, leaving the decision to descend to you. With this ticked, once you are standing in interact range, out of combat and not transformed, the interact is sent for you.\n\nThe game then asks you to confirm in its own dialog, and BMR does NOT answer that dialog - you still press Yes yourself, so a floor is never left by accident.\n\nOnly fires while the Cairn is actually unlocked, and never while \"reveal all rooms before proceeding to next floor\" still has rooms left to visit. Coffers keep exactly the priority configured above.\n\nLike \"automatically open coffers you have reached\", this needs BMR's AI (or the Normal Movement autorotation module) to be running - it is what actually sends the interact.")]
    public bool AutoUsePassage = false;

    [PropertyDisplay("Automatic mob targeting behavior")]
    public ClearBehavior AutoClear = ClearBehavior.Leveling;

    // 🔴 預設 false（opt-in）。這是新行為，不是把既有行為拆開：在此之前「自動選怪」真的就只有
    //    選怪——被動怪的優先度是 -3，AI 的走向目標那條路徑選不到它，所以角色從不主動靠過去。
    // 📌 適用全職業：接近距離看職能（AutoClear.PullApproachRange），遠程／法系／治療停在 15y 開火而不是走到臉上。
    //    ⚠️ 欄位名維持 TankPull 不改：設定是照欄位名反序列化的，改名會讓既有使用者的設定值靜默歸零。
    [PropertyDisplay("Also walk up to the mob that was picked, to start the pull",
        tooltip: "\"Automatic mob targeting\" above only picks a target - it never moves you, so passive mobs are targeted and then ignored. With this on, your character also moves into range of that target and fires an opener to actually start the pull.\n\nTanks and melee walk into melee range; ranged, casters and healers stop about 15y away and open from there, so they are not sent into melee. Monk is the only job with no ranged opener, so it walks in.\n\nOnly while out of combat: it stops as soon as anything has aggro (see the pull limit below), while transformed, and while your HP is under the \"stop travelling below\" threshold. It never overrides trap avoidance or AOE dodging.\n\nOff by default; with it off nothing about targeting changes.")]
    public bool TankPull = false;

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

    // 🔴 預設 None＝這個功能整個關著。語意是「現在交給你走」：按住期間強制趕路，放開立刻停、控制權回到使用者手上。
    //    刻意是**按住**而不是切換：切換會留下「我以為關了其實還開著」的狀態。
    // ⚠️ 只解除**趕路**的三道閘門（戰鬥中／拉怪數上限／低血量），不碰任何「互動」開關（自動開箱、自動下一層各看自己的設定）：
    //    按住這顆鍵不會讓你意外掉到下一層。
    // ⚠️ 閃避與戰鬥走位完全不經過這裡（它們住在 AIHints 的 ForbiddenZones／AIBehaviour）。
    [PropertyDisplay("Hold this key to force travelling",
        tooltip: "While the key is held, travelling to the destination room, to coffers and to the Cairn of Passage runs regardless of combat, the pull limit and the low-HP threshold above. Let go and it stops immediately.\n\nThis only forces the walking. Whether a coffer is opened, or the Cairn is actually used, still follows their own settings - holding the key can never drop you to the next floor by accident.\n\nDodging and combat positioning are unaffected either way. Off by default.")]
    public ActionTweaksConfig.ModifierKey ForceTravelKey = ActionTweaksConfig.ModifierKey.None;

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

    // 🔴 純顯示，零自動化：這個開關<b>不</b>改變任何走位或開箱行為，只決定畫不畫標記。
    //    要不要自動走過去仍然是上面 BandedCoffer + AutoMoveTreasure 的事。
    // 📌 預設 true 是刻意的。這不是回退既有行為 —— 埋藏的寶藏在此之前<b>完全沒有任何顯示</b>，
    //    小地圖與世界疊加層都沒有，所以預設開不會蓋掉任何人原本看得到的東西。
    //    （BMR 的設定反序列化是遍歷 JSON 鍵，既有使用者的檔案裡沒有這個鍵就會吃到新預設。）
    [PropertyDisplay("Show the buried Accursed Hoard",
        tooltip: "Marks the Accursed Hoard on the minimap and on the ground in the world.\n\nThe buried hoard is completely invisible in game until a Pomander of Intuition reveals it, so without this there is nothing to walk to. The marker comes straight from the object the game itself places at the spot - nothing is guessed and no location database is used, so if the game does not place the object, nothing is drawn rather than something wrong.\n\nDisplay only: it never moves you and never opens anything. The marker disappears once the hoard has been dug up.")]
    public bool ShowAccursedHoard = true;

    // 新鍵：既有使用者的檔案裡沒有它，所以每個人都吃到這個預設（取代原本寫死的 5.5 秒）。
    [PropertyDisplay("Try the possible hoard spots: seconds to stand on each spot",
        tooltip: "Used by \"Try the possible hoard spots\" on the minimap: how long to keep standing on a spot before counting it as checked. Leaving the spot restarts the count.\n\nIn your own recordings the Accursed Hoard appeared about 2.6 to 3.5 seconds after stepping onto its spot (without a Pomander of Intuition); the default 4.5 adds about one second of margin. Lower it to save time, raise it if a hoard ever appears right after BMR has walked away from a spot.")]
    [PropertySlider(2.5f, 10f, Speed = 0.1f)]
    public float HoardWalkDwellSeconds = 4.5f;

    [PropertyDisplay("Manual \"walk to room\" button on the minimap (requires vnavmesh)",
        tooltip: "Adds a button under the minimap that walks you to the room you picked, in one go. You press it, it walks, and it stops on arrival - it never opens coffers, never uses the Cairn of Passage, and never starts the next leg by itself. The route does not avoid mobs.\n\nBefore anything moves, the whole route is checked against known trap locations - every stretch between corners, not just the corners themselves - and refused outright, with the reason shown under the minimap, if any part of it passes too close. It is never re-routed around a trap. Off by default.")]
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

    // ── 仇恨迴避 ──────────────────────────────────────────────────────
    // 🔴 預設 0（opt-in），理由跟 TankPull 一模一樣：這是新的**移動**行為，
    //    而 BMR 的設定反序列化是遍歷 JSON 鍵（跟 EzConfig 相反）⇒ 新預設**既有使用者拿得到**。
    //    沒實機驗過的移動改動不該自己開起來，尤其是疊在剛驗證過的陷阱迴避／風箏／開拉之上。
    [PropertyDisplay("Route around mobs that have not noticed you yet",
        tooltip: "While travelling to the destination room, or walking to a coffer, the exit or a revealed hoard spot, and out of combat, mobs that have not noticed you yet get their detection range marked as a no-go area for pathfinding, so the route actually bends around it instead of walking straight through.\n\nSight mobs use a 90-degree cone along the direction they are facing, so hugging a wall or slipping behind them is a valid route. Sound and proximity mobs use a full circle, and so does any mob not in the data table.\n\nIt never traps you: shapes you are already standing in, or that cover the coffer, exit or hoard spot you are walking to, are skipped, and if you stop making progress for a couple of seconds the whole thing switches off for 6 seconds so you walk through anyway. Off by default.")]
    public bool AggroAvoid = false;

    // 📌 這條滑條是**半徑**，不是權重。0 ＝整個功能關閉（AddAggroAvoidance 的第一道閘門就檢查它）。
    //    禁區走的是 PixelMaxG／PathLeeway 那條路，路徑成本不看目標區權重，所以滑條上限與趕路權重無關；
    //    上限 20 純粹是「想繞得更保守」的餘裕。
    [PropertyDisplay("Assumed aggro radius (yalms, added to the mob's hitbox)",
        tooltip: "How far a mob is assumed to notice you - the radius of the cone or circle above, measured from the mob plus its hitbox.\n\n10 is the value NecroLens uses for deep dungeon mobs. 0 disables the whole feature.")]
    [PropertySlider(0f, 20f, Speed = 0.5f)]
    public float AggroAvoidRadius = 10f;

    // 🔴 預設 true。這個開關修的是一個靜默失效，完整說明見 AIHints.WantKiting。
    [PropertyDisplay("Kite: allow backing away while dodging",
        tooltip: "Whenever anything dangerous is telegraphed, the AI normally penalises any spot further from your target than where you stand now, so it does not drift away. That penalty is far stronger than the kiting preference, so without this, kiting silently does nothing while an AOE is up - which in a deep dungeon is most of the time.\n\nTicked, that one penalty is skipped while kiting is actually active. Dodging itself is completely unaffected - no dangerous spot ever becomes acceptable.\n\nUntick for the old behaviour.")]
    public bool KiteAllowRetreatWhileDodging = true;

    // 預設開（使用者點名要的行為）；BMR 反序列化遍歷 JSON 鍵，既有使用者也拿得到這個預設。半徑沿用上面的 AggroAvoidRadius。
    [PropertyDisplay("Kite: keep clear of other mobs' aggro range",
        tooltip: "While backing away from your target, mobs that have not noticed you yet get their detection range (the same cone or circle as \"Route around mobs that have not noticed you yet\", using the radius above) marked as a no-go area, so kiting does not drag you into a fresh pull. Your current target and anything already fighting you are never avoided, so the route may cut right past your target when that is the only clean way out.\n\nIt never traps you: ranges you are already standing in are skipped, it steps aside while you are dodging an AOE, only the nearest mobs are kept when every way out is covered, and if you stop making progress for a couple of seconds it switches off for 6 seconds.")]
    public bool KiteAvoidAggro = true;

    // 預設開＝沿用加這個開關之前的行為。
    [PropertyDisplay("Kite: stop kiting enemies that hit you from out of melee range",
        tooltip: "If an enemy's auto-attacks keep landing on you while you are well out of melee range, backing away from it gains nothing, so kiting is switched off for that kind of enemy until you move to another area. Auto-attacks whose listed range is melee are never counted, however far away the hit looked.\n\nUntick to kite every enemy regardless. To undo the current judgements only, use the \"Turn kiting back on\" button at the bottom of this page.")]
    public bool KiteDetectRangedAttackers = true;

    // 🔴 預設 false（opt-in）。BMR 的新設定欄位預設值會直接生效在既有使用者身上，
    //    而不是每個人都裝了 WrathCombo、也不是每個人都希望我們去動它。
    [PropertyDisplay("Pause WrathCombo's auto-rotation while in a deep dungeon",
        tooltip: "Uses WrathCombo's own lease mechanism to ask it to stop auto-rotating while you are inside a deep dungeon, so it does not fight BMR's rotation, and hands control back when you leave.\n\nReleasing the lease makes WrathCombo restore your settings itself, so nothing is left changed if the game or a plugin crashes.\n\nDoes nothing if WrathCombo is not installed.")]
    public bool SuspendWrathCombo = false;

    /// <summary>
    /// 深牢裡改用的自動循環 preset 名稱；<b>空字串＝不切換</b>（預設）。
    /// </summary>
    /// <remarks>
    /// 📌 刻意<b>沒有</b> <c>[PropertyDisplay]</c>：設定頁的通用繪製對 string 只會給一個文字輸入框，
    /// 而要使用者手打 preset 名字必然會打錯，打錯的表現又是「靜默不切換」。
    /// 改由 <see cref="DrawCustom"/> 畫成從 preset 資料庫枚舉出來的下拉選單。
    /// ⚠️ 少了 <c>PropertyDisplay</c> 不影響存檔：序列化的條件只有「非 static 且沒有 JsonIgnore」。
    /// </remarks>
    public string DeepDungeonPreset = "";

    /// <summary>
    /// 深牢專用 preset 的下拉選單。
    /// </summary>
    /// <remarks>
    /// ⚠️ preset 清單只有執行期才拿得到，而且只能透過 <c>AIManager.Instance</c>
    /// （與 <see cref="DrawHeader"/> 讀 AI 狀態是同一個已被接受的分層破壞）。
    /// AI 尚未建立時清單是空的 —— 這時<b>絕對不能</b>把設定值清成空字串，
    /// 否則使用者只是打開設定頁看一眼就把設定弄丟了。
    /// </remarks>
    public override void DrawCustom(UITree tree, WorldState ws)
    {
        var presets = AI.AIManager.Instance?.Autorot.Database.Presets.AllPresets;
        var current = DeepDungeonPreset;
        var label = string.IsNullOrEmpty(current) ? Loc.T("DD_PresetNone", "(do not switch)") : current;

        ImGui.SetNextItemWidth(Math.Min(ImGui.GetWindowWidth() * 0.4f, 260f));
        using (var combo = ImRaii.Combo(Loc.T("DD_PresetSwitch", "Autorotation preset to use inside deep dungeons"), label))
        {
            if (combo)
            {
                if (ImGui.Selectable(Loc.T("DD_PresetNone", "(do not switch)"), string.IsNullOrEmpty(current)))
                {
                    DeepDungeonPreset = "";
                    Modified.Fire();
                }

                if (presets != null)
                {
                    var count = presets.Count;
                    for (var i = 0; i < count; ++i)
                    {
                        var name = presets[i].Name;
                        if (ImGui.Selectable(name, name == current))
                        {
                            DeepDungeonPreset = name;
                            Modified.Fire();
                        }
                    }
                }
            }
        }

        // 設定裡有名字、但清單裡找不到（改名或刪除）—— 要看得見，不然表現只是「沒有切換」
        if (!string.IsNullOrEmpty(current) && presets != null && !presets.Any(p => string.Equals(p.Name, current, BossMod.Autorotation.PresetDatabase.NameComparison)))
            ImGui.TextColored(PrereqBadColor, string.Format(
                Loc.T("DD_PresetMissing", "Preset \"{0}\" no longer exists - nothing will be switched. Pick another one."), current));
        else if (presets == null)
            ImGui.TextColored(PrereqNoteColor, Loc.T("DD_PresetListUnavailable", "The preset list is only available once the AI has been initialised; the stored setting is kept as-is."));

        ImGui.TextColored(PrereqNoteColor, Loc.T("DD_PresetHint", "Switching happens in memory only - your saved AI preset setting is never rewritten, so a crash cannot leave you stuck on the deep dungeon preset."));

        ImGui.Separator();
        DrawKiteReset(ws);

        ImGui.Separator();
        DrawPomanderAutoUseList();
        DrawPomanderTimedUse();
    }

    // 放在設定頁而不是區域模組視窗：那個視窗在頭目樓層不出現，而被判定的多半正是頭目。
    private void DrawKiteReset(WorldState ws)
    {
        if (!KiteDetectRangedAttackers)
        {
            ImGui.TextColored(PrereqNoteColor, Loc.T("DD_KiteResetDetectOff", "Automatic switch-off is turned off, so every enemy is kited."));
            return;
        }

        // 判定只在下一次深宮內換區時才清；出了深宮殘留的數字沒有意義，當成 0 顯示。
        var n = ws.DeepDungeon.DungeonId != 0 ? Autorotation.xan.DeepDungeonAI.ObservedRangedAttackerCount : 0;
        ImGui.TextColored(PrereqNoteColor, n > 0
            ? string.Format(Loc.T("DD_KiteResetCount", "Kiting is currently off for {0} kind(s) of enemy (seen hitting you from out of melee range)."), n)
            : Loc.T("DD_KiteResetNone", "Kiting is not currently switched off for any enemy."));
        if (n > 0 && ImGui.Button(Loc.T("DD_KiteResetButton", "Turn kiting back on")))
            Autorotation.xan.DeepDungeonAI.ClearObservedRangedAttackers();
    }

    [PropertyDisplay("Reveal all rooms before proceeding to next floor")]
    public bool FullClear = false;
    [PropertyDisplay("Allow automatic pomander use")]
    public bool AllowPomander = false;

    // 🔴 預設 true 是使用者明確要求的。這不是回退既有行為：沒有這條的話，銀寶箱在魔石滿的時候
    //    會被**永久跳過**，那顆寶箱就此不開。
    [PropertyDisplay("Use up a magicite / demiclone when a silver coffer is skipped because you are at cap",
        tooltip: "Silver coffers in Heaven-on-High and Eureka Orthos can contain magicite / demiclones. If you already hold three, the game refuses the coffer and it used to be skipped forever.\n\nWith this on, one of the stones you are holding is used so the coffer can be opened.\n\nWhich one is picked is not arbitrary: on a boss floor Ifrit / Titan / Garuda only deal damage, while Odin defeats the boss outright, so Odin is kept until it is the only thing left. In Eureka Orthos, Unei and Doga are spent first and the Onion Knight is kept the same way.\n\nOnly ever fires for a coffer the game has already told us contains one (via the \"you cannot carry any more\" message).")]
    public bool AutoUseMagiciteWhenCapped = true;

    #region 逐魔陶器的自動使用開關

    /// <summary>
    /// 金寶箱裡的魔陶器已經滿三個時，允許自動用掉哪一種來騰位置。索引＝<see cref="PomanderID"/>。
    /// </summary>
    /// <remarks>
    /// 🔴 <b>預設值就是原本那份寫死的 15 種白名單，再加上「用了無害或有益」的那幾種</b>，
    /// 所以既有使用者不會少掉任何原本會自動使用的魔陶器。
    /// <para>
    /// 預設<b>關</b>的分兩類：①變身類（蠍尾獅化／魅魔化／基路伯化／恐慌裝甲化）——變身期間
    /// 整個模組都被 <c>IsPlayerTransformed</c> 擋住；②會改變樓層狀態而且可能不是你要的
    /// （改變敵人＝下一層某間房變成模仿怪、形態變化、魔法效果解除＝連自己的增益一起消掉）。
    /// </para>
    /// <para>
    /// ⚠️ 索引 0（<see cref="PomanderID.None"/>）永遠不用。陣列長度會在
    /// <see cref="Deserialize"/> 正規化，舊設定檔比列舉短時補上新項目的預設值，
    /// 而不是讓 UI 或消費端讀到索引外例外。
    /// </para>
    /// </remarks>
    public bool[] PomanderAutoUse = BuildDefaultPomanderAutoUse();

    /// <summary>拿滿 3 個時依效果挑時機用掉一個（總開關）。與 <see cref="PomanderAutoUse"/> 是兩件事，互不遷移。</summary>
    public bool PomanderTimedUse = false;

    /// <summary>只在沒有隊友時生效。</summary>
    public bool PomanderTimedUseSoloOnly = true;

    /// <summary>逐種勾選，索引＝<see cref="PomanderID"/>；長度在 <see cref="Deserialize"/> 補齊。</summary>
    public bool[] PomanderTimedUseKinds = new bool[(int)PomanderID.Count];

    /// <summary>
    /// 預設允許自動使用的魔陶器。效果描述逐條讀 <c>DeepDungeonItem</c> 表判定
    /// （該表第 1..35 列與 <see cref="PomanderID"/> 1..35 逐項對齊，已查表確認）。
    /// </summary>
    private static readonly PomanderID[] DefaultAutoUsable = [
        // ── 原本寫死的 15 種（維持既有行為）──────────────────────────
        PomanderID.Steel,            // 強化防禦：自身受到的傷害減輕 40%
        PomanderID.Strength,         // 強化自身：傷害與治療量 +30%
        PomanderID.Sight,            // 全景：點亮本層地圖與陷阱
        PomanderID.Raising,          // 復生：復活第一個倒下的隊員
        PomanderID.Fortune,          // 運氣上升：本層擊敗敵人時寶箱出現率上升
        PomanderID.Concealment,      // 隱形：自身與小隊成員隱形
        PomanderID.Affluence,        // 寶箱增加：增加下一層的寶箱
        PomanderID.Frailty,          // 弱化敵人：本層敵人攻防低下
        PomanderID.ProtoSteel,
        PomanderID.ProtoStrength,
        PomanderID.ProtoSight,
        PomanderID.ProtoRaising,
        PomanderID.ProtoLethargy,    // 緩速：本層敵人緩速
        PomanderID.ProtoFortune,
        PomanderID.ProtoAffluence,
        // ── 新增：用了無害或有益 ─────────────────────────────────────
        PomanderID.Safety,           // 咒印解除：清除本層所有陷阱
        PomanderID.Flight,           // 減少敵人：減少下一層的敵人（與「寶箱增加」同形狀，原本就預設開）
        PomanderID.Purity,           // 解咒：解除自身的詛咒狀態
        PomanderID.Intuition,        // 感知寶藏：顯示埋藏的寶藏位置
        PomanderID.Petrification,    // 石化敵人：本層敵人石化（模組本來就會優先打石化目標）
        PomanderID.ProtoSafety,
        PomanderID.ProtoStorms,      // 大漩渦：本層敵人 HP 降至個位數
        PomanderID.ProtoFlight,
        PomanderID.ProtoPurity,
        PomanderID.ProtoIntuition,
        // ── revision 1 ───────────────────────────────────────────────
        // 魔法效果解除：本層沒有魔法效果時遊戲會拒絕——那條由 AutoClear 的
        // 重試上限接手（3 次沒生效就本層放棄），不會卡佇列。
        PomanderID.Serenity,
        PomanderID.ProtoSerenity,
    ];

    private static bool[] BuildDefaultPomanderAutoUse()
    {
        var res = new bool[(int)PomanderID.Count];
        foreach (var p in DefaultAutoUsable)
            res[(int)p] = true;
        return res;
    }

    /// <summary>某一種魔陶器可不可以被自動用掉。索引超出存檔範圍時退回預設值。</summary>
    public bool CanAutoUsePomander(PomanderID p)
    {
        var i = (int)p;
        return (uint)i < (uint)PomanderAutoUse.Length && PomanderAutoUse[i];
    }

    /// <summary>
    /// 白名單預設值的一次性遷移版本。
    /// </summary>
    /// <remarks>
    /// 🔴 既有使用者的 <see cref="PomanderAutoUse"/> 已整條陣列存進設定檔，
    /// 改 <see cref="DefaultAutoUsable"/> 只有全新安裝的人吃得到。要把新預設帶給
    /// 既有使用者只能做一次性遷移（本欄位記到哪一版）。使用者遷移後手動關掉的話，
    /// 下次存檔會連本欄位一起存，不會被重複翻回來。
    /// </remarks>
    public int PomanderAutoUseRevision;

    /// <summary>反序列化之後把 <see cref="PomanderAutoUse"/> 的長度補回列舉長度，並套用白名單的一次性遷移。</summary>
    /// <remarks>
    /// 🔴 沒有這一步的失敗形式是：以後 <see cref="PomanderID"/> 加了新項目，
    /// 舊設定檔存的陣列比列舉短，UI 一畫就 <c>IndexOutOfRangeException</c>，
    /// 而且只有真的打開深牢設定頁的人才會踩到。
    /// 缺的那幾格補<b>新的預設值</b>，不是補 false ——「沒存過」等於「還沒表態」。
    /// </remarks>
    public override void Deserialize(System.Text.Json.JsonElement j, System.Text.Json.JsonSerializerOptions ser)
    {
        base.Deserialize(j, ser);

        var want = (int)PomanderID.Count;
        if (PomanderAutoUse.Length != want)
        {
            var fixedUp = BuildDefaultPomanderAutoUse();
            var n = Math.Min(PomanderAutoUse.Length, want);
            Array.Copy(PomanderAutoUse, fixedUp, n);
            PomanderAutoUse = fixedUp;
        }
        if (PomanderTimedUseKinds.Length != want)
        {
            var fixedUp = new bool[want];
            Array.Copy(PomanderTimedUseKinds, fixedUp, Math.Min(PomanderTimedUseKinds.Length, want));
            PomanderTimedUseKinds = fixedUp;
        }

        // revision 1：魔法效果解除加入自動使用
        if (PomanderAutoUseRevision < 1)
        {
            PomanderAutoUseRevision = 1;
            if ((int)PomanderID.Serenity < PomanderAutoUse.Length)
                PomanderAutoUse[(int)PomanderID.Serenity] = true;
            if ((int)PomanderID.ProtoSerenity < PomanderAutoUse.Length)
                PomanderAutoUse[(int)PomanderID.ProtoSerenity] = true;
        }
    }

    /// <summary>
    /// 魔陶器的顯示名稱。
    /// </summary>
    /// <remarks>
    /// 🔑 直接讀客戶端的 <c>DeepDungeonItem</c> 表，<b>不走 loc 檔</b>——
    /// 這樣拿到的一定是該語系客戶端裡的官方名稱，不必維護 35 條翻譯，
    /// 也不會有譯名與遊戲內對不上的問題。查不到才退回列舉名。
    /// </remarks>
    private static string PomanderName(PomanderID p)
    {
        var name = Service.LuminaRow<Lumina.Excel.Sheets.DeepDungeonItem>((uint)p)?.Name.ToString();
        return string.IsNullOrEmpty(name) ? p.ToString() : name;
    }

    private static string PomanderTooltip(PomanderID p) => Service.LuminaRow<Lumina.Excel.Sheets.DeepDungeonItem>((uint)p)?.Tooltip.ToString() ?? "";

    /// <summary>
    /// 逐魔陶器的勾選清單。
    /// </summary>
    /// <remarks>
    /// 📌 分兩段是照遊戲自己的分類：1..19 是死者宮殿／天之逆焰的「魔陶器」，
    /// 20..35 是厄運迷宮的「魔科學器」。兩段都摺疊起來，因為 35 個核取方塊
    /// 攤開會把這一頁其他設定推到看不見的地方。
    /// </remarks>
    private void DrawPomanderAutoUseList()
    {
        if (!ImGui.CollapsingHeader(Loc.T("DD_PomanderAutoUseHeader", "Which pomanders may be used automatically to make room")))
            return;

        ImGui.TextColored(PrereqNoteColor, Loc.T("DD_PomanderAutoUseNote",
            "Only used when a gold coffer cannot be opened because you already hold three of what is inside. Names and descriptions come straight from the game's own data."));

        DrawPomanderRange(Loc.T("DD_PomanderGroupClassic", "Palace of the Dead / Heaven-on-High"), PomanderID.Safety, PomanderID.Petrification);
        DrawPomanderRange(Loc.T("DD_PomanderGroupProto", "Eureka Orthos"), PomanderID.ProtoLethargy, PomanderID.ProtoRaising);
    }

    private void DrawPomanderRange(string label, PomanderID first, PomanderID last)
    {
        ImGui.Separator();
        ImGui.TextColored(PrereqNoteColor, label);
        var col = 0;
        for (var p = first; p <= last; ++p)
        {
            var i = (int)p;
            if ((uint)i >= (uint)PomanderAutoUse.Length)
                continue;

            if (col++ % 2 == 1)
                ImGui.SameLine(ImGui.GetWindowWidth() * 0.5f);

            var v = PomanderAutoUse[i];
            if (ImGui.Checkbox($"{PomanderName(p)}###pomanderAuto{i}", ref v))
            {
                PomanderAutoUse[i] = v;
                Modified.Fire();
            }
            if (ImGui.IsItemHovered())
            {
                var tip = PomanderTooltip(p);
                if (tip.Length > 0)
                    ImGui.SetTooltip(tip);
            }
        }
    }

    private static string TimingLabel(PomanderTiming t) => t switch
    {
        PomanderTiming.BossFloorStart => Loc.T("DD_PomanderTimingBoss", "When you enter a boss floor"),
        PomanderTiming.FloorStart => Loc.T("DD_PomanderTimingFloor", "Right after entering a floor (Serenity: only when the floor has a harmful enchantment)"),
        PomanderTiming.ApproachPassage => Loc.T("DD_PomanderTimingPassage", "When \"walk to the Cairn of Passage\" starts walking there (the effect is on the next floor)"),
        PomanderTiming.BeforeFirstPull => Loc.T("DD_PomanderTimingPull", "Out of combat, before your first fight on the floor"),
        _ => "",
    };

    private void DrawPomanderTimedUse()
    {
        if (!ImGui.CollapsingHeader(Loc.T("DD_PomanderTimedHeader", "Use one at the right moment when you hold three")))
            return;

        ImGui.TextColored(PrereqNoteColor, Loc.T("DD_PomanderTimedNote",
            "When you already hold three of a ticked pomander, one is used at the moment that suits its effect, so the next one you find is not wasted. Needs \"Allow automatic pomander use\". Pauses for 10 seconds after you use a pomander yourself. Transformations, Alteration and a few others are not in the list and are never used."));

        var changed = ImGui.Checkbox(Loc.T("DD_PomanderTimedEnable", "Enable") + "###pomanderTimedEnable", ref PomanderTimedUse);
        changed |= ImGui.Checkbox(Loc.T("DD_PomanderTimedSoloOnly", "Only when you are not in a party") + "###pomanderTimedSolo", ref PomanderTimedUseSoloOnly);

        for (var t = PomanderTiming.FloorStart; t <= PomanderTiming.BeforeFirstPull; ++t)
        {
            ImGui.Separator();
            ImGui.TextColored(PrereqNoteColor, TimingLabel(t));
            var col = 0;
            for (var p = PomanderID.Safety; p < PomanderID.Count; ++p)
            {
                var i = (int)p;
                if (PomanderTimingRules.TimingOf(p) != t || (uint)i >= (uint)PomanderTimedUseKinds.Length)
                    continue;
                if (col++ % 2 == 1)
                    ImGui.SameLine(ImGui.GetWindowWidth() * 0.5f);
                changed |= ImGui.Checkbox($"{PomanderName(p)}###pomanderTimed{i}", ref PomanderTimedUseKinds[i]);
                if (ImGui.IsItemHovered())
                {
                    var tip = PomanderTooltip(p);
                    if (tip.Length > 0)
                        ImGui.SetTooltip(tip);
                }
            }
        }

        if (changed)
            Modified.Fire();
    }

    #endregion

    // 🔴 預設 false，而且這是**刻意回退上游的既有行為**（上游會在 HP 40%／60% 以下自動喝掉）。
    //    深牢專屬秘藥是特殊商店購入、不可出售的昂貴資源，使用者明確表示要自己決定何時用。
    //    「替使用者自動花掉他刻意保留的資源」屬於不該預設開啟的那一類。
    //    ⚠️ 不影響低血量的保命藥水——那條只用一般治療劑（頂級／聖級／上級），買得到、無爭議。
    [PropertyDisplay("Also drink the deep dungeon's own potions automatically",
        tooltip: "The deep-dungeon-only potions (Sustaining / Empyrean / Orthos) come from a special vendor, cannot be sold, and are usually saved for a moment you pick yourself - so they are NOT used automatically by default.\n\nTick this to let BMR drink them. With the potion advisor below switched on, it decides when (see its settings). With the advisor off, they are drunk in combat below 60% HP (40% for tanks) by the \"Deep Dungeon AI\" autorotation module, which is what it used to do.\n\nOrdinary Max / Hyper / Super Potions are a separate matter and do not need this.")]
    public bool AutoUseDeepDungeonPotion = false;

    // ── 喝藥判斷模組 ──────────────────────────────────────────────────
    // 📌 BMR 的設定反序列化只覆寫 JSON 裡有的鍵，既有使用者缺鍵時拿到這裡的初始值。
    // 預設開是使用者裁決（可推翻）；關掉＝逐字回到舊的兩段邏輯（30% 治療劑＋xan 的 60%／40% 秘藥）。
    [PropertyDisplay("Potion advisor: decide when to drink potions",
        tooltip: "One place decides both the ordinary healing potions and the deep dungeon's own potions, instead of two fixed HP thresholds.\n\nIn combat it estimates your HP 10 seconds ahead - from the damage you actually took over the last 10 seconds, or from how many enemies are attacking you, whichever is worse - and drinks the deep dungeon potion early when that estimate is low (only if \"also drink the deep dungeon's own potions\" is ticked). A healing potion is used below the threshold below, or when you are about to drop under 15% within 4 seconds, and it picks whichever potion in your bags heals the most at your current max HP.\n\nOut of combat it does not use healing potions by default. Below 50% HP, while low HP is what keeps you from travelling or opening a coffer, it drinks the deep dungeon potion instead.\n\nEvery potion it uses is written to the log, plus a one-line summary per floor.\n\nUntick for the old behaviour: a healing potion below 30% HP at any time, and the deep dungeon potion below 60% HP (tanks 40%) in combat.")]
    public bool PotionAdvisor = true;

    [PropertyDisplay("Potion advisor: in combat, drink the deep dungeon potion below this HP (%)",
        tooltip: "Drunk once your HP is below this and is not expected to climb back above it within 10 seconds. Not drunk while its regen effect is still on you.\n\nNeeds \"also drink the deep dungeon's own potions\".")]
    [PropertySlider(0, 100)]
    public int RegenPotionCombatHP = 75;

    [PropertyDisplay("Potion advisor: also drink it when HP is expected to fall below this within 10 seconds (%)",
        tooltip: "The estimate takes the larger of the damage you actually took over the last 10 seconds and the number of enemies attacking you times a typical damage per enemy for this deep dungeon and floor, then adds natural regen and whatever is left of a deep dungeon potion already ticking. This catches a big pull before your HP has dropped much.")]
    [PropertySlider(0, 100)]
    public int RegenPotionProjectedHP = 45;

    [PropertyDisplay("Potion advisor: use a healing potion below this HP (%)",
        tooltip: "Ordinary Max / Hyper / Super Potions. Also used when HP is expected to fall below 15% within 4 seconds.\n\nThe one that heals the most at your current max HP is picked: above about 9,600 max HP a Super Potion (HQ) heals more than a Max Potion (HQ).")]
    [PropertySlider(0, 100)]
    public int EmergencyPotionHP = 30;

    [PropertyDisplay("Potion advisor: also use healing potions out of combat",
        tooltip: "Off by default. Out of combat your HP comes back by 10% every 3 seconds on its own, and almost every out-of-combat healing potion in your recordings was drunk right after a landmine or an exploding coffer - while a fight often starts within seconds of a trap. Keeping the potion's cooldown for that fight is worth more.\n\nTick to use one out of combat as well, below the healing potion threshold above.")]
    public bool EmergencyPotionOutOfCombat = false;

    [PropertyDisplay("Potion advisor: out of combat, drink the deep dungeon potion when low HP is holding you up",
        tooltip: "Below 50% HP and out of combat, while low HP is the reason you are not travelling (\"stop travelling below this much HP\") or not opening a bronze or silver coffer, the deep dungeon potion is drunk so you can move on sooner - from 22% to 70% that is about 8 seconds instead of about 15.\n\nNeeds \"also drink the deep dungeon's own potions\".")]
    public bool RegenPotionWhenHPGateBlocks = true;

    [PropertyDisplay("Potion advisor: keep this many deep dungeon potions for manual use",
        tooltip: "The advisor does not drink the deep dungeon potion when you have this many or fewer left. 0 = it may use all of them.")]
    [PropertySlider(0, 20)]
    public int RegenPotionReserve = 0;
}
