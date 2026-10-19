using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.Event;
using FFXIVClientStructs.FFXIV.Client.Game.InstanceContent;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.System.Framework;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using FFXIVClientStructs.FFXIV.Common.Component.BGCollision;
using System.Runtime.InteropServices;
using CSActionType = FFXIVClientStructs.FFXIV.Client.Game.ActionType;

namespace BossMod;

// extensions and utilities for interacting with game's ActionManager singleton
// handles following features:
// 1. automatic action execution (provided by autorotation or ai modules, if enabled); does nothing if no automatic actions are provided
// 2. effective animation lock reduction (a-la xivalex)
// 3. framerate-dependent cooldown reduction
// 4. slidecast assistant aka movement block
//    cast is interrupted if player moves when remaining cast time is greater than ~0.5s (moving during that window without interrupting is known as slidecasting)
//    this feature blocks WSAD input to prevent movement while this would interrupt a cast, allowing slidecasting efficiently while just holding movement button
//    other ways of moving (eg LMB+RMB, jumping etc) are not blocked, allowing for emergency movement even while the feature is active
//    movement is blocked a bit before cast start and unblocked as soon as action effect packet is received
// 5. preserving character facing direction
// 6. ground-targeted action queueing
//    ground-targeted actions can't be queued, making using them efficiently tricky
//    this feature allows queueing them, plus provides options to execute them automatically either at target's position or at cursor's position
// 7. auto cancel cast utility
// TODO: should not be public!
public sealed unsafe class ActionManagerEx : IDisposable
{
    public ActionID CastSpell => new(ActionType.Spell, _inst->CastSpellId);
    public ActionID CastAction => new((ActionType)_inst->CastActionType, _inst->CastActionId);
    public float CastTimeRemaining => _inst->CastSpellId != 0 ? _inst->CastTimeTotal - _inst->CastTimeElapsed : 0;
    public float ComboTimeLeft => _inst->Combo.Timer;
    public uint ComboLastMove => _inst->Combo.Action;
    public ActionID QueuedAction => new((ActionType)_inst->QueuedActionType, _inst->QueuedActionId);

    public float EffectiveAnimationLock => _inst->AnimationLock + CastTimeRemaining; // animation lock starts ticking down only when cast ends, so this is the minimal time until next action can be requested
    public float AnimationLockDelayEstimate => _animLockTweak.DelayEstimate;

    public Event<ClientActionRequest> ActionRequestExecuted = new();
    public Event<ulong, ActorCastEvent> ActionEffectReceived = new();

    public static readonly ActionTweaksConfig Config = Service.Config.Get<ActionTweaksConfig>();
    public ActionQueue.Entry AutoQueue;
    public bool MoveMightInterruptCast; // if true, moving now might cause cast interruption (for current or queued cast)
    private readonly ActionManager* _inst = ActionManager.Instance();
    private readonly WorldState _ws;

    // 「技能被遊戲拒絕」那行的節流：被拒的技能下一幀還是會拿來問，所以同一組 (技能, 拒絕原因) 在 RejectLogInterval 秒內只印一次；
    // 拒絕原因改變、換成別的技能、或該技能終於送出去時立刻重置，「卡住的原因變了」與「恢復正常了」都不會被節流吃掉。
    private const double RejectLogInterval = 10;
    private ActionID _lastRejectAction;
    private uint _lastRejectStatus;
    private DateTime _lastRejectLogTime;
    private readonly AIHints _hints;
    private readonly MovementOverride _movement;
    private readonly ManualActionQueueTweak _manualQueue;
    // 手動佇列每幀刷新詠唱時間用（UpdateDetour、框架執行緒）；建構時建一次，每幀不配置委派
    private readonly Func<ActionID, ActionID> _queueAdjust;
    private readonly Func<ActionID, float> _queueCastSeconds;
    private readonly AnimationLockTweak _animLockTweak = new();
    private readonly CooldownDelayTweak _cooldownTweak = new();
    private readonly CancelCastTweak _cancelCastTweak;
    private readonly AutoDismountTweak _dismountTweak;
    private readonly DismountEpisodeLog _dismountLog = new(Service.Log, msg => Service.Logger.Information(msg));
    private readonly RestoreRotationTweak _restoreRotTweak = new();
    private readonly SmartRotationTweak _smartRotationTweak;
    private readonly OutOfCombatActionsTweak _oocActionsTweak;
    private readonly AutoAutosTweak _autoAutosTweak;
    private readonly CastTimeReductionTweak _castTimeTweak = new();
    private readonly SlidecastMarkerTweak _slidecastMarkerTweak;
    private readonly MacroQueueTweak _macroQueueTweak = new();
    private readonly ActionQueueWindowTweak _queueWindowTweak = new();
    private readonly IgnoreLineOfSightTweak _lineOfSightTweak = new();
    private readonly ChanneledMovementTweak _channelTweak = new();
    private readonly DashInterceptTweak _dashIntercept;
    private readonly DashDodgeTweak _dashDodge;

    private readonly HookAddress<ActionManager.Delegates.Update> _updateHook;
    private readonly HookAddress<ActionManager.Delegates.UseAction> _useActionHook;
    private readonly HookAddress<ActionManager.Delegates.UseActionLocation> _useActionLocationHook;
    private readonly HookAddress<PublicContentBozja.Delegates.UseFromHolster> _useBozjaFromHolsterDirectorHook;
    private readonly HookAddress<InstanceContentDeepDungeon.Delegates.UsePomander> _usePomanderHook;
    private readonly HookAddress<InstanceContentDeepDungeon.Delegates.UseStone> _useStoneHook;
    private readonly HookAddress<ActionEffectHandler.Delegates.Receive> _processPacketActionEffectHook;
    private readonly HookAddress<AutoAttackState.Delegates.SetImpl> _setAutoAttackStateHook;

    private delegate void ExecuteCommandGTDelegate(uint commandId, Vector3* position, uint param1, uint param2, uint param3, uint param4);
    private readonly ExecuteCommandGTDelegate? _executeCommandGT;
    private DateTime _nextAllowedExecuteCommand;
    private const uint InvalidEntityId = 0xE0000000;

    private readonly unsafe delegate* unmanaged<TargetSystem*, TargetSystem*> _autoSelectTarget;
    private bool _disposed;

    // 契約：只在框架執行緒的 ExecuteAction 裡、包住那一次原生呼叫時為 true。
    // UsePomander/UseStone 的 detour 是同一條執行緒上的巢狀回呼，讀到 false 就代表這一發是使用者自己按的。
    private bool _pomanderSendInFlight;
    private bool _stoneSendInFlight;

    // GameObjectManager (like most game singletons) is null during early login / zoning / title screen; dereferencing it from a per-frame detour throws NRE and kills the game
    private static GameObject* PlayerObject()
    {
        var gom = GameObjectManager.Instance();
        return gom != null ? gom->Objects.IndexSorted[0].Value : null;
    }

    // 當下讀原生值、不保存：世界狀態是這一幀稍早的快照，下坐騎在遊戲這一幀內完成時它還說「在坐騎上」，
    // 而沒騎乘時送 Action 4（坐騎，1 秒詠唱）＝開始召喚坐騎
    private static bool LocalPlayerMountedNow()
    {
        var chr = (Character*)PlayerObject();
        return chr != null && chr->Mount.MountId != 0;
    }

