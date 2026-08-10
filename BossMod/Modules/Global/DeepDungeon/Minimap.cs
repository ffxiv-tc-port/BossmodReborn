using Dalamud.Interface.Utility.Raii;
using Dalamud.Bindings.ImGui;
using static FFXIVClientStructs.FFXIV.Client.Game.InstanceContent.InstanceContentDeepDungeon;

namespace BossMod.Global.DeepDungeon;

/// <summary>已經在 <c>ObjectTable</c> 裡看到實體、而且問得出格內位置的一個寶箱。</summary>
/// <param name="Room">房號 0..24。</param>
/// <param name="Slot">寶箱型別槽，見 <see cref="Minimap.ChestSlot"/>。</param>
/// <param name="CellOffset">
/// 相對於<b>格子中心</b>的像素偏移，已經夾在格子範圍內。
/// 📌 換算在 <see cref="AutoClear"/> 那邊做完 —— Minimap 不碰世界座標，
/// 也就不必知道那份座標是哪個服的、有沒有過校驗。
/// </param>
public readonly record struct ChestSpot(int Room, int Slot, Vector2 CellOffset);

/// <summary>
/// 一個埋藏寶藏標記的把握程度。
/// </summary>
/// <remarks>
/// 📌 兩態的畫法照 PalacePal 的慣例走「空心→實心」而不是換顏色，
/// 這樣在不看圖例的情況下也讀得出哪一個比較「實」。
/// <para>
/// 🔴🔴 <b>不要再加回「PalacePal 資料庫記載」那一態。</b>
/// 2026-08-10 使用者裁決移除，原話：「埋藏寶藏 地圖不用放預測 你這不是每一格都畫了嗎」。
/// 成因：PalacePal 的寶藏資料庫是<b>整座深牢跨樓層的聯集</b>，套到單一樓層的 25 格小地圖上
/// 幾乎格格都會命中——那不是資訊而是噪音，還會把真正有實體的那幾格淹掉；
/// 而且 PalacePal 本身就會畫自己的世界標記，BMR 再畫一份是雙份。
/// </para>
/// <para>
/// 📌 剩下這兩態的來源都是<b>遊戲自己放在那裡的事件物件</b>，是真值不是預測，所以照舊。
/// PalacePal 的<b>陷阱</b>資料也照舊使用——那一份餵的是迴避決策，有真實價值。
/// </para>
/// </remarks>
public enum HoardKind
{
    /// <summary>遊戲把埋藏處的事件物件放在那裡了（還埋著，遊戲裡看不見）。</summary>
    Buried,
    /// <summary>已現形、可以直接互動的「埋藏的寶藏」。</summary>
    Revealed
}

/// <summary>小地圖上的一個埋藏寶藏標記。</summary>
/// <param name="Room">房號 0..24。</param>
/// <param name="CellOffset">相對於格子中心的像素偏移，換算方式與 <see cref="ChestSpot"/> 完全相同。</param>
/// <param name="Kind">把握程度，見 <see cref="HoardKind"/>。</param>
public readonly record struct HoardSpot(int Room, Vector2 CellOffset, HoardKind Kind);

/// <param name="ChestSpots">
/// 已找到實體位置的寶箱。<b>null＝這一層的房間座標校驗沒過</b>
/// （硬編座標對不上，無法把實體歸屬到房間），此時全部寶箱一律畫成「地圖說有、位置不明」。
/// </param>
/// <param name="RoomEnemies">
/// 每個房間目前偵測到幾隻活著的敵人；null＝功能關閉或本層無法判定。
/// 🔴 <b>元素是 0 的意思是「現在偵測不到」，不是「已經清空」</b>——遠處房間的怪根本不在
/// <c>ObjectTable</c> 裡。因此這裡只畫正向標記，沒有數字的格子<b>不做任何宣稱</b>。
/// </param>
/// <param name="HoardSpots">
/// 埋藏的寶藏。<b>null＝功能關閉、或這一層的房間座標校驗沒過</b>（後者由 <see cref="AutoClear"/>
/// 另外印一行說明，因為那時世界疊加層還是照畫）。
/// </param>
public sealed record class Minimap(DeepDungeonState State, Actor Player, int CurrentDestination, AutoDDConfig Config, IReadOnlyList<ChestSpot>? ChestSpots, IReadOnlyList<int>? RoomEnemies, IReadOnlyList<HoardSpot>? HoardSpots)
{
    enum IconID : uint
    {
        ReturnClosed = 60905,
        ReturnOpen = 60906,
        PassageClosed = 60907,
        PassageOpen = 60908,
        ChestBronze = 60911,
        ChestSilver = 60912,
        ChestGold = 60913,
    }

