using Dalamud.Game.Config;
using Dalamud.Plugin;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.System.Input;
using FFXIVClientStructs.FFXIV.Client.UI;
using Dalamud.Bindings.ImGui;

namespace BossMod;

[StructLayout(LayoutKind.Explicit, Size = 0x18)]
public unsafe struct PlayerMoveControllerFlyInput
{
    [FieldOffset(0x0)] public float Forward;
    [FieldOffset(0x4)] public float Left;
    [FieldOffset(0x8)] public float Up;
    [FieldOffset(0xC)] public float Turn;
    [FieldOffset(0x10)] public float u10;
    [FieldOffset(0x14)] public byte DirMode;
    [FieldOffset(0x15)] public byte HaveBackwardOrStrafe;
}

public sealed unsafe class MovementOverride : IDisposable
{
    public Vector3? DesiredDirection;
    public Angle MisdirectionThreshold;
    public Angle? DesiredSpinDirection;

    public WDir UserMove; // unfiltered movement direction, as read from input
    public WDir ActualMove; // actual movement direction, as of last input read

    private readonly IDalamudPluginInterface _dalamud;
    private readonly ActionTweaksConfig _tweaksConfig = Service.Config.Get<ActionTweaksConfig>();
    private bool? _forcedControlState;
    public bool LegacyMode;
    private bool[]? _navmeshPathIsRunning;
    public static MovementOverride? Instance;

    public bool IsMoving() => ActualMove != default;
    public bool IsMoveRequested() => UserMove != default;

    /// <summary>
    /// <see cref="ActionTweaksConfig.ModifierKey"/> 這一顆「按住不放」的鍵現在有沒有被按著。
    /// </summary>
    /// <remarks>
    /// 契約：主執行緒上任何時點都能呼叫，<b>不限 Draw 回呼</b>。修飾鍵讀的是 <c>ImGui.GetIO()</c> 的一般欄位；
    /// ImGui context 由 Dalamud 建立場景時建一次、Dalamud 卸載時才銷毀，本插件的載入與卸載都落在那之間。
    /// 值是 ImGui 上一次開新幀時的鍵盤狀態 ⇒ 在 Draw 回呼裡讀，與下一次遊戲讀輸入時
    /// （<c>MovementBlocked</c> getter 經 <see cref="IsForceUnblocked"/> 在 <see cref="RMIWalkDetour"/> 裡讀）拿到的是同一份。
    /// 📌 抽成 static 是為了讓滑鼠雙鍵那一條（唯一需要 unsafe 的部分）只有一份實作。
    /// </remarks>
    public static bool IsModifierHeld(ActionTweaksConfig.ModifierKey key) => key switch
    {
        ActionTweaksConfig.ModifierKey.Ctrl => ImGui.GetIO().KeyCtrl,
        ActionTweaksConfig.ModifierKey.Alt => ImGui.GetIO().KeyAlt,
        ActionTweaksConfig.ModifierKey.Shift => ImGui.GetIO().KeyShift,
        // UIInputData.Instance() 是手寫包裝（UIModule 未建立時回 null），不是 [StaticAddress] 產生碼——判空回「沒按住」。
        ActionTweaksConfig.ModifierKey.M12 => UIInputData.Instance() is var input && input != null && input->UIFilteredCursorInputs.MouseButtonHeldFlags.HasFlag(MouseButtonFlags.LBUTTON | MouseButtonFlags.RBUTTON),
        _ => false,
    };

    public bool IsForceUnblocked() => IsModifierHeld(_tweaksConfig.MoveEscapeHatch);

    /// <summary>
    /// 這一幀使用者有沒有按著「暫停自動移動」那顆鍵（<see cref="ActionTweaksConfig.PauseAutoMoveKey"/>）。
    /// </summary>
    /// <remarks>
    /// 每幀快照：由 <c>Plugin.DrawUI</c> 呼叫 <see cref="UpdateAutoMovementPause"/> 拍一次；讀取者之一
    /// <see cref="DirectionToDestination"/> 住在 <see cref="RMIWalkDetour"/>／<see cref="RMIFlyDetour"/> 裡，只讀這個 bool。
    /// 刻意不改成在 detour 裡即時查 <see cref="IsModifierHeld"/>：那樣讀也安全、拿到的也是同一幀的鍵盤狀態，
    /// 但快照讓「翻轉時記一行 log」只有一個出處，detour 裡也少一次原生呼叫。
    /// </remarks>
    public bool AutoMovementPaused { get; private set; }