    public ActionManagerEx(WorldState ws, AIHints hints, MovementOverride movement)
    {
        _ws = ws;
        _hints = hints;
        _movement = movement;
        _manualQueue = new(ws, hints, DescribeActionBlockers);
        hints.ActionsToExecute.ManualForbidden = _manualQueue.OnManualForbidden;
        _queueAdjust = NormalizeActionForQueue;
        _queueCastSeconds = action => GetAdjustedCastTime(action) * 0.001f;
        _cancelCastTweak = new(ws, hints);
        _dismountTweak = new(ws);
        _smartRotationTweak = new(ws, hints);
        _oocActionsTweak = new(ws);
        _autoAutosTweak = new(ws, hints);
        _slidecastMarkerTweak = new(_castTimeTweak); // shares the reduction record, so the drawn window always matches the one CalculateDesiredOrientation uses
        _dashIntercept = new(ws);
        _dashDodge = new(ws, (action, target) => GetActionStatus(action, target), DashPathBlocked, DashDodgeTweak.VnavNearestPoint);

        Service.Log($"[AMEx] ActionManager singleton address = 0x{(ulong)_inst:X}");
        _updateHook = new(ActionManager.Addresses.Update, UpdateDetour);
        _useActionHook = new(ActionManager.Addresses.UseAction, UseActionDetour);
        _useActionLocationHook = new(ActionManager.Addresses.UseActionLocation, UseActionLocationDetour);
        _useBozjaFromHolsterDirectorHook = new(PublicContentBozja.Addresses.UseFromHolster, UseBozjaFromHolsterDirectorDetour);
        _usePomanderHook = new(InstanceContentDeepDungeon.Addresses.UsePomander, UsePomanderDetour);
        _useStoneHook = new(InstanceContentDeepDungeon.Addresses.UseStone, UseStoneDetour);
        _processPacketActionEffectHook = new(ActionEffectHandler.Addresses.Receive, ProcessPacketActionEffectDetour);
        _setAutoAttackStateHook = new(AutoAttackState.Addresses.SetImpl, SetAutoAttackStateDetour);

        if (Service.SigScanner.TryScanText("E8 ?? ?? ?? ?? EB 3D 8B 93 ?? ?? ?? ??", out var executeCommandGTAddress))
            _executeCommandGT = Marshal.GetDelegateForFunctionPointer<ExecuteCommandGTDelegate>(executeCommandGTAddress);
        else
            Service.Log("[AMEx] ExecuteCommandGT signature not found; ground-targeted pet actions will be unavailable");
        Service.Log($"ExecuteCommandGT address: 0x{executeCommandGTAddress:X}");

        if (Service.SigScanner.TryScanText("E8 ?? ?? ?? ?? 48 8B CE E8 ?? ?? ?? ?? 48 3B C5", out var selectTargetAddress))
            _autoSelectTarget = (delegate* unmanaged<TargetSystem*, TargetSystem*>)selectTargetAddress;
        else
            Service.Log("[AMEx] SelectTarget signature not found; auto nearest-target selection will be unavailable");
        Service.Log($"SelectTarget address: 0x{selectTargetAddress:X}");
    }

    public void Dispose()
    {
        _disposed = true;
        _setAutoAttackStateHook.Dispose();
        _processPacketActionEffectHook.Dispose();
        _useStoneHook.Dispose();
        _usePomanderHook.Dispose();
        _useBozjaFromHolsterDirectorHook.Dispose();
        _useActionLocationHook.Dispose();
        _useActionHook.Dispose();
        _updateHook.Dispose();
        _oocActionsTweak.Dispose();
        _castTimeTweak.Dispose();
        _queueWindowTweak.Dispose();
        _lineOfSightTweak.Dispose();
    }

    // ImGui overlay on top of the player's own cast bar; no-op unless explicitly enabled, and it neither reads nor writes any BossMod state
    public void DrawSlidecastMarker() => _slidecastMarkerTweak.Draw();

    /// <summary>
    /// 每幀重建位移攔截用的危險區快照，並更新手動佇列的 <c>HoldDashEmergency</c> 與詠唱讓位的期限。由 <c>Plugin.DrawUI</c> 在這一幀的
    /// 危險區建好之後呼叫；<paramref name="escapeHatchHeld"/> 由呼叫端傳入（<c>MovementOverride.IsForceUnblocked</c>）。
    /// </summary>
    public void UpdateDashIntercept(bool escapeHatchHeld)
    {
        // 先寫手動佇列的旗標：下面的快照擲例外時它照樣更新
        var aiHolds = AI.AIManager.Instance?.Beh != null && !escapeHatchHeld;
        _manualQueue.HoldDashEmergency = aiHolds;
        _manualQueue.EscapeHatchHeld = escapeHatchHeld;
        _dashIntercept.Update(_hints, escapeHatchHeld);
        // 讀「現在負責移動」那一份尋路上一次的閃避預判與腳下餘裕：自動移動舉手時是它（主執行緒），否則是 AI（背景執行緒，經 Volatile 欄位）
        _manualQueue.CastHazardNavigation = Autorotation.MiscAI.NormalMovement.OwnsMovement ? Autorotation.MiscAI.NormalMovement.NavigationContext : AI.AIManager.Instance?.Beh?.NavigationContext;
        _manualQueue.UpdateCastHazard(aiHolds); // 會呼叫模組的形狀函式，排最後：擲例外不影響上面兩項
    }

    public void QueueManualActions()
    {
        _manualQueue.RemoveExpired();
        _manualQueue.FillQueue(_hints.ActionsToExecute);
    }

    // finish gathering candidate actions for this frame: sort by priority and select best action to execute
    public void FinishActionGather()
    {
        AutoQueue = default;
        var player = _ws.Party.Player();
        if (player == null)
            return;

        _oocActionsTweak.FillActions(player, _hints);
        if (Config.DashDodge)
            _dashDodge.FillActions(player, _hints, _manualQueue.CastHazardNavigation, DashDodgeGates());
        AutoQueue = _hints.ActionsToExecute.FindBest(_ws, player, _ws.Client.Cooldowns, EffectiveAnimationLock, _hints, _animLockTweak.DelayEstimate, _dismountTweak.AutoDismountEnabled);
        if (AutoQueue.Delay > 0)
            AutoQueue = default;

        if (AutoQueue.Priority < ActionQueue.Priority.ManualEmergency)
        {
            if (Config.PyreticThreshold > 0 && _hints.ImminentSpecialMode.mode == AIHints.SpecialMode.Pyretic && _hints.ImminentSpecialMode.activation < _ws.FutureTime(Config.PyreticThreshold))
                AutoQueue = default; // do not execute non-emergency actions when pyretic is imminent

            if (_hints.FindEnemy(AutoQueue.Target)?.Priority == AIHints.Enemy.PriorityForbidden)
                AutoQueue = default; // or if selected target is forbidden
        }
        else if (AutoQueue.Manual && _manualQueue.HoldEmergencyForPyretic(AutoQueue))
            AutoQueue = default; // 連送升的緊急模式同樣不在熱病將至時放；逃生鍵按著才放
    }

    private DashDodgeTweak.Gates DashDodgeGates()
    {
        bool followPath;
        try
        {
            followPath = _movement.FollowPathActive();
        }
        catch (Exception)
        {
            followPath = true; // 讀不到 vnavmesh 的狀態＝當作它在走，不放
        }
        return new(AI.AIManager.Instance?.Beh != null, _manualQueue.EscapeHatchHeld, _movement.UserMove != default, _movement.AutoMovementPaused, followPath, EffectiveAnimationLock);
    }

    // 位移路徑上有沒有地形：與 CheckActionLoS 同一支射線、同樣抬高 2 碼；解析不到碰撞模組時當作擋著
    private static bool DashPathBlocked(Vector3 from, Vector3 to)
    {
        from.Y += 2;
        to.Y += 2;
        var offset = to - from;
        var dist = offset.Length();
        if (dist < 0.1f)
            return false;
        if (Framework.Instance() == null)
            return true;
        return BGCollisionModule.RaycastMaterialFilter(from, offset / dist, out _, dist);
    }

    public Vector3? GetWorldPosUnderCursor()
    {
        Vector3 res = default;
        return _inst->GetGroundPositionForCursor(&res) ? res : null;
    }

    public void FaceDirection(Angle direction)
    {
        var player = (Character*)PlayerObject();
        if (player != null)
        {
            var position = player->Position.ToSystem() + direction.ToDirection().ToVec3();
            _inst->AutoFaceTargetPosition(&position);

            // if rotation interpolation is in progress, we have to reset desired rotation to avoid game rotating us away next frame
            // 位移由 NativeLayoutGate 在載入時從遊戲碼推導；0＝沒推導過，不寫。
            var desiredRotationOffset = NativeLayoutGate.CharacterDesiredRotationOffset;
            if (desiredRotationOffset != 0)
                *(float*)((byte*)player + desiredRotationOffset) = direction.Rad;
        }
    }

    public void GetCooldown(ref Cooldown result, RecastDetail* data)
    {
        if (data->IsActive)
        {
            result.Elapsed = data->Elapsed;
            result.Total = data->Total;
        }
        else
        {
            result.Elapsed = result.Total = 0;
        }
    }

    public void GetCooldowns(Span<Cooldown> cooldowns)
    {
        // [0, 80) are in ActionManager
        var rg = _inst->GetRecastGroupDetail(0);
        var i = 0;
        for (; i < 80; ++i)
            GetCooldown(ref cooldowns[i], rg++);

        // 80, 81 are in DutyActionManager
        rg = _inst->GetRecastGroupDetail(80);
        if (rg != null)
        {
            for (; i < 82; ++i)
                GetCooldown(ref cooldowns[i], rg++);
        }
        else
        {
            for (; i < 82; ++i)
                cooldowns[i] = default;
        }

        // [82,87) are in MassivePcContentDirector
        rg = _inst->GetRecastGroupDetail(82);
        if (rg != null)
        {
            for (; i < 87; ++i)
                GetCooldown(ref cooldowns[i], rg++);
        }
        else
        {
            for (; i < 87; ++i)
                cooldowns[i] = default;
        }
    }