    /// <summary>寶箱型別的槽位數：0..2 對應遊戲的 ChestType 1..3（銅／銀／金），3 是未知型別。</summary>
    public const int ChestTypeSlots = 4;

    /// <summary>一個房間格子的邊長（像素）。</summary>
    public const float CellPixels = 88f;

    /// <summary>格子中心到邊緣的像素距離。</summary>
    public const float CellHalfPixels = CellPixels * 0.5f;

    /// <summary>格內真實點位用的小圖示邊長。</summary>
    private const float SpotIconSize = 18f;

    /// <summary>未知型別的槽位。</summary>
    public const int UnknownChestSlot = 3;

    // 標記用色。刻意不沿用 Colors.* —— 那些是使用者可調的語意色（陷阱、危險…），
    // 借來當「不知道」會在使用者改色之後變成謊話。
    // NecroLens 風格基準：有外框、不疊顏色。
    private const uint ColorConfirmedOutline = 0xFFFFFFFFu; // 白框＝已看到實體
    private const uint ColorUnknown = 0xFFB4B4B4u;          // 灰＝不知道（不要用警示色，這不是錯誤）
    private const uint ColorCount = 0xFFFFFFFFu;
    private const uint ColorCountShadow = 0xFF000000u;

    // 埋藏的寶藏。ABGR：R=0x30 G=0xE0 B=0xF0 ＝青色，與 AutoClear 的世界疊加層同一個值，
    // 也與 PalacePal 的埋藏寶藏預設色同一系 —— 三個地方看起來要是同一件事。
    private const uint ColorHoard = 0xFFF0E030u;

    /// <summary>埋藏寶藏菱形標記的半徑（像素）。刻意比寶箱圖示（18px 見方）小一點，避免搶掉寶箱。</summary>
    private const float HoardMarkerRadius = 7f;

    // 敵人數。ABGR：淡紅＝通道石還沒開（找剩下的怪最有價值時），灰＝已經開了（淡化避免噪音）。
    // 只用在文字上，不疊在格子底圖上 —— 維持 NecroLens 的「不疊顏色」語彙。
    private const uint ColorEnemyCount = 0xFF6E6EFFu;
    private const uint ColorEnemyCountDim = 0xFF9A9A9Au;

    /// <summary>
    /// 把遊戲的 <c>DeepDungeonChestInfo.ChestType</c> 轉成計數陣列的槽位。
    /// </summary>
    /// <remarks>
    /// ⚠️ 「型別 1／2／3 ＝ 銅／銀／金」是沿用本檔原本的假設（原碼寫 <c>1 &lt;&lt; (Type - 1)</c>）。
    /// 遊戲結構裡沒有列舉可對照，這個對應**沒有離線證據**。
    /// 落在 1..3 以外的值一律進未知槽並畫成問號，<b>不靜默丟掉</b>——
    /// 真的冒出新型別時要在格子上看得見，而不是少畫一個寶箱。
    /// </remarks>
    public static int ChestSlot(int type) => type >= 1 && type <= 3 ? type - 1 : UnknownChestSlot;

    /// <summary>最後一次 <see cref="Draw"/> 算出來的「地圖說這一格這一型別有幾個」；null＝還沒畫過。</summary>
    private int[]? _diagChestCounts;

    /// <summary>最後一次 <see cref="Draw"/> 算出來的「其中已經在 <c>ObjectTable</c> 看到實體的有幾個」。</summary>
    private int[]? _diagLocated;