    /// <summary>上一次記過的暫停狀態；用來只在<b>翻轉</b>時記一行 log。</summary>
    private bool _loggedAutoMovementPaused;
    /// <summary>「暫停自動移動」的長說明印過了沒；印過之後只留短行。</summary>
    private bool _explainedAutoMovementPause;

    /// <summary>
    /// 拍下這一幀的「暫停自動移動」鍵狀態。
    /// </summary>
    /// <remarks>
    /// 由 <c>Plugin.DrawUI</c> 每幀呼叫一次，是 <see cref="AutoMovementPaused"/> 唯一的寫入點。
    /// 📌 log 走 <c>Debug</c>：用途是讓「我按著 Alt 它怎麼還在自己走」與「該閃卻站著不動」這種回報分辨「鍵根本沒被認到」
    /// 與「認到了但走的人不是 BMR」（例如 vnavmesh 在走）；這顆鍵常兼任別的熱鍵，平時不該佔一般 log。
    /// 只在翻轉時印——這支每幀都會被呼叫到。放開那一行附按住的秒數。
    /// </remarks>
    public void UpdateAutoMovementPause()
    {
        var key = _tweaksConfig.PauseAutoMoveKey;
        AutoMovementPaused = key != ActionTweaksConfig.ModifierKey.None && IsModifierHeld(key);
        if (AutoMovementPaused == _loggedAutoMovementPaused)
            return;
        _loggedAutoMovementPaused = AutoMovementPaused;
        if (AutoMovementPaused)
        {
            _autoMovementPausedSinceMs = Environment.TickCount64;
            // 長說明只印第一次；翻轉本身仍然每次都印，那才是要保留的訊號。
            Service.Logger.Debug(_explainedAutoMovementPause ? $"[MovementOverride] 暫停自動移動:啟動（{key}）" : $"[MovementOverride] 暫停自動移動:啟動（{key}）——這段期間 BMR 完全不注入移動輸入(走路與飛行都是),操作權整個交回使用者;出招、閃避提示、方位提示與減傷照常。");
            _explainedAutoMovementPause = true;
        }
        else
            Service.Logger.Debug($"[MovementOverride] 暫停自動移動:放開（按住 {(Environment.TickCount64 - _autoMovementPausedSinceMs) / 1000d:f1} 秒）——自動移動當幀恢復。");
    }

    private long _autoMovementPausedSinceMs;

    public bool MovementBlocked
    {
        get => field && !IsForceUnblocked();
        set;
    }

    // sig 失效時為 null:誤導(misdirection)輔助與強制移動方向同步降級停用,不讓整個外掛載入失敗
    public static readonly float* ForcedMovementDirection = InitForcedMovementDirection();

    private static float* InitForcedMovementDirection()
    {
        if (Service.SigScanner.TryGetStaticAddressFromSig("F3 0F 11 0D ?? ?? ?? ?? 48 85 DB", out var addr))
            return (float*)addr;
        Service.Log("[MovementOverride] 特徵碼解析失敗:ForcedMovementDirection,誤導輔助功能停用");
        return null;
    }

    private delegate bool RMIWalkIsInputEnabled(void* self);
    private readonly RMIWalkIsInputEnabled? _rmiWalkIsInputEnabled1;
    private readonly RMIWalkIsInputEnabled? _rmiWalkIsInputEnabled2;

    private delegate void RMIWalkDelegate(void* self, float* sumLeft, float* sumForward, float* sumTurnLeft, byte* haveBackwardOrStrafe, byte* a6, byte bAdditiveUnk);
    private readonly HookAddress<RMIWalkDelegate> _rmiWalkHook;
    // NativeLayoutGate 載入時用同一條特徵碼推導「旋轉移動」旗標位置。
    internal const string RMIWalkSignature = "E8 ?? ?? ?? ?? 80 7B 3E 00 48 8D 3D";

    private delegate void RMIFlyDelegate(void* self, PlayerMoveControllerFlyInput* result);
    private readonly HookAddress<RMIFlyDelegate> _rmiFlyHook;
    // NativeLayoutGate 載入時用同一條特徵碼驗 result 版面。
    internal const string RMIFlySignature = "E8 ?? ?? ?? ?? 0F B6 0D ?? ?? ?? ?? B8";

