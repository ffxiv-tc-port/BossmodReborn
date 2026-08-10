using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Ipc.Exceptions;

namespace BossMod.Global.DeepDungeon;

/// <summary>
/// 對 PalacePal 的唯讀 IPC 包裝：拿它累積的陷阱與埋藏寶藏座標，補充 BMR 內建的那份表。
/// </summary>
/// <remarks>
/// <para>
/// 形狀刻意與 <see cref="DeepDungeonNav"/> 一致：每次呼叫都即時探測、失敗一律回「不可用」、
/// <b>不快取「可用」狀態</b>（使用者中途裝上／停用外掛都要能反應），
/// <c>IpcError</c> 安靜處理，其他例外記 <c>Information</c> 之後吞掉。
/// </para>
/// <para>
/// 🔴 <b>紅線</b>：唯讀。這裡不回寫任何東西給 PalacePal、不碰記憶體、不碰封包。
/// </para>
/// <para>
/// 🔴 <b>合約是雙方逐字約定的，不可以自己改名</b>：
/// <c>PalacePal.ApiVersion</c>（() → int，目前必須是 1）、
/// <c>PalacePal.GetTrapLocations</c>（(ushort territoryType) → List&lt;Vector3&gt;）、
/// <c>PalacePal.GetHoardLocations</c>（同上）。
/// 版本不是 1 就整條停用——寧可退回內建表，也不要照著一份語意可能已經變掉的資料走路。
/// </para>
/// <para>
/// ⚠️ <b>座標是「這個區域曾經出現過」的聯集，不是「這一層現在有」。</b>
/// 深牢一個 territory 含 10 層，而各層是用同一組版面在同一組世界座標上拼出來的，
/// 所以 PalacePal 的清單與 BMR 內建的 <see cref="GeneratedTrapData"/> 一樣是跨層聯集。
/// 陷阱照這個語意用是對的（內建表本來就是這樣用）；
/// <b>寶藏就必須標成「資料庫記載」而不是「這裡有」</b>。
/// </para>
/// </remarks>
static class PalacePalIpc
{
    /// <summary>本端支援的合約版本。對方回別的值就整條停用。</summary>
    public const int SupportedApiVersion = 1;

    private static void LogUnexpected(string endpoint, Exception ex)
        => Service.Logger.Information($"[DD pal] PalacePal.{endpoint} 擲出非 IPC 例外（已忽略，不影響 BMR）: {ex}");

    private static readonly Lazy<ICallGateSubscriber<int>?> ApiVersion =
        new(() => Service.PluginInterface?.GetIpcSubscriber<int>("PalacePal.ApiVersion"));

    private static readonly Lazy<ICallGateSubscriber<ushort, List<Vector3>>?> TrapLocations =
        new(() => Service.PluginInterface?.GetIpcSubscriber<ushort, List<Vector3>>("PalacePal.GetTrapLocations"));

    private static readonly Lazy<ICallGateSubscriber<ushort, List<Vector3>>?> HoardLocations =
        new(() => Service.PluginInterface?.GetIpcSubscriber<ushort, List<Vector3>>("PalacePal.GetHoardLocations"));

    /// <summary>
    /// 對方在不在、而且說得出我們認得的合約版本。
    /// </summary>
    /// <remarks>
    /// ⚠️ 這個結果<b>不快取</b>。呼叫端要自己節流（見 <c>AutoClear</c> 的重整間隔），
    /// 別在每幀路徑上呼叫——沒安裝時 <c>InvokeFunc</c> 是靠擲例外回報的。
    /// </remarks>
    public static bool IsAvailable()
    {
        try
        {
            if (ApiVersion.Value is not { } g)
                return false;
            return g.InvokeFunc() == SupportedApiVersion;
        }
        catch (IpcError)
        {
            return false;
        }
        catch (Exception ex)
        {
            LogUnexpected("ApiVersion", ex);
            return false;
        }
    }

    /// <summary>這個區域已知的陷阱座標；null＝拿不到（沒裝／版本不合／對方出錯）。</summary>
    public static List<Vector3>? GetTraps(ushort territory) => Fetch(TrapLocations, "GetTrapLocations", territory);

    /// <summary>這個區域已知的埋藏寶藏座標；null＝拿不到。</summary>
    public static List<Vector3>? GetHoards(ushort territory) => Fetch(HoardLocations, "GetHoardLocations", territory);

    /// <remarks>
    /// 🔴 <b>先問版本再取資料</b>，而不是「取到東西就用」。端點名稱可能被別的外掛佔用，
    /// 也可能是舊版 PalacePal 用同名端點回傳不同語意的東西——那種情況下拿到的是
    /// 一份長得很正常但意義不同的座標清單，失敗形式是安靜地畫錯／閃錯地方。
    /// </remarks>
    private static List<Vector3>? Fetch(Lazy<ICallGateSubscriber<ushort, List<Vector3>>?> gate, string endpoint, ushort territory)
    {
        if (!IsAvailable())
            return null;

        try
        {
            return gate.Value?.InvokeFunc(territory);
        }
        catch (IpcError)
        {
            return null;
        }
        catch (Exception ex)
        {
            LogUnexpected(endpoint, ex);
            return null;
        }
    }
}