    /// <summary>
    /// 寶箱繪製決策的內容簽章，供呼叫端節流用（值變了才值得再印一行 log）。
    /// </summary>
    /// <remarks>
    /// 🔴 刻意<b>不</b>把格內像素位置算進去——那個每幀都在動，混進來會讓節流失效變成每幀刷 log。
    /// </remarks>
    public ulong ChestDiagSignature { get; private set; }

    /// <summary>
    ///
    /// </summary>
    /// <returns>Integer index of the room the user clicked on.</returns>
    public int Draw()
    {
        var dest = -1;

        // 每間房、每種型別各有幾個寶箱。
        // 🔴 原本這裡是 `chests[room] |= (RoomChest)(1 << (type - 1))` —— 位元 OR 會把
        //    「同一間房兩個銅寶箱」壓成同一個位元，格子上永遠只看得到一個。改成計數。
        var chestCounts = new int[DeepDungeonState.NumRooms * ChestTypeSlots];
        var lenC = State.Chests.Length;

        for (var i = 0; i < lenC; ++i)
        {
            ref readonly var c = ref State.Chests[i];
            // 🔴 原本這裡是 `c.Room > 0`——**0 是合法房號**（房號＝5×row+col 的線性格號 0..24，
            //    離線反組譯確認過沒有旗標也沒有映射），所以左上角那一格的寶箱永遠不顯示。
            //    空槽是 {ChestType=0, RoomIndex=0xFF}，0xFF 走 WorldStateGameSync 的
            //    SanitizeDeepDungeonRoom 會變成 0 —— 擋掉空槽的是 `c.Type > 0`，不是房號。
            if (c.Room < DeepDungeonState.NumRooms && c.Type > 0)
                ++chestCounts[c.Room * ChestTypeSlots + ChestSlot(c.Type)];
        }

        // 已經找到實體位置的，逐間逐型別數一次；上排的摘要只畫「還沒找到」的那些
        var located = new int[DeepDungeonState.NumRooms * ChestTypeSlots];
        if (ChestSpots != null)
        {
            var lenS = ChestSpots.Count;
            for (var i = 0; i < lenS; ++i)
            {
                var s = ChestSpots[i];
                if ((uint)s.Room < DeepDungeonState.NumRooms && (uint)s.Slot < ChestTypeSlots)
                    ++located[s.Room * ChestTypeSlots + s.Slot];
            }
        }

        // ── 儀器：把「這一幀算出來要畫什麼」留給呼叫端寫進 log ──────────────────
        // 🔴 這裡交出去的是**下面繪製迴圈真的會讀的那兩個陣列本身**，不是另外重算一份。
        //    重算一份只能證明「重算的碼跟原碼一樣」，證明不了繪製端看到的是什麼。
        // 📌 這裡只算簽章、不組字串：Draw() 每幀都跑，字串由呼叫端在簽章變了時才組。
        _diagChestCounts = chestCounts;
        _diagLocated = located;
        ChestDiagSignature = ComputeChestDiagSignature(chestCounts, located);

        var lenP = State.Party.Length;
        DeepDungeonState.PartyMember player = default;
        for (var i = 0; i < lenP; ++i)
        {
            ref readonly var p = ref State.Party[i];
            if (p.EntityId == Player.InstanceID)
            {
                player = p;
                break;
            }
        }
        var playerCell = player.Room;

        using var _ = ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, new Vector2());

        var roomsTex = Service.Texture.GetFromGame("ui/uld/DeepDungeonNaviMap_Rooms_hr1.tex").GetWrapOrEmpty();
        var mapTex = Service.Texture.GetFromGame("ui/uld/DeepDungeonNaviMap_hr1.tex").GetWrapOrEmpty();
        var passageTex = Service.Texture.GetFromGameIcon(new((uint)(State.PassageActive ? IconID.PassageOpen : IconID.PassageClosed))).GetWrapOrEmpty();
        var returnTex = Service.Texture.GetFromGameIcon(new((uint)(State.ReturnActive ? IconID.ReturnOpen : IconID.ReturnClosed))).GetWrapOrEmpty();
        var bronzeTex = Service.Texture.GetFromGameIcon(new((uint)IconID.ChestBronze)).GetWrapOrEmpty();
        var silverTex = Service.Texture.GetFromGameIcon(new((uint)IconID.ChestSilver)).GetWrapOrEmpty();
        var goldTex = Service.Texture.GetFromGameIcon(new((uint)IconID.ChestGold)).GetWrapOrEmpty();

