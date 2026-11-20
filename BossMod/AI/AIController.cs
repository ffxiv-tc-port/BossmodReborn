using Dalamud.Game.ClientState.Conditions;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.Object;

namespace BossMod.AI;

// utility for simulating user actions based on AI decisions:
// - navigation
// - using actions safely (without spamming, not in cutscenes, etc)
sealed class AIController(WorldState ws, ActionManagerEx amex, MovementOverride movement)
{
    public WPos? NaviTargetPos;
    public float? NaviTargetVertical;
    public bool AllowInterruptingCastByMovement;
    public bool ForceCancelCast;
    // 到點防過衝：AIManager 每幀從設定抄進來（關＝與沒有這段時逐位元相同）
    public bool ArrivalHoldEnabled;
    public float ArrivalHoldDistance = 0.3f;
    private readonly ArrivalHold _arrivalHold = new();

    private readonly ActionManagerEx _amex = amex;
    private readonly MovementOverride _movement = movement;

    /// <summary>診斷用、只在主執行緒呼叫：下游會擋住自動移動的各道閘門此刻的狀態。</summary>
    public (bool Paused, bool UserMove, bool Moving, bool Blocked, bool VnavPath, bool CastGuard, bool EscapeHatch) MoveGates()
        => (_movement.AutoMovementPaused, _movement.IsMoveRequested(), _movement.IsMoving(), _movement.MovementBlocked, _movement.YieldingToFollowPath, _amex.MoveMightInterruptCast, _movement.IsForceUnblocked());

    public bool IsVerticalAllowed => Service.Condition[ConditionFlag.InFlight];
    public Angle CameraFacing => (Camera.Instance?.CameraAzimuth ?? 0).Radians() + 180.Degrees();
    public Angle CameraAltitude => (Camera.Instance?.CameraAltitude ?? 0).Radians();

    public void Clear()
    {
        NaviTargetPos = null;
        NaviTargetVertical = null;
        AllowInterruptingCastByMovement = false;
        ForceCancelCast = false;
    }

    public void SetFocusTarget(Actor? actor)
    {
        if (Service.TargetManager.FocusTarget?.EntityId != actor?.InstanceID)
            Service.TargetManager.FocusTarget = actor != null ? Service.ObjectTable.SearchById((uint)actor.InstanceID) : null;
    }

    public void Update(Actor? player, AIHints hints, DateTime now)
    {
        if (player == null || player.IsDead || ws.Party.Members[PartyState.PlayerSlot].InCutscene)
        {
            return;
        }

        Vector3? desiredPosition = null;

        // TODO this checks whether movement keys are pressed, we need a better solution
        var moveRequested = _movement.IsMoveRequested();
        var castInProgress = player.CastInfo != null && !player.CastInfo.EventHappened;
        var forbidMovement = moveRequested || !AllowInterruptingCastByMovement && _amex.MoveMightInterruptCast;
        var hold = NaviTargetPos != null && !forbidMovement
            && _arrivalHold.ShouldHold(ArrivalHoldEnabled && !(NaviTargetVertical != null && IsVerticalAllowed) && !MisdirectionActive(hints, now),
                now, player.Position, NaviTargetPos.Value, 0.001f, ws.Client.MoveSpeed, ws.Frame.Duration, ArrivalHoldDistance, hints.ForbiddenZones);
        if (NaviTargetPos != null && !forbidMovement && (NaviTargetPos.Value - player.Position).LengthSq() > 0.001f && !hold)
        {
            desiredPosition = NaviTargetPos.Value.ToVec3(NaviTargetVertical != null && IsVerticalAllowed ? NaviTargetVertical.Value : player.PosRot.Y);
        }
        else
        {
            hints.ForceCancelCast |= ForceCancelCast && castInProgress;
        }

        if (hints.ForcedMovement == null && desiredPosition != null)
        {
            hints.ForcedMovement = desiredPosition.Value - player.PosRot.XYZ();
        }
    }

    // 迷惑中能不能走看的是遊戲強制的方向，不是離目的地多遠 ⇒ 不停住
    private static bool MisdirectionActive(AIHints hints, DateTime now)
        => hints.ImminentSpecialMode.mode == AIHints.SpecialMode.Misdirection && hints.ImminentSpecialMode.activation <= now;
}