    public float GCD()
    {
        var gcd = _inst->GetRecastGroupDetail(ActionDefinitions.GCDGroup);
        return gcd->Total - gcd->Elapsed;
    }

    public ClientState.DutyAction GetDutyAction(ushort slot)
    {
        // TODO: 7.1: there are now 5 actions, but only 2 charges...
        var dm = DutyActionManager.GetInstanceIfReady();

        (byte cur, byte max) charges(ushort slot) => slot < 2 ? (dm->CurCharges[slot], dm->MaxCharges[slot]) : default;

        if (dm == null || !dm->ActionActive[0] || slot >= dm->NumValidSlots)
            return default;

        var (cur, max) = charges(slot);
        return new(new(ActionType.Spell, dm->ActionId[slot]), cur, max);
    }
    public ClientState.DutyAction[] GetDutyActions() => [GetDutyAction(0), GetDutyAction(1), GetDutyAction(2), GetDutyAction(3), GetDutyAction(4)];

    // 與上面同一組五格、同一順序，寫進呼叫端給的空間（slots 至少 5 格）。
    public void GetDutyActions(Span<ClientState.DutyAction> slots)
    {
        for (ushort i = 0; i < 5; ++i)
            slots[i] = GetDutyAction(i);
    }

    public uint GetAdjustedActionID(uint actionID) => _inst->GetAdjustedActionId(actionID);

    public uint GetSpellIdForAction(ActionID action) => ActionManager.GetSpellIdForAction((CSActionType)action.Type, action.ID);

    public uint GetActionStatus(ActionID action, ulong target, bool checkRecastActive = true, bool checkCastingActive = true, uint* outOptExtraInfo = null)
    {
        if (action.Type is ActionType.BozjaHolsterSlot0 or ActionType.BozjaHolsterSlot1)
            action = BozjaActionID.GetHolster(action.As<BozjaHolsterID>()); // see BozjaContentDirector.useFromHolster
        return _inst->GetActionStatus((CSActionType)action.Type, action.ID, target, checkRecastActive, checkCastingActive, outOptExtraInfo);
    }

    // 純診斷：在 detour 內（遊戲主執行緒）當幀取樣，抄成值型別交出去，呼叫端不持有原生指標。
    // 不參與任何判斷，所以取不到就留預設值；Range 由呼叫端用它自己手上的定義補。
    private ActionBlockInfo DescribeActionBlockers(ActionID action, Actor? target)
    {
        var player = _ws.Party.Player();
        return new(GetActionStatus(action, target?.InstanceID ?? InvalidEntityId), IsRecastTimerActive(action), GCD(), EffectiveAnimationLock, CastTimeRemaining,
            _movement.IsMoving(), _movement.MovementBlocked, 0, target != null ? player?.DistanceToHitbox(target) ?? 0 : 0, target != null);
    }

    // returns time in ms
    public int GetAdjustedCastTime(ActionID action, bool applyProcs = true, ActionManager.CastTimeProc* outOptProc = null)
        => ActionManager.GetAdjustedCastTime((CSActionType)action.Type, action.ID, applyProcs, outOptProc);

    public int GetAdjustedRecastTime(ActionID action, bool applyClassMechanics = true) => ActionManager.GetAdjustedRecastTime((CSActionType)action.Type, action.ID, applyClassMechanics);

    public bool CanMoveWhileCasting(ActionID action)
    {
        return action switch
        {
            { Type: ActionType.Spell, ID: 29391 or 29402 } => true, // phys ranged PVP actions
            { Type: ActionType.Mount } => true,
            _ => false
        };
    }

    public bool IsRecastTimerActive(ActionID action)
        => _inst->IsRecastTimerActive((CSActionType)action.Type, action.ID);

    public int GetRecastGroup(ActionID action)
        => _inst->GetRecastGroup((int)action.Type, action.ID);

    // see ActionEffectHandler.Receive - there are a few hardcoded actions here
    private bool ExpectAnimationLockUpdate(ActionEffectHandler.Header* header)
        => header->SourceSequence != 0 && !(header->ActionType == CSActionType.Action && (NIN.AID)header->ActionId is NIN.AID.Ten1 or NIN.AID.Chi1 or NIN.AID.Jin1 or NIN.AID.Ten2 or NIN.AID.Chi2 or NIN.AID.Jin2)
        || header->ForceAnimationLock;

    // perform some action transformations to simplify implementation of queueing; UseActionLocation expects some normalization to be already done
    private ActionID NormalizeActionForQueue(ActionID action)
    {
        switch (action.Type)
        {
            case ActionType.Spell:
                // for normal actions, we want to do adjustment immediately, before action is queued; there are several reasons for that:
                // 1. transformation can affect action targeting (eg MNK meditation/chakra); queue will check the properties of the queued action
                // 2. for classes that start at high level and then are synced down, the 'base' action can be some upgrade; the queue will ignore non-adjusted actions, assuming they aren't unlocked yet
                // note that when action is executed several frames later, the adjustment will be done again, in case something changes in the state
                return new(ActionType.Spell, GetAdjustedActionID(action.ID));
            case ActionType.General:
                // for general actions, we want to convert things we care about to spells; UseActionLocation will expect that to be done
                if (action == ActionDefinitions.IDGeneralLimitBreak)
                {
                    var lb = LimitBreakController.Instance();
                    var lbPlayer = (Character*)PlayerObject();
                    if (lb == null || lbPlayer == null)
                        return action;
                    var level = lb->BarUnits != 0 ? lb->CurrentUnits / lb->BarUnits : 0;
                    var id = level > 0 ? lb->GetActionId(lbPlayer, (byte)(level - 1)) : 0;
                    return id != 0 ? new(ActionType.Spell, id) : action;
                }
                // special case for lunar sprint, copied from UseGeneralAction
                else if (action == ActionDefinitions.IDGeneralSprint && GameMain.Instance() != null && GameMain.Instance()->CurrentTerritoryIntendedUseId == 60)
                {
                    return new(ActionType.Spell, 43357);
                }
                else if (action == ActionDefinitions.IDGeneralSprint || action == ActionDefinitions.IDGeneralDuty1 || action == ActionDefinitions.IDGeneralDuty2)
                {
                    return new(ActionType.Spell, GetSpellIdForAction(action));
                }
                else
                {
                    return action;
                }
            default:
                return action;
        }
    }

