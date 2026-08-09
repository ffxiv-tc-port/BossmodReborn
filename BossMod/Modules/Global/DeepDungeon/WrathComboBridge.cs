using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Ipc.Exceptions;

namespace BossMod.Global.DeepDungeon;

/// <summary>
/// 進深牢時請 WrathCombo 讓出自動循環，離開時還給它。
/// </summary>
/// <remarks>
/// <para>
/// 用的是 WrathCombo 官方的<b>租約（lease）</b>機制而不是直接改它的設定：租約是它為了
/// 「別的外掛暫時接管」設計的正式介面，釋放租約時它會<b>自己把設定還原</b>，
/// 所以我們不需要記住使用者原本的設定、也不會在崩潰後留下被改壞的設定。
/// AutoDuty 走的是同一條路。
/// </para>
/// <para>
/// 🔴 <b>軟依賴</b>：WrathCombo 沒安裝、IPC 還沒就緒、租約被使用者撤銷、版本對不上——
/// 全部都只是「這個功能不作用」，一律靜默跳過（首次記一行 Information），
/// 絕不擲例外、絕不影響深牢模組本身。
/// </para>
/// <para>
/// 📌 端點名稱是 ECommons EzIPC 的慣例 <c>「前綴.方法名」</c>，WrathCombo 的前綴就是
/// <c>WrathCombo</c>（<c>Services/IPC/Provider.cs</c> 的 <c>EzIPC.Init(output, prefix: "WrathCombo")</c>）。
/// </para>
/// </remarks>
sealed class WrathComboBridge : IDisposable
{
    // 給 WrathCombo 的使用者看的名字（會顯示在它的 UI 上「誰在控制我」）
    private const string DisplayName = "BossMod Reborn (deep dungeon)";

    /// <summary>取得租約失敗後，隔多久才再試一次。</summary>
    private static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(10d);

    private static ICallGateSubscriber<T>? Gate<T>(string name)
        => Service.PluginInterface?.GetIpcSubscriber<T>("WrathCombo." + name);

    private static readonly Lazy<ICallGateSubscriber<bool>?> IpcReady = new(() => Gate<bool>("IPCReady"));

    private static readonly Lazy<ICallGateSubscriber<string, string, Guid?>?> RegisterForLease =
        new(() => Service.PluginInterface?.GetIpcSubscriber<string, string, Guid?>("WrathCombo.RegisterForLease"));

    // 📌 WrathCombo 端回傳的是它自己的 SetResult 列舉，我們拿不到那個型別。
    //    Dalamud 的 CallGate 在型別對不上時會走 JSON 轉換（CallGateChannel.ConvertObject），
    //    列舉序列化成數字，所以宣告成 int 是安全的。我們也不看這個回傳值。
    private static readonly Lazy<ICallGateSubscriber<Guid, bool, int>?> SetAutoRotationState =
        new(() => Service.PluginInterface?.GetIpcSubscriber<Guid, bool, int>("WrathCombo.SetAutoRotationState"));

    // ReleaseControl 在 WrathCombo 端是 void ⇒ EzIPC 走 RegisterAction
    // ⇒ 訂閱型別是 ICallGateSubscriber<Guid, object> 且必須用 InvokeAction()。
    //    寫成 InvokeFunc() 會在執行期炸，編譯期看不出來。
    private static readonly Lazy<ICallGateSubscriber<Guid, object>?> ReleaseControl =
        new(() => Service.PluginInterface?.GetIpcSubscriber<Guid, object>("WrathCombo.ReleaseControl"));

    private Guid? _lease;
    private DateTime _nextAttempt = DateTime.MinValue;
    private bool _loggedUnavailable;
    private bool _releaseFailureLogged;

    /// <summary>我們現在是不是握著 WrathCombo 的租約（＝它的設定正被我們鎖住）。</summary>
    public bool Active => _lease != null;

    /// <summary>
    /// 確保目前的狀態與設定一致：要接管就拿租約，不要接管就還回去。
    /// </summary>
    /// <param name="want">現在是否應該壓住 WrathCombo 的自動循環。</param>
    /// <param name="now">目前時間（用 WorldState 的時間，跟其他節流一致）。</param>
    public void Update(bool want, DateTime now)
    {
        if (want == Active)
            return;

        if (want)
            TryAcquire(now);
        else
            ReleaseLease();
    }