    // input source flags: 1 = kb/mouse, 2 = gamepad
    private delegate byte MoveControlIsInputActiveDelegate(void* self, byte inputSourceFlags);
    private readonly HookAddress<MoveControlIsInputActiveDelegate> _mcIsInputActiveHook;

    public MovementOverride(IDalamudPluginInterface dalamud)
    {
        _dalamud = dalamud;
        Instance = this;
        // sig 失效時降級:自動走位視同「移動輸入停用」,不讓整個外掛載入失敗
        if (Service.SigScanner.TryScanText("E8 ?? ?? ?? ?? 84 C0 75 10 38 43 3C", out var rmiWalkIsInputEnabled1Addr))
        {
            Service.Log($"RMIWalkIsInputEnabled1 address: 0x{rmiWalkIsInputEnabled1Addr:X}");
            _rmiWalkIsInputEnabled1 = Marshal.GetDelegateForFunctionPointer<RMIWalkIsInputEnabled>(rmiWalkIsInputEnabled1Addr);
        }
        else
            Service.Log("[MovementOverride] 特徵碼解析失敗:RMIWalkIsInputEnabled1,自動走位降級停用");
        if (Service.SigScanner.TryScanText("E8 ?? ?? ?? ?? 84 C0 75 03 88 47 3F", out var rmiWalkIsInputEnabled2Addr))
        {
            Service.Log($"RMIWalkIsInputEnabled2 address: 0x{rmiWalkIsInputEnabled2Addr:X}");
            _rmiWalkIsInputEnabled2 = Marshal.GetDelegateForFunctionPointer<RMIWalkIsInputEnabled>(rmiWalkIsInputEnabled2Addr);
        }
        else
            Service.Log("[MovementOverride] 特徵碼解析失敗:RMIWalkIsInputEnabled2,自動走位降級停用");

        _rmiWalkHook = new(RMIWalkSignature, RMIWalkDetour);
        _rmiFlyHook = new(RMIFlySignature, RMIFlyDetour);
        _mcIsInputActiveHook = new("E8 ?? ?? ?? ?? 84 C0 74 09 84 DB 74 1A", MCIsInputActiveDetour);

        Service.GameConfig.UiControlChanged += OnConfigChanged;
        UpdateLegacyMode();
    }

    public void Dispose()
    {
        _dalamud.RelinquishData("vnav.PathIsRunning");
        Service.GameConfig.UiControlChanged -= OnConfigChanged;
        MovementBlocked = false;
        _mcIsInputActiveHook.Dispose();
        _rmiWalkHook.Dispose();
        _rmiFlyHook.Dispose();
        Instance = null;
    }

    public bool FollowPathActive()
    {
        if (_navmeshPathIsRunning == null && _dalamud.TryGetData<bool[]>("vnav.PathIsRunning", out var data))
            _navmeshPathIsRunning = data;

        return _navmeshPathIsRunning != null && _navmeshPathIsRunning[0];
    }