    // skips queueing etc
    // 不是 detour：唯一的呼叫端是 UpdateDetour 裡已經包好 try 的那一段，_inst 也已在該處判過空。
    // 刻意不在這裡再包一層 try：那會讓 UpdateDetour 的 fail-closed 語意（失敗就不封鎖移動）失效。
    private bool ExecuteAction(ActionID action, ulong targetId, Vector3 targetPos)
    {
        switch (action.Type)
        {
            case ActionType.Spell:
                // for spells, execute our UAL hook
                // note that for 'summon carbuncle/eos/titan/ifrit/garuda' actions, extraParam can be used to select glamour; the function will return 0 for non-summon actions
                return _inst->UseActionLocation(CSActionType.Action, action.ID, targetId, &targetPos, ActionManager.GetExtraParamForSummonAction(action.ID));
            case ActionType.Item:
                // note that for items extraParam should be 0xFFFF (since we want to use any item, not from first inventory slot)
                return _inst->UseActionLocation(CSActionType.Item, action.ID, targetId, &targetPos, 0xFFFFu);
            case ActionType.General:
                // TODO: are there any general actions that require (or even work with) UAL?
                // 23 Dismount does not, haven't tested others
                return _useActionHook.Original(_inst, CSActionType.GeneralAction, action.ID, targetId, 0, ActionManager.UseActionMode.None, 0, null);
            case ActionType.PetAction:
                if (action.ID == 3)
                {
                    // pet action "Place" - uses location targeting but doesn't interact with UseActionLocation at all, meaning it requires its own send-packet function
                    if (_executeCommandGT == null)
                        return false;
                    var now = DateTime.Now;
                    if (_nextAllowedExecuteCommand > now)
                        return false;
                    _nextAllowedExecuteCommand = now.AddMilliseconds(100);
                    _executeCommandGT(1800, &targetPos, action.ID, 0, 0, 0);
                    return true;
                }
                else
                {
                    // all other pet actions can be used as normal through UA (not UAL)
                    // TODO: consider calling UsePetAction instead?..
                    return _useActionHook.Original(_inst, CSActionType.PetAction, action.ID, targetId, 0, ActionManager.UseActionMode.None, 0, null);
                }

            // fake action types
            case ActionType.BozjaHolsterSlot0:
            case ActionType.BozjaHolsterSlot1:
                var state = PublicContentBozja.GetState(); // note: if it's non-null, the director instance can't be null too
                var holsterIndex = state != null ? state->HolsterActions.IndexOf((byte)action.ID) : -1;
                return holsterIndex >= 0 && PublicContentBozja.GetInstance()->UseFromHolster((uint)holsterIndex, action.Type == ActionType.BozjaHolsterSlot1 ? 1u : 0);
            case ActionType.Pomander:
                var ef = EventFramework.Instance();
                var dd = ef != null ? ef->GetInstanceContentDeepDungeon() : null;
                var slot = _ws.DeepDungeon.GetPomanderSlot((PomanderID)action.ID);
                if (dd != null && slot >= 0)
                {
                    Service.Logger.Debug($"[AMEx][DD] 送出魔陶器：{(PomanderID)action.ID} 槽位 {slot}");
                    _pomanderSendInFlight = true;
                    try
                    {
                        dd->UsePomander((uint)slot);
                    }
                    finally
                    {
                        _pomanderSendInFlight = false;
                    }
                    return true;
                }
                Service.Logger.Information($"[AMEx][DD] 魔陶器送不出去：{(PomanderID)action.ID} dd={(dd != null ? "有" : "null")} 槽位={slot}");
                return false;
            case ActionType.Magicite:
                // ActionType.Magicite 的 id 是「槽位 + 1」（ActionDefinitions 註冊的是 1..3，0 不註冊），而 UseStone 吃的是 0 基底的槽位。
                // UseStone 本身沒有任何邊界檢查，越界的槽位絕不該送。
                if (action.ID is < 1 or > 3)
                {
                    Service.Logger.Information($"[AMEx][DD] 魔石送不出去：槽位代碼 {action.ID} 不在 1..3");
                    return false;
                }
                ef = EventFramework.Instance();
                dd = ef != null ? ef->GetInstanceContentDeepDungeon() : null;
                if (dd != null)
                {
                    Service.Logger.Information($"[AMEx][DD] 送出魔石：第 {action.ID} 格（0 基底槽位 {action.ID - 1}）");
                    _stoneSendInFlight = true;
                    try
                    {
                        dd->UseStone(action.ID - 1);
                    }
                    finally
                    {
                        _stoneSendInFlight = false;
                    }
                    return true;
                }
                Service.Logger.Information("[AMEx][DD] 魔石送不出去：拿不到 InstanceContentDeepDungeon");
                return false;

            default:
                // fall back to UAL hook for everything not covered explicitly
                return _inst->UseActionLocation((CSActionType)action.Type, action.ID, targetId, &targetPos, 0);
        }
    }

    private Angle? CalculateDesiredOrientation(bool actionImminent)
    {
        if (actionImminent && AutoQueue.FacingAngle != null)
            return AutoQueue.FacingAngle; // explicit angle overrides all other concerns

        var gom = GameObjectManager.Instance();
        if (gom == null)
            return null; // early login / zoning / title screen - no object manager yet
        var player = (Character*)gom->Objects.IndexSorted[0].Value;
        if (player == null)
            return null;
        var current = player->Rotation.Radians();

        // restore rotation logic; note that movement abilities (like charge) can take multiple frames until they allow changing facing
        var restored = MoveMightInterruptCast || actionImminent ? null : _restoreRotTweak.TryRestore(current);

        // gaze avoidance & targeting
        // note: to execute an oriented action (cast a spell or use instant), target has to be within 45 degrees of character orientation (reversed)
        // to finish a spell without interruption, by the beginning of the slide-cast window target has to be within 75 degrees of character orientation (empirical)
        var castInfo = player->GetCastInfo();
        // with <500ms remaining on cast timer, player can face and move wherever they want and still complete the cast successfully (slidecast)
        // note: if CastTimeReductionTweak shortened this cast, the client's total is already that much shorter than the duration the server used,
        // so the window has to shrink by the same amount to keep pointing at the same moment in real time; returns 0 while the tweak is off
        var slidecastWindow = 0.5f;
        if (castInfo != null)
            slidecastWindow -= _castTimeTweak.ReductionSeconds(new((ActionType)castInfo->ActionType, castInfo->ActionId));
        var isCasting = castInfo != null && castInfo->IsCasting && castInfo->CurrentCastTime + slidecastWindow < castInfo->TotalCastTime;
        var currentAction = isCasting ? new((ActionType)castInfo->ActionType, castInfo->ActionId) : actionImminent ? AutoQueue.Action : default;
        var currentTargetId = isCasting ? (ulong)castInfo->TargetId : (AutoQueue.Target?.InstanceID ?? InvalidEntityId);
        var currentTargetSelf = currentTargetId == player->EntityId;
        var currentTargetObj = currentTargetSelf ? &player->GameObject : currentTargetId is not 0 and not InvalidEntityId ? gom->Objects.GetObjectByGameObjectId(currentTargetId) : null;
        WPos? currentTargetPos = currentTargetObj != null ? new WPos(currentTargetObj->Position.X, currentTargetObj->Position.Z) : null;
        var currentTargetLoc = isCasting ? new WPos(castInfo->TargetLocation.X, castInfo->TargetLocation.Z) : new(AutoQueue.TargetPos.XZ()); // note: this only matters for area-targeted spells, for which targetlocation in castinfo is set correctly
        var idealOrientation = currentAction ? _smartRotationTweak.GetSpellOrientation(GetSpellIdForAction(currentAction), new(player->Position.X, player->Position.Z), currentTargetSelf, currentTargetPos, currentTargetLoc) : null;
        var avoidGaze = _smartRotationTweak.GetSafeRotation(current, idealOrientation, isCasting ? 75.Degrees() : 45.Degrees());

        // avoiding a gaze has a priority over restore
        return avoidGaze ?? restored;
    }