    private void TryAcquire(DateTime now)
    {
        if (now < _nextAttempt)
            return;
        _nextAttempt = now + RetryInterval;

        try
        {
            // 先問 IPC 就緒沒有。沒安裝的話這裡就會丟 IpcNotReadyError，是預期路徑。
            if (IpcReady.Value?.InvokeFunc() != true)
                return;

            var internalName = Service.PluginInterface?.InternalName;
            if (string.IsNullOrEmpty(internalName))
                return;

            // 🔴 internalPluginName 必須是我們真正的內部名稱：WrathCombo 拿它檢查我們還在不在，
            //    寫錯的話它會以為我們已經卸載而自己撤銷租約。
            var lease = RegisterForLease.Value?.InvokeFunc(internalName, DisplayName);
            if (lease is not Guid id)
            {
                // 租約可能因為「同名租約已存在」「使用者剛撤銷過」「IPC 服務停用」而拿不到，
                // 這些都不是錯誤，等下一次重試即可。
                LogUnavailableOnce("WrathCombo 沒有核發租約（可能同名租約已存在、剛被撤銷，或它的 IPC 服務停用中）");
                return;
            }

            // 🔴 這一行之後，租約就已經登記在 WrathCombo 那邊了。從這裡開始無論發生什麼
            //    都**不可以**把 _lease 清掉——清掉等於把一個我們再也還不回去的租約留在對方身上，
            //    使用者的 WC 設定會一直鎖著，而且沒有任何恢復路徑（只能重載外掛）。
            _lease = id;
            SetAutoRotationState.Value?.InvokeFunc(id, false);
            Service.Logger.Information($"[DD] 已取得 WrathCombo 租約 {id}，深牢期間暫停它的自動循環。");
        }
        catch (IpcError)
        {
            // 沒安裝／介面對不上：功能靜默跳過
            LogUnavailableOnce("未偵測到 WrathCombo 或它的 IPC 介面不相容");
        }
        catch (Exception ex)
        {
            // WrathCombo 自己的處理常式炸掉不是 IpcError，照樣不能讓它冒到深牢模組。
            // ⚠️ 刻意不動 _lease：上面若已經拿到租約，它必須留著才有機會被釋放。
            Service.Logger.Information($"[DD] 與 WrathCombo 交接時發生非預期例外（已忽略）: {ex}");
        }
    }

    /// <summary>
    /// 把租約還回去。
    /// </summary>
    /// <remarks>
    /// 🔴 <b>只有在確定對方已經收下、或確定對方不在了，才可以放掉本地的 GUID。</b>
    /// 原本的寫法是「不管成不成功先清掉」，那會在一次暫時性失敗之後就永遠失去這個 handle，
    /// 租約卻還留在 WrathCombo 那邊 —— 使用者看到的就是「BMR 把我的設定鎖住了」
    /// 而且關掉開關也解不開。現在失敗就留著，下一幀 <see cref="Update"/> 會再試。
    /// </remarks>
    private void ReleaseLease()
    {
        if (_lease is not Guid id)
            return;

        try
        {
            // ⚠️ 對面是 RegisterAction ⇒ 必須 InvokeAction
            ReleaseControl.Value?.InvokeAction(id);
            _lease = null;
            _releaseFailureLogged = false;
            Service.Logger.Information($"[DD] 已釋放 WrathCombo 租約 {id}，它的自動循環設定會自行還原。");
        }
        catch (IpcError)
        {
            // WrathCombo 已經不在了：它卸載時本來就會作廢所有租約並還原設定，
            // 對方都沒了，本地放手是安全的（而且必須放手，否則會一直重試）。
            _lease = null;
            _releaseFailureLogged = false;
        }
        catch (Exception ex)
        {
            // 對方還在、但這次呼叫炸了 ⇒ 租約仍然有效，**不能**放手，下一幀再試。
            if (!_releaseFailureLogged)
            {
                _releaseFailureLogged = true;
                Service.Logger.Information($"[DD] 釋放 WrathCombo 租約失敗，將持續重試（租約仍在對方手上）: {ex}");
            }
        }
    }

    private void LogUnavailableOnce(string reason)
    {
        if (_loggedUnavailable)
            return;
        _loggedUnavailable = true;
        Service.Logger.Information($"[DD] 不與 WrathCombo 交接：{reason}。深牢模組其餘功能不受影響。");
    }

    /// <summary>
    /// 最後一次機會把租約還回去。
    /// </summary>
    /// <remarks>
    /// ⚠️ 這裡失敗就沒有下一幀可以重試了，所以要說出來 —— 使用者需要知道
    /// 「WC 的設定還鎖著」是因為交接沒收乾淨，而不是他自己設錯。
    /// 📌 WrathCombo 會自行檢查登記者是否還載入著，外掛整個卸載時它會作廢租約，
    /// 所以最壞情況也只是撐到下次重載。
    /// </remarks>
    public void Dispose()
    {
        ReleaseLease();
        if (Active)
            Service.Logger.Information("[DD] 模組卸載時仍未能釋放 WrathCombo 租約；重載外掛或重啟遊戲後它會自行作廢。");
    }
}
