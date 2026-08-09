using Dalamud.Interface.Utility.Raii;
using Dalamud.Bindings.ImGui;
using static FFXIVClientStructs.FFXIV.Client.Game.InstanceContent.InstanceContentDeepDungeon;

namespace BossMod.Global.DeepDungeon;

/// <param name="ConfirmedChests">
/// 每間房、每種寶箱型別「實際在 ObjectTable 裡看到幾個」，長度
/// <c>NumRooms * ChestTypeSlots</c>，索引 <c>房號 * ChestTypeSlots + 型別槽</c>。
/// <b>null＝這一層的房間座標校驗沒過</b>（硬編座標對不上，無法把實體歸屬到房間），
/// 此時全部寶箱一律畫成「地圖說有、位置未知」。
/// </param>
public sealed record class Minimap(DeepDungeonState State, Actor Player, int CurrentDestination, AutoDDConfig Config, int[]? ConfirmedChests)
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

    /// <summary>未知型別的槽位。</summary>
    public const int UnknownChestSlot = 3;

    // 標記用色。刻意不沿用 Colors.* —— 那些是使用者可調的語意色（陷阱、危險…），
    // 借來當「不知道」會在使用者改色之後變成謊話。
    // NecroLens 風格基準：有外框、不疊顏色。
    private const uint ColorConfirmedOutline = 0xFFFFFFFFu; // 白框＝已看到實體
    private const uint ColorUnknown = 0xFFB4B4B4u;          // 灰＝不知道（不要用警示色，這不是錯誤）
    private const uint ColorCount = 0xFFFFFFFFu;
    private const uint ColorCountShadow = 0xFF000000u;

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
            var chestTooltip = DrawChests(i, pos, chestCounts, bronzeTex, silverTex, goldTex);

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
    private string? DrawChests(int room, Vector2 pos, int[] chestCounts, Dalamud.Interface.Textures.TextureWraps.IDalamudTextureWrap bronzeTex, Dalamud.Interface.Textures.TextureWraps.IDalamudTextureWrap silverTex, Dalamud.Interface.Textures.TextureWraps.IDalamudTextureWrap goldTex)
    {
        Span<(int Slot, int Total, int Confirmed)> entries = stackalloc (int, int, int)[ChestTypeSlots];
        var count = 0;
        for (var s = 0; s < ChestTypeSlots; ++s)
        {
            var known = chestCounts[room * ChestTypeSlots + s];
            var confirmed = ConfirmedChests != null ? ConfirmedChests[room * ChestTypeSlots + s] : 0;
            // 實體看得到但遊戲清單沒列的也照畫：資訊只會多不會少，靜默丟掉才是問題
            var total = known > confirmed ? known : confirmed;
            if (total > 0)
                entries[count++] = (s, total, confirmed);
        }

        if (count == 0)
            return null;

        // 一格 88px。三種以內用 28px 一階（圖示 26px），出現未知型別而變成四種時縮到 21px 一階。
        var step = count > 3 ? 21f : 28f;
        var size = step - 2f;
        var dl = ImGui.GetWindowDrawList();
        var tips = new List<string>(count);

        for (var e = 0; e < count; ++e)
        {
            var (slot, total, confirmed) = entries[e];
            var located = confirmed >= total;

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
                var tex = slot == 0 ? bronzeTex : slot == 1 ? silverTex : goldTex;
                ImGui.Image(tex.Handle, new Vector2(size), default, new Vector2(1f),
                    located ? new Vector4(1f) : new Vector4(1f, 1f, 1f, 0.45f));
            }

            if (located)
                dl.AddRect(screen - new Vector2(1f), screen + new Vector2(size + 1f), ColorConfirmedOutline, 3f);
            else
                dl.AddText(screen + new Vector2(size - 8f, -4f), ColorUnknown, "?");

            if (total > 1)
            {
                // 帶一格陰影，免得白字落在金寶箱圖示上看不見
                var at = screen + new Vector2(size - 9f, size - 16f);
                dl.AddText(at + new Vector2(1f), ColorCountShadow, $"{total}");
                dl.AddText(at, ColorCount, $"{total}");
            }

            var name = slot switch
            {
                0 => Loc.T("DD_ChestBronze", "bronze coffer"),
                1 => Loc.T("DD_ChestSilver", "silver coffer"),
                2 => Loc.T("DD_ChestGold", "gold coffer"),
                _ => Loc.T("DD_ChestUnknownType", "coffer of an unrecognized type"),
            };
            tips.Add(located
                ? string.Format(Loc.T("DD_ChestLocated", "{0} x{1}: located"), name, total)
                : ConfirmedChests == null
                    ? string.Format(Loc.T("DD_ChestPositionUnavailable", "{0} x{1}: the map lists it, but the exact spot cannot be shown on this floor"), name, total)
                    : string.Format(Loc.T("DD_ChestNotSeenYet", "{0} x{1}: the map lists it, {2} located so far"), name, total, confirmed));
        }

        return string.Join("\n", tips);
    }

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