    // fail-closed 約定（見 Util/DetourGuard.cs）：自訂邏輯進 try、Original 留在 try 外。這一支每幀執行，DetourGuard.Report 的節流是必要條件。
    // 受管理例外來源：① ExecuteAction 的 General/PetAction 分支呼叫 _useActionHook.Original（HookAddress<T>.Original 在位址解析失敗時
    // 擲 InvalidOperationException，而 Update 與 UseAction 的位址是各自解析的）② Service.LuminaRow<Action>／<LogMessage>
    // ③ CS 的 [MemberFunction]/[StaticAddress] 特徵碼失效 ④ 自動循環／smart rotation／手動佇列這些純受管理邏輯。
    // 不防 AccessViolationException：裸指標一律靠判空處理，不靠 try。
    private void UpdateDetour(ActionManager* self)
    {
        // Framework.Instance() 是 [StaticAddress(isPointer: true)]：位址解析失敗時擲 InvalidOperationException，否則回 *ppInstance（可能是 null）。
        // 兩種失效都存在 ⇒ 擲出由這個 try 接，null 由下面的 fwk == null 擋，兩個都要。
        // 引述 CS 宣告時讀實際編進來的那份：本專案 CustomCS=false，吃的是 Dalamud 自帶的 CS；repo 內的子模組只有 CustomCS=true 時才會被建置。
        Framework* fwk;
        try
        {
            fwk = Framework.Instance();
        }
        catch (Exception ex)
        {
            // 退化行為：與「遊戲還沒把 Framework 建起來」完全相同的既有路徑 —— 跑 Original、跳過我們整幀的邏輯。
            fwk = null;
            DetourGuard.Report(nameof(UpdateDetour) + "(resolve)", ex);
        }
        // the detour can still fire while/after we're being torn down (plugin unload or hot-update), and singletons can be null in early login / zoning / title states;
        // in either case just run the original update and skip all our per-frame logic instead of throwing out of the framework tick (which kills the game)
        if (_disposed || _updateHook.IsDisposed || fwk == null || _inst == null)
        {
            _updateHook.Original(self);
            return;
        }

        // 前段失敗時這兩個維持 default（falsy）→ 後段的移動封鎖預測整段跳過，等於這一幀沒有滑步輔助
        ActionID imminentAction = default, imminentActionAdj = default;
        try
        {
            var dt = fwk->GameSpeedMultiplier * fwk->FrameDeltaTime;
            imminentAction = _inst->ActionQueued ? QueuedAction : AutoQueue.Action;
            imminentActionAdj = imminentAction.Type == ActionType.Spell ? new(ActionType.Spell, GetAdjustedActionID(imminentAction.ID)) : imminentAction;
            var imminentRecast = imminentActionAdj ? _inst->GetRecastGroupDetail(GetRecastGroup(imminentActionAdj)) : null;

            _cooldownTweak.StartAdjustment(_inst->AnimationLock, imminentRecast != null && imminentRecast->IsActive ? imminentRecast->Total - imminentRecast->Elapsed : 0, dt);
        }
        catch (Exception ex)
        {
            // 補償值算不出來就當作沒有補償（＝ RemoveCooldownDelay 關掉時的行為）——
            // Adjustment 會被 HandleActionRequest 直接加到冷卻與動畫鎖上，不能拿半套的值去改遊戲狀態
            _cooldownTweak.StopAdjustment();
            DetourGuard.Report(nameof(UpdateDetour) + "(pre)", ex);
        }
        _updateHook.Original(self);

        // 手動佇列照「現在換過之後的技能」刷新詠唱時間，放出前再重判一次（換法與下面放出時的 NormalizeActionForQueue 相同）。
        // 失敗＝沿用排隊時的詠唱時間、照常放出（舊行為）。只在這裡（框架執行緒）呼叫原生函式，結果只存值。
        var holdManual = false;
        try
        {
            _manualQueue.RefreshCastTimes(_queueAdjust, _queueCastSeconds);
            holdManual = AutoQueue.Action && _manualQueue.HoldAtExecution(AutoQueue, _hints.MaxCastTime);
        }
        catch (Exception ex)
        {
            holdManual = false;
            DetourGuard.Report(nameof(UpdateDetour) + "(manual)", ex);
        }

        // autoRotateConfig 指向遊戲自己的設定項，我們只是「暫時」改它；還原動作放 finally，因為半路失敗
        // 而沒還原的話，使用者的「使用技能時自動面向目標」會被永久留在我們改過的值上（且他不會知道）。
        // 宣告在 try 外才能讓 finally 看得到；維持 null 表示我們還沒碰過它，finally 就不寫入。
        FFXIVClientStructs.FFXIV.Common.Configuration.ConfigEntry* autoRotateConfig = null;
        var autoRotateOriginal = 0u;
        var blockMovement = false;
        // 這一幀被坐騎擋住的技能（default＝沒有）；下面送不送下坐騎、診斷那一行都讀這幾個
        ActionID dismountFor = default;
        var dismountManual = false;
        var dismountInFlight = false;
        var dismountStatus = 0u;
        var dismountSkip = DismountSkip.None;
        try
        {
            // check whether movement is safe; block movement if not and if desired
            MoveMightInterruptCast &= CastTimeRemaining > 0; // previous cast could have ended without action effect
            // if we're not casting, but will start soon, moving might interrupt future cast
            // 這一幀不放的手動詠唱不算「馬上要詠唱」：照它擋移動會把該閃的人釘住
            if (imminentActionAdj && !(holdManual && imminentAction == AutoQueue.Action) && CastTimeRemaining <= 0 && _inst->AnimationLock < 0.1f && GetAdjustedCastTime(imminentActionAdj) > 0 && !CanMoveWhileCasting(imminentActionAdj) && GCD() < 0.1f)
            {
                // check LoS on target; blocking movement can cause AI mode to get stuck behind a wall trying to cast a spell on an unreachable target forever
                MoveMightInterruptCast |= CheckActionLoS(imminentAction, _inst->ActionQueued ? _inst->QueuedTargetId : (AutoQueue.Target?.InstanceID ?? 0));
            }
            // 🔑 引導技（噴火／即興表演／武裝戍衛…）走的是另一條訊號：它們是瞬發，CastTimeRemaining
            //    永遠是 0，所以 MoveMightInterruptCast 對它們結構性地不成立（見 ChanneledMovementTweak）。
            //    每幀都呼叫 Update 讓它的追蹤狀態保持同步，設定關著時只是不把結果 OR 進來 ——
            //    這樣使用者中途打開開關也不會拿到一個殘留的舊判斷。
            var player = _ws.Party.Player();
            var channelBlock = _channelTweak.Update(player, _ws.CurrentTime) && Config.PreventMovingWhileChanneling;
            // 📌 子旗標刻意收在 PreventMovingWhileCasting 這個總開關底下：總開關關著時整個括號不論真假都不生效，
            //    所以「引導技也封鎖」預設 false 加上總開關預設 false ⇒ 既有使用者的行為逐位元組不變。
            blockMovement = Config.PreventMovingWhileCasting && (MoveMightInterruptCast || channelBlock) && player?.MountId == 0;
            blockMovement |= Config.PyreticThreshold > 0 && _hints.ImminentSpecialMode.mode == AIHints.SpecialMode.Pyretic && _hints.ImminentSpecialMode.activation < _ws.FutureTime(Config.PyreticThreshold);

            // note: if we cancel movement and start casting immediately, it will be canceled some time later - instead prefer to delay for one frame
            bool actionImminent = !holdManual && EffectiveAnimationLock <= 0 && AutoQueue.Action && !IsRecastTimerActive(AutoQueue.Action) && !(blockMovement && _movement.IsMoving());
            var desiredRotation = CalculateDesiredOrientation(actionImminent);

            // execute rotation, if needed
            autoRotateConfig = fwk->SystemConfig.GetConfigOption((uint)ConfigOption.AutoFaceTargetOnAction);
            autoRotateOriginal = autoRotateConfig != null ? autoRotateConfig->Value.UInt : 0u;
            if (desiredRotation != null)
            {
                if (autoRotateConfig != null)
                    autoRotateConfig->Value.UInt = 1;
                FaceDirection(desiredRotation.Value);
            }

            if (actionImminent)
            {
                var actionAdj = NormalizeActionForQueue(AutoQueue.Action);
                var targetID = AutoQueue.Target?.InstanceID ?? InvalidEntityId;
                var status = GetActionStatus(actionAdj, targetID);
                if (status == 0)
                {
                    // disable in-game auto rotation, to prevent fucking up with our logic
                    if (autoRotateConfig != null)
                        autoRotateConfig->Value.UInt = _smartRotationTweak.Enabled || AI.AIManager.Instance?.Beh != null ? 0 : autoRotateOriginal;
                    // 送得出去＝上一次的拒絕原因已經不成立，清掉才不會讓下一次的拒絕被誤判成「同一次」而不印。
                    _lastRejectAction = default;
                    _lastRejectStatus = 0;
                    var res = ExecuteAction(actionAdj, targetID, AutoQueue.TargetPos);
                    //Service.Log($"[AMEx] Auto-execute {AutoQueue.Source} action {AutoQueue.Action} (=> {actionAdj}) @ {targetID:X} {Utils.Vec3String(AutoQueue.TargetPos)} => {res}");
                }
                else if (_dismountTweak.IsMountPreventingAction(actionAdj))
                {
                    dismountFor = actionAdj;
                    dismountManual = AutoQueue.Manual;
                    dismountStatus = status;
                    dismountInFlight = Service.Condition[Dalamud.Game.ClientState.Conditions.ConditionFlag.InFlight];
                    dismountSkip = AutoDismountTweak.AutoDismountGate(_dismountTweak.AutoDismountEnabled, dismountManual, dismountInFlight, AI.AIManager.Instance?.Beh != null, _movement.IsMoveRequested());
                    _hints.WantDismount |= dismountSkip == DismountSkip.None;
                }
                else
                {
                    var rejectNow = _ws.CurrentTime;
                    if (actionAdj != _lastRejectAction || status != _lastRejectStatus || (rejectNow - _lastRejectLogTime).TotalSeconds >= RejectLogInterval)
                    {
                        _lastRejectAction = actionAdj;
                        _lastRejectStatus = status;
                        _lastRejectLogTime = rejectNow;
                        Service.Log($"[AMEx] Can't execute prio {AutoQueue.Priority} action {AutoQueue.Action} (=> {actionAdj}) @ {targetID:X}: status {status} '{Service.LuminaRow<Lumina.Excel.Sheets.LogMessage>(status)?.Text}'");
                    }
                    blockMovement = false;
                }
            }
        }
        catch (Exception ex)
        {
            // 一律不封鎖移動：把玩家卡在原地（WSAD 失效、而且他不會知道為什麼）比少一次滑步輔助嚴重得多
            blockMovement = false;
            DetourGuard.Report(nameof(UpdateDetour), ex);
        }
        finally
        {
            // 這三行都是純欄位寫入，不會擲例外 —— finally 本身絕不能再擲，否則整個防護等於沒有
            if (autoRotateConfig != null)
                autoRotateConfig->Value.UInt = autoRotateOriginal;
            _cooldownTweak.StopAdjustment(); // clear any potential adjustments
            _movement.MovementBlocked = blockMovement;
        }

        try
        {
            // TODO: what's the reason to do it in AM update, rather than plugin's executehints?..
            var uiState = UIState.Instance();
            if (uiState != null)
            {
                if (_ws.Party.Player()?.CastInfo != null && _cancelCastTweak.ShouldCancel(_ws.CurrentTime, _hints.ForceCancelCast))
                    uiState->Hotbar.CancelCast();

                var autosEnabled = uiState->WeaponState.AutoAttackState.IsAutoAttacking;
                if (_autoAutosTweak.GetDesiredState(autosEnabled, _ws.Party.Player()?.TargetID ?? 0) != autosEnabled)
                    _inst->UseAction(CSActionType.GeneralAction, 1);
            }

            if (_hints.WantDismount)
            {
                var skip = _movement.FollowPathActive() ? DismountSkip.FollowPath
                    : !_dismountTweak.AllowDismount() ? DismountSkip.MountNotDismountable
                    : !LocalPlayerMountedNow() ? DismountSkip.AlreadyDismounted
                    : DismountSkip.None;
                if (skip == DismountSkip.None)
                    _inst->UseAction(CSActionType.Action, 4);
                if (dismountFor)
                    dismountSkip = skip;
            }
            var player = _ws.Party.Player();
            if (dismountFor)
                _dismountLog.Blocked(_ws.CurrentTime, dismountFor, dismountManual, dismountStatus, player?.MountId ?? 0, dismountInFlight, player?.InCombat ?? false, dismountSkip);
            else
                _dismountLog.NotBlocked(_ws.CurrentTime, (player?.MountId ?? 0) != 0);
        }
        catch (Exception ex)
        {
            // 失敗代價：這一幀不取消詠唱、不切自動攻擊、不下馬。下一幀會重算，狀態不會累積
            DetourGuard.Report(nameof(UpdateDetour) + "(post)", ex);
        }
    }