        for (var i = 0; i < 25; ++i)
        {
            var highlight = CurrentDestination > 0 && CurrentDestination == i;

            var isValidDestination = State.Rooms[i] > 0;

            using var _1 = ImRaii.PushId($"room{i}");

            var pos = ImGui.GetCursorPos();
            var tile = (byte)State.Rooms[i] & 0xF;
            var row = tile / 4;
            var col = tile & 3;

            var xoff = 0.0104f + col * 0.25f;
            var yoff = 0.0104f + row * 0.25f;
            var xoffend = xoff + 0.2292f;
            var yoffend = yoff + 0.2292f;

            // trim off 1px from each edge to account for extra space from highlight square
            // TODO there is probably a sensible primitive for this somewhere
            if (highlight)
            {
                xoff += 0.2292f / 88f;
                yoff += 0.2292f / 88f;
                xoffend -= 0.2292f / 88f;
                yoffend -= 0.2292f / 88f;
            }

            ImGui.SetCursorPos(pos);
            ImGui.Image(roomsTex.Handle, highlight ? new(86) : new(88), new Vector2(xoff, yoff), new Vector2(xoffend, yoffend), tile > 0 ? new(1f) : new(0.6f), highlight ? new(0, 0.6f, 0, 1) : default);

            if (i == playerCell)
            {
                isValidDestination = false;
                ImGui.SetCursorPos(pos + new Vector2(12, 12));
                ImGui.Image(mapTex.Handle, new Vector2(64, 64), new Vector2(0.2424f, 0.4571f), new Vector2(0.4848f, 0.6857f));
            }

            if (State.Rooms[i].HasFlag(RoomFlags.Passage))
            {
                ImGui.SetCursorPos(pos + new Vector2(28, 44));
                ImGui.Image(passageTex.Handle, new Vector2(32, 32));
            }

            if (State.Rooms[i].HasFlag(RoomFlags.Return))
            {
                ImGui.SetCursorPos(pos + new Vector2(28, 44));
                ImGui.Image(returnTex.Handle, new Vector2(32, 32));
            }

            // ── 寶箱 ──────────────────────────────────────────────────────
            // 兩態（照 PalacePal 的慣例）：
            //   半透明 ＋ 角標問號 ＝ 遊戲的寶箱清單說這間有，但實體還沒串流進來 → 不知道確切位置
            //   實心 ＋ 白外框     ＝ ObjectTable 裡真的看到實體了
            // 🔴「不知道」本身必須在格子上看得見，不能只寫進 tooltip——
            //    把不知道畫成跟知道一樣，比不畫還糟。
            var chestTooltip = DrawChests(i, pos, chestCounts, located, bronzeTex, silverTex, goldTex);

            // ── 埋藏的寶藏 ────────────────────────────────────────────────
            // 刻意不走寶箱那條管線：它不在遊戲的深牢寶箱清單裡，混進去會讓
            // 「地圖說有幾個」與「看到幾個」對不起來（同一個理由寫在 ChestSlotForOID 上）。
            // 形狀也刻意用菱形而不是另一個方形圖示 —— 銅銀金三個已經都是方的了。
            var hoardTooltip = DrawHoards(i, pos);

            // ── 房間裡的敵人數 ────────────────────────────────────────────
            // 🔴 只畫正向標記。沒有數字的格子**不代表清空了**（可能只是不在串流範圍內），
            //    所以絕不畫「0」，也不畫任何「這裡沒有」的記號 —— 那會是謊話。
            //    限制本身寫在小地圖下方的常駐說明裡。
            var enemies = RoomEnemies != null && i < RoomEnemies.Count ? RoomEnemies[i] : 0;
            if (enemies > 0)
            {
                // 左下角：上排是寶箱、中間是玩家箭頭與寶箱點位、(28,44) 是通道石／回歸點，
                // 左下是唯一還空著的角落
                ImGui.SetCursorPos(pos + new Vector2(4f, CellPixels - 20f));
                var at = ImGui.GetCursorScreenPos();
                // 通道石還沒開的時候找剩下的怪最有價值；開了之後淡化，避免全程視覺噪音
                var color = State.PassageActive ? ColorEnemyCountDim : ColorEnemyCount;
                var text = $"x{enemies}";
                var dl = ImGui.GetWindowDrawList();
                dl.AddText(at + new Vector2(1f), ColorCountShadow, text);
                dl.AddText(at, color, text);
            }

            if (i == playerCell)
            {
                ImGui.SetCursorPos(pos + new Vector2(44, 44));
                DrawPlayer(ImGui.GetCursorScreenPos(), Player.Rotation, mapTex.Handle, Config.PlayerMarkerScale);
            }

            ImGui.SetCursorPos(pos);
            ImGui.Dummy(new(88, 88));
            if (ImGui.IsItemHovered())
            {
                // tooltip 藏的是「為什麼／細節」，不是「有沒有問題」——
                // 有幾個寶箱、找到沒有，格子上已經看得見了，這裡補的是文字說明。
                var tip = chestTooltip;
                if (hoardTooltip != null)
                    tip = tip == null ? hoardTooltip : $"{tip}\n{hoardTooltip}";
                if (enemies > 0)
                {
                    var line = string.Format(Loc.T("DD_RoomEnemies", "{0} enemies detected here"), enemies);
                    tip = tip == null ? line : $"{line}\n{tip}";
                }
                if (isValidDestination)
                {
                    ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
                    var click = i == CurrentDestination
                        ? Loc.T("DD_ClickToClearDestination", "Click to clear destination")
                        : Loc.T("DD_ClickToSetDestination", "Click to set destination");
                    tip = tip == null ? click : $"{tip}\n{click}";
                }
                if (tip != null)
                    ImGui.SetTooltip(tip);
            }
            if (isValidDestination && ImGui.IsItemClicked())
                dest = i == CurrentDestination ? 0 : i;
            if (i % 5 < 4)
                ImGui.SameLine();
        }