    // RMIWalk／RMIFly 兩支 detour 遵守 fail-closed 約定（見 Util/DetourGuard.cs）：自訂邏輯進 try、Original 留在 try 外。
    // 失敗時 *sumLeft/*sumForward 維持 Original 算出來的值，也就是「玩家自己的輸入原封不動通過、只是不做覆寫」。
    // 受管理例外來源：① FollowPathActive() 讀 vnavmesh 透過 Dalamud data share 給的 bool[]（長度不是我們控制的 → IndexOutOfRange）
    //   ② ForwardMovementDirection() 的 Camera.Instance!（NullReference）與 CS 特徵碼失效時擲的 InvalidOperationException。
    // sumLeft／sumForward／result 是遊戲的 out 參數，每一支都先呼叫 Original（負責寫入它們的遊戲函式），刻意不補判空。
    private void RMIWalkDetour(void* self, float* sumLeft, float* sumForward, float* sumTurnLeft, byte* haveBackwardOrStrafe, byte* a6, byte bAdditiveUnk)
    {
        // 附加取樣（bAdditiveUnk==1）緊接在同一幀的一般取樣之後：不清掉一般取樣已決定的強制輸入狀態。
        if (bAdditiveUnk == 0)
            _forcedControlState = null;
        _rmiWalkHook.Original(self, sumLeft, sumForward, sumTurnLeft, haveBackwardOrStrafe, a6, bAdditiveUnk);

        // 旋轉移動（狀態 2973）：遊戲只在旗標非零、一般取樣沒有輸入時才做這次附加取樣，此時只能轉向，
        // 一般移動的覆寫全部不適用 ⇒ 只在使用者沒輸入時轉向 DesiredSpinDirection，其餘原樣交回。
        var spinningOffset = NativeLayoutGate.MoveControllerSpinningOffset;
        if (bAdditiveUnk == 1 && spinningOffset != 0 && *((byte*)self + spinningOffset) == 1)
        {
            try
            {
                if (!AutoMovementPaused && DesiredSpinDirection is { } dir && *sumLeft == 0 && *sumForward == 0)
                {
                    var turnDir = (dir - ForwardMovementDirection()).ToDirection();
                    *sumLeft = turnDir.X;
                    *sumForward = turnDir.Z;
                    _forcedControlState = true;
                }
            }
            catch (Exception ex)
            {
                DetourGuard.Report(nameof(RMIWalkDetour), ex);
            }
            return;
        }

        bool movementAllowed, misdirectionMode;
        // 診斷用：這一幀有沒有真的問過 vnavmesh、問到的答案是什麼。
        // 拆成兩個變數是為了保住短路：inputEnabled 為 false 時不呼叫 FollowPathActive()，
        // 多呼叫一次會多一個擲 IndexOutOfRange 的機會（見這支 detour 上方約定的 ①）。
        bool inputEnabled = false, followPathActive = false;
        try
        {
            // TODO: we really need to introduce some extra checks that PlayerMoveController::readInput does - sometimes it skips reading input, and returning something non-zero breaks stuff...
            inputEnabled = bAdditiveUnk == 0 && _rmiWalkIsInputEnabled1 != null && _rmiWalkIsInputEnabled1(self) && _rmiWalkIsInputEnabled2 != null && _rmiWalkIsInputEnabled2(self);
            if (inputEnabled)
                followPathActive = FollowPathActive();
            movementAllowed = inputEnabled && !followPathActive;
            misdirectionMode = PlayerHasMisdirection();
        }
        catch (Exception ex)
        {
            // 連「該不該介入」都算不出來 → 完全不介入，Original 的輸出原封不動送回遊戲
            DetourGuard.Report(nameof(RMIWalkDetour), ex);
            return;
        }

        // 診斷擺在 try 外面：上面那個 catch 會 return，記 log 失敗不該連帶取消這一幀的移動覆寫。
        LogFollowPathYield(inputEnabled, followPathActive);

        if (!movementAllowed && misdirectionMode)
        {
            // in misdirection mode, when we are already moving, the 'original' call will not actually sample input and just return immediately
            // we actually want to know the direction, in case user changes input mid movement - so force sample raw input
            // 注意：這是第二次呼叫 Original，同樣刻意留在 try 外
            float realTurn = 0;
            byte realStrafe = 0, realUnk = 0;
            _rmiWalkHook.Original(self, sumLeft, sumForward, &realTurn, &realStrafe, &realUnk, 1);
        }

        try
        {
            // at this point, UserMove contains true user input
            UserMove = new(*sumLeft, *sumForward);

            // apply movement block logic
            // note: currently movement block is ignored in misdirection mode
            // the assumption is that, with misdirection active, it's not safe to block movement just because player is casting or doing something else (as arrow will rotate away)
            ActualMove = !MovementBlocked || misdirectionMode ? UserMove : default;

            // movement override logic
            // note: currently we follow desired direction, only if user does not have any input _or_ if manual movement is blocked
            // this allows AI mode to move even if movement is blocked (TODO: is this the right behavior? AI mode should try to avoid moving while casting anyway...)
            var allowAuto = movementAllowed ? !MovementBlocked : misdirectionMode;
            if (allowAuto && ActualMove == default && DirectionToDestination(false) is var relDir && relDir != null)
            {
                ActualMove = relDir.Value.h.ToDirection();
            }

            // misdirection override logic
            if (misdirectionMode)
            {
                var thresholdDeg = UserMove != default ? _tweaksConfig.MisdirectionThreshold : MisdirectionThreshold.Deg;
                if (thresholdDeg < 180f && ForcedMovementDirection != null)
                {
                    // note: if we are already moving, it doesn't matter what we do here, only whether 'is input active' function returns true or false
                    _forcedControlState = ActualMove != default && (Angle.FromDirection(ActualMove) + ForwardMovementDirection() - ForcedMovementDirection->Radians()).Normalized().Abs().Deg <= thresholdDeg;
                }
            }

            // finally, update output
            var output = !misdirectionMode ? ActualMove // standard mode - just return desired movement
                : !movementAllowed ? default // misdirection and already moving - always return 0, as game does
                : _forcedControlState == null ? ActualMove // misdirection mode, but we're not trying to help user
                : _forcedControlState.Value ? ActualMove // misdirection mode, not moving yet, but want to start - can return anything really
                : default; // misdirection mode, not moving yet and don't want to
            *sumLeft = output.X;
            *sumForward = output.Z;
        }
        catch (Exception ex)
        {
            // 半路失敗：*sumLeft/*sumForward 還沒被我們改過（最後兩行是唯一的寫入點，
            // 而且是不會擲例外的欄位讀取）⇒ 遊戲拿到的仍是它自己算出來的輸入。
            // _forcedControlState 維持 null，MCIsInputActiveDetour 也會自動退回 Original。
            DetourGuard.Report(nameof(RMIWalkDetour), ex);
        }
    }