    // note: targetId is usually your current primary target (or InvalidEntityId if you don't target anyone), unless you do something like /ac XXX <f> etc
    // fail-closed 約定（見 Util/DetourGuard.cs）：自訂邏輯進 try、**Original 一律留在 try 外**。
    // 這裡的受管理例外來源是 _manualQueue.Push —— 它會走 LINQ、字典查表，並且呼叫 ActionDefinition 的
    // SmartTarget/TransformAngle 委派（各職業各自實作的任意程式碼）；NormalizeActionForQueue 與
    // GetAdjustedCastTime 則會踩到 CS 的 [MemberFunction] 特徵碼失效。
    // 🔴 self 與 GetConfigOption 的回傳值是裸指標，用判空處理、不包 try（AVE 攔不到）。
    private bool UseActionDetour(ActionManager* self, CSActionType actionType, uint actionId, ulong targetId, uint extraParam, ActionManager.UseActionMode mode, uint comboRouteId, bool* outOptAreaTargeted)
    {
        var origTargetId = targetId; // 覆寫可能只做到一半就失敗，Original 要拿完全沒被我們碰過的參數
        var haveTargetSystem = false; // 維持 false ＝ 走原本「targetSystem == null 就原封不動轉交」那條路
        var queued = false;
        var dashBlocked = false; // 位移技危險攔截；算不出來就維持 false ＝ 照舊放行
        try
        {
            var targetSystem = TargetSystem.Instance();
            haveTargetSystem = targetSystem != null;
            if (targetSystem != null)
            {
                var action = new ActionID((ActionType)actionType, actionId);
                //Service.Log($"[AMEx] UA: {action} @ {targetId:X}: {extraParam} {mode} {comboRouteId}");
                action = NormalizeActionForQueue(action);
                var spellId = GetSpellIdForAction(action);

                // if mouseover mode is enabled AND target is a usual primary target AND current mouseover is valid target for action, then we override target to mouseover
                var primaryTarget = targetSystem->Target;
                var primaryTargetId = primaryTarget != null ? primaryTarget->GetGameObjectId() : InvalidEntityId;
                var targetOverridden = targetId != primaryTargetId;
                var pronounModule = PronounModule.Instance();
                if (Config.PreferMouseover && !targetOverridden && pronounModule != null)
                {
                    var mouseoverTarget = pronounModule->UiMouseOverTarget;
                    if (mouseoverTarget != null && ActionManager.CanUseActionOnTarget(spellId, mouseoverTarget))
                    {
                        targetId = mouseoverTarget->GetGameObjectId();
                        targetOverridden = true;
                    }
                }

                (ulong, Vector3?) getAreaTarget() => targetOverridden ? (targetId, null) :
                    (Config.GTMode == ActionTweaksConfig.GroundTargetingMode.AtTarget ? targetId : InvalidEntityId, Config.GTMode == ActionTweaksConfig.GroundTargetingMode.AtCursor ? GetWorldPosUnderCursor() : null);

                ulong findNearestTarget()
                {
                    var fwk = Framework.Instance();
                    // GetConfigOption 會回 null（同一支呼叫在 UpdateDetour 也是判空的）
                    var autoNearest = fwk != null ? fwk->SystemConfig.GetConfigOption((uint)ConfigOption.AutoNearestTarget) : null;
                    if (_autoSelectTarget != null && autoNearest != null && autoNearest->Value.UInt == 1u)
                    {
                        _autoSelectTarget(targetSystem);
                        if (targetSystem->Target != null)
                            return targetSystem->Target->GetGameObjectId();
                    }

                    return InvalidEntityId;
                }

                // 位移技危險攔截：**一定要擺在手動佇列之前** —— 進了我們自己的佇列之後，實際執行是走
                // UpdateDetour → ExecuteAction → UseActionLocation，這一關就再也攔不到了。
                // 目標解析：targetId 為空時用玩家目前的硬目標（遊戲自己也是這樣解），否則用（可能被滑鼠指向覆寫過的）targetId。
                dashBlocked = _dashIntercept.ShouldBlock(action, targetId is 0u or InvalidEntityId ? primaryTargetId : targetId);

                // note: only standard mode can be filtered
                // note: current implementation introduces slight input lag (on button press, next autorotation update will pick state updates, which will be executed on next action manager update)
                queued = !dashBlocked && mode == ActionManager.UseActionMode.None && action.Type is ActionType.Spell or ActionType.Item && _manualQueue.Push(action, targetId, GetAdjustedCastTime(action) * 0.001f, !targetOverridden, getAreaTarget, findNearestTarget);
            }
        }
        catch (Exception ex)
        {
            // 退化行為：這一次按鍵不進我們的手動佇列、也不套滑鼠指向目標，直接以原始參數交給遊戲的原生佇列
            //（等於「手動佇列微調關掉」時的按鍵行為）。技能照樣打得出去 —— 位移攔截同樣退回「不攔」。
            targetId = origTargetId;
            queued = false;
            dashBlocked = false;
            DetourGuard.Report(nameof(UseActionDetour), ex);
        }

        if (dashBlocked)
            return false; // 吞掉這一發位移技（診斷已在 ShouldBlock 裡記過），下一幀安全就會自然放行

        if (queued)
            return false; // 已收進我們自己的佇列，遊戲不會看到這次按鍵

        if (!haveTargetSystem)
            return _useActionHook.Original(self, actionType, actionId, origTargetId, extraParam, mode, comboRouteId, outOptAreaTargeted);

        var areaTargeted = false;
        // note: the transform is applied only here, on the value handed to the game - the manual-queue branch above still sees the original mode
        var res = _useActionHook.Original(self, actionType, actionId, targetId, extraParam, _macroQueueTweak.TransformMode(mode), comboRouteId, &areaTargeted);
        if (outOptAreaTargeted != null)
            *outOptAreaTargeted = areaTargeted;
        // self 是遊戲以 thiscall 傳進來的 this 指標，照理不會是 null；但下面兩行是**我們自己的寫入**，
        // 判空的成本是一個比較，寫進 null+偏移的成本是 AVE（攔不到、整個遊戲直接沒了）
        if (self != null && areaTargeted && Config.GTMode == ActionTweaksConfig.GroundTargetingMode.AtCursor)
            self->AreaTargetingExecuteAtCursor = true;
        if (self != null && areaTargeted && Config.GTMode == ActionTweaksConfig.GroundTargetingMode.AtTarget)
            self->AreaTargetingExecuteAtObject = targetId;
        return res;
    }

