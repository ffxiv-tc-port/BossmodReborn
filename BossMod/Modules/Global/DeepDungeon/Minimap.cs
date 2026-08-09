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

/// <param name="ChestSpots">
/// 已找到實體位置的寶箱。<b>null＝這一層的房間座標校驗沒過</b>
/// （硬編座標對不上，無法把實體歸屬到房間），此時全部寶箱一律畫成「地圖說有、位置不明」。
/// </param>
/// <param name="RoomEnemies">
/// 每個房間目前偵測到幾隻活著的敵人；null＝功能關閉或本層無法判定。
/// 🔴 <b>元素是 0 的意思是「現在偵測不到」，不是「已經清空」</b>——遠處房間的怪根本不在
/// <c>ObjectTable</c> 裡。因此這裡只畫正向標記，沒有數字的格子<b>不做任何宣稱</b>。
/// </param>
public sealed record class Minimap(DeepDungeonState State, Actor Player, int CurrentDestination, AutoDDConfig Config, IReadOnlyList<ChestSpot>? ChestSpots, IReadOnlyList<int>? RoomEnemies)
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
            if (c.Room > 0 && c.Room < DeepDungeonState.NumRooms && c.Type > 0)
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