    /// <summary>目前是不是已經回報過「因為 vnavmesh 而讓路」。</summary>
    private bool _yieldingToFollowPath;

    /// <summary>診斷用：BMR 想自動走、但因為 vnavmesh 回報路徑執行中而讓路（與上面那支 log 同一個狀態）。</summary>
    public bool YieldingToFollowPath => _yieldingToFollowPath;

    /// <summary>
    /// 把「BMR 想自動走位，但因為 vnavmesh 回報路徑執行中而整段讓路」這件事講出來。
    /// </summary>
    /// <remarks>
    /// 🔴 movementAllowed 為 false 有四個成因（bAdditiveUnk、兩個 IsInputEnabled、FollowPathActive），
    /// <b>只有最後一個</b>值得報：前三個是遊戲自己正常的「現在不讀移動輸入」，報了全是噪音。
    /// <para>
    /// 🔴 解除條件刻意只看 <paramref name="followPathActive"/>，<b>不</b>看「還想不想自動走」——
    /// DesiredDirection 每幀由 <c>AIHints.ForcedMovement</c> 重算，本來就會頻繁進出，
    /// 拿它當解除條件會讓同一行 log 每秒刷好幾次。真正的狀態變化是 vnavmesh 放手。
    /// </para>
    /// <para>
    /// ⚠️ <paramref name="evaluated"/> 為 false 代表這一幀連問都沒問（遊戲自己就停用了移動輸入），
    /// 那就<b>什麼都不宣稱</b>——維持上一次的判斷，不要把「不知道」印成「已恢復」。
    /// </para>
    /// 📌 要使用者回報的診斷（<c>Information</c>）：這條路徑完全隱形——
    /// vnavmesh 留了殘路徑時，注入會被永久壓住，而手動輸入與世界標線都正常，看不出任何異狀。
    /// </remarks>
    private void LogFollowPathYield(bool evaluated, bool followPathActive)
    {
        if (!evaluated)
            return;

        if (!_yieldingToFollowPath)
        {
            // 只有「真的有想走的方向」時才值得報一次：沒人要自動走的時候讓路是無害的
            if (!followPathActive || DesiredDirection is not { } wanted || wanted == default)
                return;
            _yieldingToFollowPath = true;
            Service.Logger.Information("[MovementOverride] 自動走位讓路中：vnavmesh 回報路徑執行中（Dalamud 共享資料 vnav.PathIsRunning = true），BMR 這一段完全不注入移動輸入。若 vnavmesh 其實沒有在走，代表它留了殘路徑，BMR 的自動移動會一直被壓住——手動輸入與世界標線都不受影響，所以外觀上看不出來。");
        }
        else if (!followPathActive)
        {
            _yieldingToFollowPath = false;
            Service.Logger.Information("[MovementOverride] 自動走位讓路結束：vnavmesh 已回報路徑停止，BMR 恢復注入移動輸入。");
        }
    }

