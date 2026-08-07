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

    public bool IsForceUnblocked() => _tweaksConfig.MoveEscapeHatch switch
    {
        ActionTweaksConfig.ModifierKey.Ctrl => ImGui.GetIO().KeyCtrl,
        ActionTweaksConfig.ModifierKey.Alt => ImGui.GetIO().KeyAlt,
        ActionTweaksConfig.ModifierKey.Shift => ImGui.GetIO().KeyShift,
        ActionTweaksConfig.ModifierKey.M12 => UIInputData.Instance()->UIFilteredCursorInputs.MouseButtonHeldFlags.HasFlag(MouseButtonFlags.LBUTTON | MouseButtonFlags.RBUTTON),
        _ => false,
    };

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

    private delegate void RMIFlyDelegate(void* self, PlayerMoveControllerFlyInput* result);
    private readonly HookAddress<RMIFlyDelegate> _rmiFlyHook;

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

        _rmiWalkHook = new("E8 ?? ?? ?? ?? 80 7B 3E 00 48 8D 3D", RMIWalkDetour);
        _rmiFlyHook = new("E8 ?? ?? ?? ?? 0F B6 0D ?? ?? ?? ?? B8", RMIFlyDetour);
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

    // 以下三支 detour 遵守與 WorldStateGameSync 相同的 fail-closed 約定（見 Util/DetourGuard.cs）：
    // **自訂邏輯進 try、Original 一律留在 try 外**。失敗時 *sumLeft/*sumForward 維持 Original 算出來的值，
    // 也就是「玩家自己的輸入原封不動通過、只是不做覆寫」。
    // ⚠️ 這不防 AccessViolationException（在 .NET Core 是 corrupted-state exception，攔不到）；
    //    防的是受管理例外 —— 這裡實際存在的來源有兩個：
    //    ① FollowPathActive() 讀 vnavmesh 透過 Dalamud data share 給的 bool[]（長度不是我們控制的 → IndexOutOfRange）
    //    ② ForwardMovementDirection() 的 Camera.Instance!（null-forgiving → NullReference）
    //       與 CS 特徵碼失效時 [StaticAddress]/[MemberFunction] 擲的 InvalidOperationException。
    private void RMIWalkDetour(void* self, float* sumLeft, float* sumForward, float* sumTurnLeft, byte* haveBackwardOrStrafe, byte* a6, byte bAdditiveUnk)
    {
        _forcedControlState = null;
        _rmiWalkHook.Original(self, sumLeft, sumForward, sumTurnLeft, haveBackwardOrStrafe, a6, bAdditiveUnk);

        bool movementAllowed, misdirectionMode;
        try
        {
            // TODO: we really need to introduce some extra checks that PlayerMoveController::readInput does - sometimes it skips reading input, and returning something non-zero breaks stuff...
            movementAllowed = bAdditiveUnk == 0 && _rmiWalkIsInputEnabled1 != null && _rmiWalkIsInputEnabled1(self) && _rmiWalkIsInputEnabled2 != null && _rmiWalkIsInputEnabled2(self) && !FollowPathActive();
            misdirectionMode = PlayerHasMisdirection();
        }
        catch (Exception ex)
        {
            // 連「該不該介入」都算不出來 → 完全不介入，Original 的輸出原封不動送回遊戲
            DetourGuard.Report(nameof(RMIWalkDetour), ex);
            return;
        }

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
    // 包起來只會得到一個永遠進不去的 catch，反而讓下一輪稽核以為這裡有東西要防。
    private byte MCIsInputActiveDetour(void* self, byte inputSourceFlags)
    {
        return _forcedControlState != null ? (byte)(_forcedControlState.Value ? 1 : 0) : _mcIsInputActiveHook.Original(self, inputSourceFlags);
    }

    private (Angle h, Angle v)? DirectionToDestination(bool allowVertical)
    {
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
        for (var i = 0; i < sm->NumValidStatuses; ++i)
            if (sm->Status[i].StatusId is 1422 or 2936 or 3694 or 3909)
                return true;
        return false;
    }

    private void OnConfigChanged(object? sender, ConfigChangeEvent evt) => UpdateLegacyMode();
    private void UpdateLegacyMode()
    {
        LegacyMode = Service.GameConfig.UiControl.TryGetUInt("MoveMode", out var mode) && mode == 1;
        Service.Log($"Legacy mode is now {(LegacyMode ? "enabled" : "disabled")}");
    }
}