    // fail-closed 約定（見 Util/DetourGuard.cs）。
    // 包住 Original 的是 try/finally、沒有 catch：只保證 EnterGameExecution/LeaveGameExecution 成對
    // （不成對會讓縮短詠唱洩漏到遊戲 UI 與其他外掛），刻意不動它。
    // location 是裸指標，CS 的宣告就是 `Vector3* location = null`：用預設值呼叫的人（遊戲內部路徑、其他外掛）會傳 null 進來。判空，不是包 try。
    private bool UseActionLocationDetour(ActionManager* self, CSActionType actionType, uint actionId, ulong targetId, Vector3* location, uint extraParam, byte a7)
    {
        // TargetSystem.Instance() 與 PlayerObject() 內的 GameObjectManager.Instance() 都是不帶 isPointer 的 [StaticAddress]：
        // 解析失敗只會擲 InvalidOperationException、永遠不會回 null（與 isPointer 那類相反）⇒ 放進 try。
        // PlayerObject() 裡的 gom != null 因此是死碼（留著當防禦）；真正會是 null 的是 IndexSorted[0].Value，那個判斷是必要的。
        TargetSystem* targetSystem;
        GameObject* player;
        try
        {
            targetSystem = TargetSystem.Instance();
            player = PlayerObject();
        }
        catch (Exception ex)
        {
            // 退化行為：兩者都當成拿不到 —— 這正是本函式原本就寫好的那條路（preventAutos 維持 false、
            // prevRot/currRot 維持 default，而 Preserve(default, default) 因 original == modified 是 no-op）。
            // 技能照常打出去，只是這一發不做自動攻擊抑制與旋轉還原。
            targetSystem = null;
            player = null;
            DetourGuard.Report(nameof(UseActionLocationDetour) + "(resolve)", ex);
        }
        var prevSeq = _inst != null ? _inst->LastUsedActionSequence : default;
        var prevRot = player != null ? player->Rotation.Radians() : default;
        var hardTarget = targetSystem != null ? targetSystem->Target : null;
        var preventAutos = false;
        try
        {
            // ShouldPreventAutoActivation 會讀 Lumina 的 Action 表 —— 台服表對不上時是擲例外，不是回 null
            preventAutos = targetSystem != null && _autoAutosTweak.ShouldPreventAutoActivation(ActionManager.GetSpellIdForAction(actionType, actionId));
        }
        catch (Exception ex)
        {
            // 退化行為：這一發不做「開打前不要誤觸自動攻擊」的抑制，其餘完全照舊
            DetourGuard.Report(nameof(UseActionLocationDetour) + "(pre)", ex);
        }
        if (preventAutos)
            targetSystem->Target = null;
        bool ret;
        // this is the only place where the game is allowed to observe a shortened cast time (it calls GetAdjustedCastTime internally to set up the cast timer);
        // everything outside this scope - our own GetAdjustedCastTime wrapper, the manual queue, the game's UI, other plugins - keeps seeing the original value
        _castTimeTweak.EnterGameExecution();
        try
        {
            ret = _useActionLocationHook.Original(self, actionType, actionId, targetId, location, extraParam, a7);
        }
        finally
        {
            _castTimeTweak.LeaveGameExecution();
            // 玩家的硬目標是我們暫時清掉的，還原一定要發生，所以放 finally；先 LeaveGameExecution 再還原目標。
            if (preventAutos)
                targetSystem->Target = hardTarget;
        }
        var currSeq = _inst != null ? _inst->LastUsedActionSequence : default;
        var currRot = player != null ? player->Rotation.Radians() : default;
        if (currSeq != prevSeq)
        {
            try
            {
                // location 判空：null 時退化成原點，等同「這是非範圍技」——註：_inst 為 null 時 prevSeq/currSeq
                // 都是 0，這裡進不來，所以 HandleActionRequest 裡的 _inst 解參考不需要另外判空
                HandleActionRequest(new((ActionType)actionType, actionId), currSeq, targetId, location != null ? *location : default, prevRot, currRot);
            }
            catch (Exception ex)
            {
                // 退化行為：技能遊戲照樣打出去，但 BossMod 的事件流沒收到這一發 —— 這次請求不進 replay、
                // 動畫鎖延遲估計少一個樣本、手動佇列裡的對應項目不會出列（它會自己到期）。遊戲本身不受影響。
                DetourGuard.Report(nameof(UseActionLocationDetour), ex);
            }
        }
        return ret;
    }

    // fail-closed 約定（見 Util/DetourGuard.cs）：自訂邏輯進 try、Original 留在 try 外。受管理例外來源：
    //   ① HandleActionRequest：會 Fire ActionRequestExecuted（BossMod 整條事件流），並呼叫 CanMoveWhileCasting（讀 Lumina 的 Action 表）
    //      與 GetRecastGroup（CS 的 [MemberFunction]，特徵碼失效時擲 InvalidOperationException，不是回 0）。
    //   ② HolsterActions 是 Span<byte>，索引子有邊界檢查：holsterIndex 越界（含 (int) 轉型變負）會擲 IndexOutOfRangeException。
    // self 與 _inst 是裸指標，用判空處理、不包 try（AVE 攔不到）；HandleActionRequest 整支都在解參考 _inst。
    private bool UseBozjaFromHolsterDirectorDetour(PublicContentBozja* self, uint holsterIndex, uint slot)
    {
        // 🔴 與 UseActionLocationDetour 同一個形狀：PlayerObject() 內的 GameObjectManager.Instance() 是
        //    不帶 isPointer 的 [StaticAddress]，特徵碼失效時**擲 InvalidOperationException**、不是回 null。
        // 它跑在 Original **之前**：逸出的話連失落技能都不會送出，所以放進 try。
        GameObject* player;
        try
        {
            player = PlayerObject();
        }
        catch (Exception ex)
        {
            // 退化行為：與「拿不到玩家物件」的既有路徑相同 —— prevRot/currRot 維持 default，
            // 這一發不做旋轉還原（Preserve(default, default) 是 no-op）。失落技能照常送出。
            player = null;
            DetourGuard.Report(nameof(UseBozjaFromHolsterDirectorDetour) + "(resolve)", ex);
        }
        var prevRot = player != null ? player->Rotation.Radians() : default;
        var res = _useBozjaFromHolsterDirectorHook.Original(self, holsterIndex, slot);
        var currRot = player != null ? player->Rotation.Radians() : default;
        if (res && self != null && _inst != null)
        {
            try
            {
                var entry = (BozjaHolsterID)self->State.HolsterActions[(int)holsterIndex];
                HandleActionRequest(ActionID.MakeBozjaHolster(entry, (int)slot), 0, InvalidEntityId, default, prevRot, currRot);
            }
            catch (Exception ex)
            {
                // 退化行為：寶物庫失落技能照樣打得出去（Original 已經跑完、遊戲已經送出請求），
                // 但這一發不進 BossMod 事件流 —— 不進 replay、手動佇列不會被 Pop、動畫鎖延遲少一個樣本、
                // 這一次的旋轉還原不生效。遊戲本身完全不受影響。
                DetourGuard.Report(nameof(UseBozjaFromHolsterDirectorDetour), ex);
            }
        }
        return res;
    }

    // TODO add to manual queue (and also add holsters)
    // fail-closed 約定（見 Util/DetourGuard.cs）：自訂邏輯進 try、Original 一律留在 try 外；
    // self 是裸指標，這兩支完全不解參考它（只原樣交回 Original），所以沒有判空的對象。
    // 旗標讀到 false ⇒ 這一發不是 BossMod 送的，記一行診斷分辨手動使用。
    private void UsePomanderDetour(InstanceContentDeepDungeon* self, uint slot)
    {
        var ours = _pomanderSendInFlight;
        _usePomanderHook.Original(self, slot);
        if (!ours)
        {
            try
            {
                Service.Logger.Information($"[AMEx][DD] 使用者手動使用魔陶器：槽位 {slot}");
            }
            catch (Exception ex)
            {
                DetourGuard.Report(nameof(UsePomanderDetour), ex);
            }
        }
    }

    private void UseStoneDetour(InstanceContentDeepDungeon* self, uint slot)
    {
        var ours = _stoneSendInFlight;
        _useStoneHook.Original(self, slot);
        if (!ours)
        {
            try
            {
                Service.Logger.Information($"[AMEx][DD] 使用者手動使用魔石／亞靈複製體：槽位 {slot}");
            }
            catch (Exception ex)
            {
                DetourGuard.Report(nameof(UseStoneDetour), ex);
            }
        }
    }