    private void RMIFlyDetour(void* self, PlayerMoveControllerFlyInput* result)
    {
        _forcedControlState = null;
        _rmiFlyHook.Original(self, result);

        try
        {
            // do nothing while followpath is running
            if (FollowPathActive())
                return;

            // TODO: we really need to introduce some extra checks that PlayerMoveController::readInput does - sometimes it skips reading input, and returning something non-zero breaks stuff...
            if (result->Forward == 0 && result->Left == 0 && result->Up == 0 && DirectionToDestination(true) is var relDir && relDir != null)
            {
                var dir = relDir.Value.h.ToDirection();
                result->Forward = dir.Z;
                result->Left = dir.X;
                result->Up = relDir.Value.v.Rad;
            }
        }
        catch (Exception ex)
        {
            DetourGuard.Report(nameof(RMIFlyDetour), ex);
        }
    }

    // 刻意**不**包 try：這支的自訂邏輯只有讀一個 bool? 欄位，沒有任何會擲受管理例外的東西
    // （`_forcedControlState != null` 已經保證 `.Value` 安全），唯一的呼叫就是 Original 本身。
    // 包起來只會得到一個永遠進不去的 catch。
    private byte MCIsInputActiveDetour(void* self, byte inputSourceFlags)
    {
        return _forcedControlState != null ? (byte)(_forcedControlState.Value ? 1 : 0) : _mcIsInputActiveHook.Original(self, inputSourceFlags);
    }

    private (Angle h, Angle v)? DirectionToDestination(bool allowVertical)
    {
        // 🔴 閘門放在**消費端**而不是 DesiredDirection 的指派端,是刻意的:
        //    這一支是走路(RMIWalkDetour)與飛行(RMIFlyDetour)兩條注入唯一共用的出口,
        //    擋在這裡＝以後不管誰寫 DesiredDirection(Plugin.ExecuteHints、Debug 視窗、將來新增的)
        //    都自動被同一道閘門涵蓋,不會漏掉一個寫入點。
        // 📌 回 null 的語意與「沒有想去的方向」逐字相同 ⇒ 呼叫端不必改,ActualMove 保持使用者自己的輸入。
        if (AutoMovementPaused)
            return null;

        if (DesiredDirection == null || DesiredDirection.Value == default)
            return null;

        var player = GameObjectManager.Instance()->Objects.IndexSorted[0].Value;
        if (player == null)
            return null;

        var dxz = new WDir(DesiredDirection.Value.X, DesiredDirection.Value.Z);
        var dirH = Angle.FromDirection(dxz);
        var dirV = allowVertical ? Angle.FromDirection(new(DesiredDirection.Value.Y, dxz.Length())) : default;
        return (dirH - ForwardMovementDirection(), dirV);
    }

    private Angle ForwardMovementDirection() => LegacyMode ? Camera.Instance!.CameraAzimuth.Radians() + 180f.Degrees() : GameObjectManager.Instance()->Objects.IndexSorted[0].Value->Rotation.Radians();

    private bool PlayerHasMisdirection()
    {
        var player = (Character*)GameObjectManager.Instance()->Objects.IndexSorted[0].Value;
        var sm = player != null && player->IsCharacter() ? player->GetStatusManager() : null;
        if (sm == null)
            return false;
        // NumValidStatuses 是遊戲寫入的 byte，Status 是 FixedSizeArray60：夾到容量內。
        var numStatuses = Math.Min((int)sm->NumValidStatuses, sm->Status.Length);
        for (var i = 0; i < numStatuses; ++i)
            if (sm->Status[i].StatusId is 1422 or 2936 or 3694 or 3909)
                return true;
        return false;
    }

    // 上一次寫進 log 的 legacy mode 值；null ＝ 還沒印過任何一行。
    private bool? _loggedLegacyMode;

    private void OnConfigChanged(object? sender, ConfigChangeEvent evt) => UpdateLegacyMode();
    private void UpdateLegacyMode()
    {
        // UiControlChanged 會對「任何」UI 設定變更觸發，所以這個方法被呼叫得非常頻繁。
        // LegacyMode 必須每次重讀（那是行為），但 log 只在值真的變了時才印。
        LegacyMode = Service.GameConfig.UiControl.TryGetUInt("MoveMode", out var mode) && mode == 1;
        if (_loggedLegacyMode == LegacyMode)
            return;
        var firstLegacyModeLog = _loggedLegacyMode is null;
        _loggedLegacyMode = LegacyMode;
        Service.Log(firstLegacyModeLog
            ? $"Legacy mode is initially {(LegacyMode ? "enabled" : "disabled")}"
            : $"Legacy mode is now {(LegacyMode ? "enabled" : "disabled")}");
    }
}