        return dest;
    }

    /// <summary>
    /// 畫某一格的寶箱標示，回傳要接進該格 tooltip 的說明文字（沒有寶箱時回 null）。
    /// </summary>
    private string? DrawChests(int room, Vector2 pos, int[] chestCounts, int[] located, Dalamud.Interface.Textures.TextureWraps.IDalamudTextureWrap bronzeTex, Dalamud.Interface.Textures.TextureWraps.IDalamudTextureWrap silverTex, Dalamud.Interface.Textures.TextureWraps.IDalamudTextureWrap goldTex)
    {
        var dl = ImGui.GetWindowDrawList();
        List<string>? tips = null;

        // ① 已找到實體位置的：直接畫在格子裡的真實點位上
        if (ChestSpots != null)
        {
            var lenS = ChestSpots.Count;
            for (var i = 0; i < lenS; ++i)
            {
                var s = ChestSpots[i];
                if (s.Room != room)
                    continue;

                ImGui.SetCursorPos(pos + new Vector2(CellHalfPixels, CellHalfPixels) + s.CellOffset - new Vector2(SpotIconSize * 0.5f));
                var screen = ImGui.GetCursorScreenPos();
                if (s.Slot == UnknownChestSlot)
                    dl.AddText(screen + new Vector2(SpotIconSize * 0.25f, 0f), ColorUnknown, "?");
                else
                    ImGui.Image(SlotTexture(s.Slot, bronzeTex, silverTex, goldTex).Handle, new Vector2(SpotIconSize));
                // NecroLens 風格：有外框、不疊顏色
                dl.AddRect(screen - new Vector2(1f), screen + new Vector2(SpotIconSize + 1f), ColorConfirmedOutline, 3f);
            }

            // tooltip 走「每種型別一行」而不是「每個實體一行」——同房三個銅寶箱不必刷三行。
            // ⚠️ 也因為這樣，字串仍然是 {0} x{1} 兩個參數：格式字串的參數個數改了而譯文沒跟著改，
            //    `string.Format` 會在執行期擲 FormatException，而且只有進到深牢才會踩到。
            for (var s = 0; s < ChestTypeSlots; ++s)
            {
                var n = located[room * ChestTypeSlots + s];
                if (n > 0)
                    (tips ??= []).Add(string.Format(Loc.T("DD_ChestLocated", "{0} x{1}: located"), SlotName(s), n));
            }
        }

        // ② 地圖清單說有、但還沒找到實體的：上排摘要，半透明＋問號＋還沒找到的數量
        Span<(int Slot, int Total, int Located, int Remaining)> pending = stackalloc (int, int, int, int)[ChestTypeSlots];
        var count = 0;
        for (var s = 0; s < ChestTypeSlots; ++s)
        {
            var idx = room * ChestTypeSlots + s;
            var total = chestCounts[idx];
            var remaining = total - located[idx];
            if (remaining > 0)
                pending[count++] = (s, total, located[idx], remaining);
        }

        // 一格 88px。三種以內用 28px 一階（圖示 26px），出現未知型別而變成四種時縮到 21px 一階。
        var step = count > 3 ? 21f : 28f;
        var size = step - 2f;

        for (var e = 0; e < count; ++e)
        {
            var (slot, total, locatedCount, remaining) = pending[e];

            ImGui.SetCursorPos(pos + new Vector2(2f + e * step, 2f));
            var screen = ImGui.GetCursorScreenPos();

            if (slot == UnknownChestSlot)
            {
                // 沒有對應圖示的型別：畫框加問號。🔴 不要因為「不認得」就不畫。
                dl.AddRect(screen, screen + new Vector2(size), ColorUnknown, 3f);
                dl.AddText(screen + new Vector2(size * 0.28f, size * 0.1f), ColorUnknown, "?");
            }
            else
            {
                ImGui.Image(SlotTexture(slot, bronzeTex, silverTex, goldTex).Handle, new Vector2(size), default, new Vector2(1f),
                    new Vector4(1f, 1f, 1f, 0.45f));
            }

            // 🔴 位置不明的角標問號：這是「不知道」本身，一定要留在格子上，不能只寫進 tooltip
            dl.AddText(screen + new Vector2(size - 8f, -4f), ColorUnknown, "?");

            if (remaining > 1)
            {
                // 帶一格陰影，免得白字落在金寶箱圖示上看不見
                var at = screen + new Vector2(size - 9f, size - 16f);
                dl.AddText(at + new Vector2(1f), ColorCountShadow, $"{remaining}");
                dl.AddText(at, ColorCount, $"{remaining}");
            }

            (tips ??= []).Add(ChestSpots == null
                ? string.Format(Loc.T("DD_ChestPositionUnavailable", "{0} x{1}: the map lists it, but the exact spot cannot be shown on this floor"), SlotName(slot), total)
                : string.Format(Loc.T("DD_ChestNotSeenYet", "{0} x{1}: the map lists it, {2} located so far"), SlotName(slot), total, locatedCount));
        }

        return tips == null ? null : string.Join("\n", tips);
    }

    /// <summary>
    /// 畫某一格裡的埋藏寶藏，回傳要接進該格 tooltip 的說明文字（這一格沒有就回 null）。
    /// </summary>
    /// <remarks>
    /// 「已現形」畫實心、「還埋著」畫空心，兩者都來自遊戲自己放的事件物件。
    /// <para>
    /// 🔴 遊戲的深牢地圖資料本身<b>不含</b>埋藏寶藏的位置，所以「這一格到底有沒有」在沒有實體時
    /// 的正確表現就是<b>什麼都不畫</b>——不畫問號去暗示某一格有，也不拿別人玩過的紀錄去猜
    /// （為什麼不猜，見 <see cref="HoardKind"/> 上的裁決紀錄）。
    /// 唯一需要說出口的「不知道」是「偵測到了但放不上小地圖」，那一行由 <see cref="AutoClear"/> 印。
    /// </para>
    /// </remarks>
    private string? DrawHoards(int room, Vector2 pos)
    {
        if (HoardSpots == null)
            return null;

        var dl = ImGui.GetWindowDrawList();
        var best = (HoardKind?)null;
        var count = HoardSpots.Count;

        for (var i = 0; i < count; ++i)
        {
            var s = HoardSpots[i];
            if (s.Room != room)
                continue;

            // 與 DrawChests 同一套換算：先把游標移到格內的目標位置，再問螢幕座標。
            ImGui.SetCursorPos(pos + new Vector2(CellHalfPixels, CellHalfPixels) + s.CellOffset);
            var center = ImGui.GetCursorScreenPos();

            const float r = HoardMarkerRadius;

            // 菱形四角
            var top = new Vector2(center.X, center.Y - r);
            var right = new Vector2(center.X + r, center.Y);
            var bottom = new Vector2(center.X, center.Y + r);
            var left = new Vector2(center.X - r, center.Y);

            // NecroLens 風格：先深色外框再本體，不疊半透明色塊。
            dl.AddQuad(top, right, bottom, left, ColorCountShadow, 3f);
            if (s.Kind == HoardKind.Revealed)
                dl.AddQuadFilled(top, right, bottom, left, ColorHoard);
            else
                dl.AddQuad(top, right, bottom, left, ColorHoard, 1.6f);

            // 🔴 一格裡混著兩態時，tooltip 要講最有把握的那一個，不是第一個碰到的
            if (best == null || s.Kind > best)
                best = s.Kind;
        }

        return best switch
        {
            HoardKind.Revealed => Loc.T("DD_HoardRevealed", "Accursed Hoard: uncovered here, ready to be taken"),
            HoardKind.Buried => Loc.T("DD_HoardBuried", "Accursed Hoard: buried here (invisible in game until you dig it up)"),
            _ => null,
        };
    }

    private static Dalamud.Interface.Textures.TextureWraps.IDalamudTextureWrap SlotTexture(int slot, Dalamud.Interface.Textures.TextureWraps.IDalamudTextureWrap bronze, Dalamud.Interface.Textures.TextureWraps.IDalamudTextureWrap silver, Dalamud.Interface.Textures.TextureWraps.IDalamudTextureWrap gold)
        => slot == 0 ? bronze : slot == 1 ? silver : gold;

    private static string SlotName(int slot) => slot switch
    {
        0 => Loc.T("DD_ChestBronze", "bronze coffer"),
        1 => Loc.T("DD_ChestSilver", "silver coffer"),
        2 => Loc.T("DD_ChestGold", "gold coffer"),
        _ => Loc.T("DD_ChestUnknownType", "coffer of an unrecognized type"),
    };

    /// <summary>
    /// 把最後一次 <see cref="Draw"/> 的寶箱繪製決策組成一行診斷文字。
    /// </summary>
    /// <remarks>
    /// 🔴 這一行的用途是把「資料 → 繪製決策」的斷點釘死：
    /// log 說某一格要畫、使用者卻看不到 ⇒ 斷在<b>渲染側</b>（圖示沒載到、位置算歪、被別的東西蓋住…）；
    /// log 根本沒提到那一格 ⇒ 斷在<b>資料側</b>（房號歸屬、型別過濾…）。
    /// 兩邊都要列出來：<c>地圖 n 已定位 0</c> 是「上排摘要要畫」，<c>地圖 0 已定位 n</c> 是
    /// 「遊戲的寶箱清單沒列、但 <c>ObjectTable</c> 看得到實體」——後者今天完全沒有任何顯示能透露。
    /// <para>📌 診斷字串刻意<b>不</b>進在地化：它是寫給 log 的，不是介面文字。</para>
    /// </remarks>
    public string FormatChestDiagnostic()
    {
        var sb = new StringBuilder(192);
        sb.Append("[DD] 小地圖寶箱摘要 樓層 ").Append(State.Floor)
          .Append(" 版面 ").Append(State.Progress.Tileset)
          .Append(ChestSpots == null ? " 座標校驗未過（整層只畫摘要）：" : " 座標校驗通過：");

        var counts = _diagChestCounts;
        var located = _diagLocated;
        if (counts == null || located == null)
        {
            sb.Append("（還沒畫過任何一幀）");
            return sb.ToString();
        }

        var any = false;
        for (var room = 0; room < DeepDungeonState.NumRooms; ++room)
        {
            for (var s = 0; s < ChestTypeSlots; ++s)
            {
                var idx = room * ChestTypeSlots + s;
                var total = counts[idx];
                var loc = located[idx];
                if (total == 0 && loc == 0)
                    continue;
                if (any)
                    sb.Append('、');
                any = true;
                var pending = total - loc;
                sb.Append("cell").Append(room).Append('=').Append(SlotDiagName(s))
                  .Append(" 地圖").Append(total)
                  .Append(" 已定位").Append(loc)
                  .Append(" 摘要待畫").Append(pending > 0 ? pending : 0);
            }
        }
        if (!any)
            sb.Append("（遊戲的寶箱清單沒列任何東西，ObjectTable 也沒看到）");
        return sb.ToString();
    }

    /// <summary>
    /// 寶箱繪製決策的內容簽章（FNV-1a 64）。
    /// </summary>
    /// <remarks>
    /// 只涵蓋「哪一格哪一型別各幾個、其中幾個已定位」與樓層／版面／座標閘門狀態。
    /// 格內像素位置刻意不算進來，理由見 <see cref="ChestDiagSignature"/>。
    /// </remarks>
    private ulong ComputeChestDiagSignature(int[] chestCounts, int[] located)
    {
        var h = 14695981039346656037ul;
        h = MixHash(h, State.Floor);
        h = MixHash(h, State.Progress.Tileset);
        h = MixHash(h, ChestSpots == null ? 1u : 0u);
        var len = chestCounts.Length;
        for (var i = 0; i < len; ++i)
        {
            var total = chestCounts[i];
            var loc = located[i];
            if (total == 0 && loc == 0)
                continue;
            h = MixHash(MixHash(MixHash(h, (uint)i), (uint)total), (uint)loc);
        }
        return h;
    }

    private static ulong MixHash(ulong h, uint v) => (h ^ v) * 1099511628211ul;

    /// <summary>診斷用的型別名。刻意不走 <c>Loc.T</c>——這是 log 文字，不是介面文字。</summary>
    private static string SlotDiagName(int slot) => slot switch
    {
        0 => "銅",
        1 => "銀",
        2 => "金",
        _ => "未知型別",
    };

    /// <summary>
    /// 畫玩家所在位置的方向箭頭。
    /// </summary>
    /// <param name="scale">
    /// 箭頭縮放倍率。四個角一起乘，所以旋轉樞紐（<paramref name="center"/>，也就是格子中心）
    /// 不動——箭頭是往樞紐收縮，不是往左上角收縮。
    /// ⚠️ 原始四角是 (-32,-37.5)…(32,26.5)：寬高都是 64，但**垂直方向刻意偏移**了 5.5px
    /// （樞紐不在圖形的幾何中心）。整組等比例縮放才會保住這個偏移關係。
    /// </summary>
    /// <remarks>
    /// 📌 只縮箭頭。玩家所在房間底下那張 64px 底圖（`mapTex` 那次 <c>ImGui.Image</c>）是
    /// 「你在這一間」的房間標示，語意不同，不跟著縮。
    /// </remarks>
    private static void DrawPlayer(Vector2 center, Angle rotation, ImTextureID texHandle, float scale)
    {
        var cos = -rotation.Cos();
        var sin = rotation.Sin();
        ImGui.GetWindowDrawList().AddImageQuad(
            texHandle,
            center + Rotate(new(-32f * scale, -37.5f * scale), cos, sin),
            center + Rotate(new(32f * scale, -37.5f * scale), cos, sin),
            center + Rotate(new(32f * scale, 26.5f * scale), cos, sin),
            center + Rotate(new(-32f * scale, 26.5f * scale), cos, sin),
            new Vector2(0.0000f, 0.4571f),
            new Vector2(0.2424f, 0.4571f),
            new Vector2(0.2424f, 0.6857f),
            new Vector2(0.0000f, 0.6857f)
        );
    }

    private static Vector2 Rotate(Vector2 v, float cosA, float sinA) => new(v.X * cosA - v.Y * sinA, v.X * sinA + v.Y * cosA);
}