    // fail-closed 約定（見 Util/DetourGuard.cs）：自訂邏輯全部進 try、Original 一律照樣呼叫；
    // 前半段失敗時跳過後半段的動畫鎖調整（不拿沒算出來的值去改遊戲狀態）。不防 AccessViolationException。
    // 🔴 裸指標一律判空、不靠 try：header 是整段自訂處理唯一的資料來源；CS 的 Receive 註解寫明
    //    effects/targets 只保證有 header->NumTargets 個元素，而 targetPos「只有範圍技才有意義」；
    //    _inst 則是動畫鎖調整的寫入目標。任一不成立就整段不做，把封包原封不動交回遊戲。
    private void ProcessPacketActionEffectDetour(uint casterID, Character* casterObj, Vector3* targetPos, ActionEffectHandler.Header* header, ActionEffectHandler.TargetEffects* effects, GameObjectId* targets)
    {
        ActorCastEvent? info = null;
        float packetAnimLock = 0, prevAnimLock = 0;
        // NumTargets == 0 時下面的迴圈不會執行，effects/targets 是不是 null 就無所謂（(ulong*) 轉型不解參考）
        if (header != null && _inst != null && (header->NumTargets == 0 || (effects != null && targets != null)))
        {
            try
            {
                // notify listeners about the event
                // note: there's a slight difference with dispatching event from here rather than from packet processing (ActionEffectN) functions
                // 1. action id is already unscrambled
                // 2. this function won't be called if caster object doesn't exist
                // the last point is deemed to be minor enough for us to not care, as it simplifies things (no need to hook 5 functions)
                info = new ActorCastEvent(new((ActionType)header->ActionType, header->ActionId), header->AnimationTargetId, header->AnimationLock, header->NumTargets, targetPos != null ? *targetPos : default,
                    header->GlobalSequence, header->SourceSequence, Network.PacketDecoder.IntToFloatAngle(header->RotationInt));
                var rawEffects = (ulong*)effects;
                for (var i = 0; i < header->NumTargets; ++i)
                {
                    var targetEffects = new ActionEffects();
                    for (var j = 0; j < ActionEffects.MaxCount; ++j)
                        targetEffects[j] = rawEffects[i * 8 + j];
                    info.Targets.Add(new(targets[i], targetEffects));
                }
                ActionEffectReceived.Fire(casterID, info);

                packetAnimLock = header->AnimationLock;
                prevAnimLock = _inst->AnimationLock;
            }
            catch (Exception ex)
            {
                info = null; // 前半段沒跑完 -> 後半段的動畫鎖調整整段跳過
                DetourGuard.Report(nameof(ProcessPacketActionEffectDetour), ex);
            }
        }

        // call the hooked function and observe the effects
        _processPacketActionEffectHook.Original(casterID, casterObj, targetPos, header, effects, targets);

        // info != null 蘊含 header != null && _inst != null（上面那道閘門），所以下面兩者可以直接解參考
        if (info == null)
            return;

        try
        {
            var currAnimLock = _inst->AnimationLock;
            var uiState = UIState.Instance();

            // uiState 為 null（早期登入／換區／標題畫面）時一律當成「非玩家發動」：只記錄、不調整動畫鎖。
            if (uiState == null || casterID != uiState->PlayerState.EntityId || !ExpectAnimationLockUpdate(header))
            {
                // this action is either executed by non-player, or is non-player-initiated
                // TODO: reconsider the condition:
                // - do we want to do non-anim-lock related things (eg unblock movement override) when we get action with 'force anim lock' flag?
                if (currAnimLock != prevAnimLock)
                    Service.Log($"[AMEx] Animation lock updated by non-player-initiated action: #{header->SourceSequence} {casterID:X} {info.Action} {prevAnimLock:f3} -> {currAnimLock:f3}");
                return;
            }

            MoveMightInterruptCast = false; // slidecast window start
            _movement.MovementBlocked = false; // unblock input unconditionally on successful cast (I assume there are no instances where we need to immediately start next GCD?)

            // animation lock delay update
            var animLockReduction = _animLockTweak.Apply(header->SourceSequence, prevAnimLock, _inst->AnimationLock, packetAnimLock, header->AnimationLock, out var animLockDelay);
            _inst->AnimationLock -= animLockReduction;
            Service.Log($"[AMEx] AEP #{header->SourceSequence} {prevAnimLock:f3} {info.Action} -> ALock={currAnimLock:f3} (delayed by {animLockDelay:f3}) -> {_inst->AnimationLock:f3}), Flags={header->Flags:X}, CTR={CastTimeRemaining:f3}, GCD={GCD():f3}");
        }
        catch (Exception ex)
        {
            DetourGuard.Report(nameof(ProcessPacketActionEffectDetour) + "(post)", ex);
        }
    }

    private void HandleActionRequest(ActionID action, uint seq, ulong targetID, Vector3 targetPos, Angle prevRot, Angle currRot)
    {
        _manualQueue.Pop(action);
        _dashDodge.OnActionRequested(action);
        _animLockTweak.RecordRequest(seq, _inst->AnimationLock);
        _restoreRotTweak.Preserve(prevRot, currRot);
        _channelTweak.RecordRequest(action);
        MoveMightInterruptCast = CastTimeRemaining > 0 && !CanMoveWhileCasting(action);

        var recast = _inst->GetRecastGroupDetail(GetRecastGroup(action));

        if (CastTimeRemaining > 0)
            _inst->CastTimeElapsed += _cooldownTweak.Adjustment;
        else
            _inst->AnimationLock = Math.Max(0, _inst->AnimationLock - _cooldownTweak.Adjustment);

        if (recast != null)
            recast->Elapsed += _cooldownTweak.Adjustment;

        var (castElapsed, castTotal) = _inst->CastSpellId != 0 ? (_inst->CastTimeElapsed, _inst->CastTimeTotal) : (0, 0);
        var (recastElapsed, recastTotal) = recast != null ? (recast->Elapsed, recast->Total) : (0, 0);
        Service.Log($"[AMEx] UAL #{seq} {action} @ {targetID:X} / {Utils.Vec3String(targetPos)}, ALock={_inst->AnimationLock:f3}, CTR={CastTimeRemaining:f3}, CD={recastElapsed:f3}/{recastTotal:f3}, GCD={GCD():f3}");
        ActionRequestExecuted.Fire(new(action, targetID, targetPos, seq, _inst->AnimationLock, castElapsed, castTotal, recastElapsed, recastTotal));
    }

    // note: we can't rely on worldstate target id, it might not be updated when this is called
    // TODO: current implementation means that we'll check desired state twice (once before making a decision to start autos, then again in the hook)
    // fail-closed 約定（見 Util/DetourGuard.cs）：自訂邏輯進 try、**Original 一律留在 try 外**。
    // 受管理例外來源是 _autoAutosTweak.GetDesiredState —— 它讀 AIHints、對 ws.Actors 查表、走 LINQ
    // （player.Statuses.Any(...)）、呼叫 hints.FindEnemy，而且它讀的 Enabled 會呼叫
    // GameMain.IsInPvPInstance()（CS 的 [MemberFunction]，特徵碼失效時擲 InvalidOperationException）。
    // self 原封不動轉交給 Original、從不解參考；targetSystem 有判空。
    private bool SetAutoAttackStateDetour(AutoAttackState* self, bool value, bool sendPacket, bool isInstant)
    {
        var prevent = false;
        try
        {
            var targetSystem = TargetSystem.Instance();
            prevent = value && targetSystem != null && !_autoAutosTweak.GetDesiredState(true, targetSystem->GetTargetObjectId());
        }
        catch (Exception ex)
        {
            // 退化行為：這一次不抑制自動攻擊，等同「自動攻擊管理」關掉時的樣子。
            // 方向是刻意選的：算不出來就當 true 會讓玩家按了卻打不出去、而且他不會知道為什麼；
            // 不抑制最多是提早一拍開打，還在遊戲原本就允許的行為範圍內。
            prevent = false;
            DetourGuard.Report(nameof(SetAutoAttackStateDetour), ex);
        }

        if (prevent)
        {
            Service.Log($"[AMEx] Prevented starting autoattacks");
            return true;
        }
        return _setAutoAttackStateHook.Original(self, value, sendPacket, isInstant);
    }

    // just the LoS portion of ActionManager::GetActionInRangeOrLoS (which also checks range, which we don't care about, and also checks facing angle, which we don't care about)
    private static bool CheckActionLoS(ActionID action, ulong targetID)
    {
        var row = action.Type == ActionType.Spell ? Service.LuminaRow<Lumina.Excel.Sheets.Action>(action.ID) : null;
        if (row == null)
            return true; // unknown action, assume nothing

        if (!row.Value.RequiresLineOfSight)
            return true;

        var gom = GameObjectManager.Instance();
        if (gom == null)
            return true;

        var player = gom->Objects.IndexSorted[0].Value;
        var targetObj = gom->Objects.GetObjectByGameObjectId(targetID);
        if (player == null || targetObj == null || targetObj->EntityId == player->EntityId)
            return true;

        var playerPos = *player->GetPosition();
        var targetPos = *targetObj->GetPosition();

        playerPos.Y += 2;
        targetPos.Y += 2;

        var offset = targetPos - playerPos;
        var maxDist = offset.Magnitude;
        var direction = offset / maxDist;

        return !BGCollisionModule.RaycastMaterialFilter(playerPos, direction, out _, maxDist);
    }
}
