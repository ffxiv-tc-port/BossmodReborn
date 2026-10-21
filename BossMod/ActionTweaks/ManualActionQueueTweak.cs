namespace BossMod;

// Custom queue for manual actions.
// When running autorotation, we typically still want the ability to execute actions manually (e.g. if there is no plan available, or if some emergency happens).
// There are some problematic interactions with autorotation that this tweak solves:
// - typically we want to manually execute oGCDs, and typically we prefer slightly delaying them if it means not losing GCD uptime
// - however, we also want to give an emergency 'use asap' option (not worse than if you were to spam button without autorotation active)
// Tweak uses the following implementation:
// - maintain our own queue of manually requested actions, and route user-requested actions here instead of passing directly to the game
// - unlike native action queue, ours supports multiple pending entries
// - our queue distinguishes GCD and oGCD actions; since oGCDs can be delayed, effective 'expiration' time for oGCDs is much larger than native 0.5s
// - trying to queue an oGCD action while it is already queued (double tapping) activates 'emergency mode': all preceeding queued actions are removed and this action is returned even if it would delay GCD
// - entries from the manual queue are added to the autoqueue every frame with appropriate priorities, and usual logic selects best action to execute
// ActionManagerEx 在 detour 內（遊戲主執行緒）當幀取樣後交過來的快照：全是值型別，
// 這一側永遠不持有原生指標。Status 就是 LogMessage 的 row id，0＝遊戲說這一發可以用。
public readonly record struct ActionBlockInfo(uint Status, bool RecastActive, float GCDRemaining, float AnimationLock, float CastRemaining, bool Moving, bool MovementBlocked, float Range, float Distance, bool HasTarget)
{
    // 節流用的穩定鍵：只取離散成分 —— 動畫鎖與距離每幀都在變，放進鍵等於完全沒有節流
    public uint BlockKey => Status
        | (RecastActive ? 0x01000000u : 0u)
        | (AnimationLock > 0 ? 0x02000000u : 0u)
        | (CastRemaining > 0 ? 0x04000000u : 0u)
        | (Moving ? 0x08000000u : 0u)
        | (MovementBlocked ? 0x10000000u : 0u)
        | (HasTarget ? 0u : 0x20000000u)
        | (Range > 0 && Distance > Range ? 0x40000000u : 0u);

    public string Describe()
    {
        var status = Status != 0 ? $"status {Status} '{Service.LuminaRow<Lumina.Excel.Sheets.LogMessage>(Status)?.Text}'" : "status 0（遊戲說這一發可以用）";
        var tgt = !HasTarget ? "無目標" : Range > 0 ? $"dist={Distance:f2}/range={Range:f0}{(Distance > Range ? " 超出範圍" : "")}" : $"dist={Distance:f2}";
        return $"{status}, recast={(RecastActive ? "on" : "off")}, GCD={GCDRemaining:f2}, aLock={AnimationLock:f2}, cast={CastRemaining:f2}, moving={Moving}, moveBlocked={MovementBlocked}, {tgt}";
    }
}

public sealed class ManualActionQueueTweak(WorldState ws, AIHints hints, Func<ActionID, Actor?, ActionBlockInfo>? describeBlockers = null)
{
    private readonly record struct Entry(ActionID Action, Actor? Target, Vector3 TargetPos, Angle? FacingAngle, ActionDefinition Definition, DateTime ExpireAt, float CastTime, DateTime QueuedAt)
    {
        // 最近一次送來的、還沒換過的按鈕（一般動作與道具＝Action）；放出時從它重新換，不對已換過的 Action 再換
        public ActionID Original { get; init; }

        // 放出時遊戲會把它換成的技能（RefreshCastTimes 每幀寫、ResolveForExecution 放出時寫）；default＝還沒刷新過
        public ActionID Adjusted { get; init; }

        public readonly bool Expired(DateTime now) => ExpireAt < now || (Target?.IsDestroyed ?? false);
    }

    private readonly ActionTweaksConfig _config = Service.Config.Get<ActionTweaksConfig>();
    private readonly List<Entry> _queue = [];
    private bool _emergencyMode;

    // 上一次 FillQueue 交出去的每一項的原始按鈕：Draw 挑完到放出之間那一項被重送換掉時，放出照樣從它換
    private readonly List<(ActionID Action, ActionID Original)> _offered = [];

    /// <summary>
    /// true＝連按位移技不升級成緊急模式（BMR AI 開著、逃生鍵沒按著）。由 Draw 回呼每幀寫入，UseAction detour 讀。
    /// </summary>
    /// <remarks>緊急模式在 <c>ActionQueue.CanExecute</c> 直接放行、不跑落點檢查；外掛每 50 ms 重送同一招就等於永遠跳過。</remarks>
    public bool HoldDashEmergency;

    /// <summary>逃生鍵這一幀有沒有按著（不論 AI 開不開）。由 Draw 回呼每幀寫入，<see cref="HoldEmergencyForPyretic"/> 讀。</summary>
    public bool EscapeHatchHeld;

    // 緊急模式被熱病檢查擋下時的診斷節流：同一招、同一種結果（擋／逃生鍵放行），PyreticHoldLogInterval 秒內只印一次
    private const double PyreticHoldLogInterval = 5;
    private ActionID _pyreticHoldAction;
    private DateTime _pyreticHoldSince, _pyreticHoldLogTime;
    private bool _pyreticHoldForced;
    private int _pyreticHoldSuppressed;

    // 詠唱讓位：UpdateCastHazard 在 Draw 回呼（hints 剛建好、主執行緒）每幀寫，Push 只讀。
    // _castDeadline＝硬詠唱最晚要在這一刻結束，才來得及照尋路的要求走出腳下的危險區；MaxValue＝不讓位。
    private DateTime _castDeadline = DateTime.MaxValue;
    private DateTime _castHazardActivation;
    private float _castHazardExit, _castHazardSpeed;
    private bool _castHazardLookahead; // 期限來自閃避預判（接不上下一波），不是腳下的危險區
    private bool _castHazardPath; // 期限取自尋路的餘裕（安全處要繞路或得穿過別的區、或格子還沒走出去），比直線走出這一區早
    private bool _castHazardUnderfoot; // 玩家中心在已登記的危險區裡（_castHazardActivation／_castHazardExit 有值）
    private bool _castHazardLatched; // 期限來自「剛為了閃避取消了詠唱」的閂鎖，不是這一幀算出來的

    // 詠唱取消：已經在詠唱、危險才冒出來時，期限（只看腳下危險區與尋路，不看閃避預判）早於滑步點就請 ActionManagerEx 取消。
    // 取消後把那個期限閂住到那一波生效為止：期限一幀內忽有忽無時，重送的硬詠唱仍讓位，不會「取消→升緊急→又取消」來回。
    private const double CastCancelSlideSeconds = 0.5; // 與自動移動 Leeway 的取消條件相同：詠唱最後 0.5 秒可以滑步
    private DateTime _cancelLatchDeadline = DateTime.MaxValue, _cancelLatchUntil;
    private ActionID _cancelLogAction;
    private DateTime _cancelLogCastStart, _cancelLogTime;
    private int _cancelLogSuppressed;

    // 放出後一段時間不能操作的技能，讓位判定把它當成這麼長的詠唱。只能寫死：狂怒（41592）表上 StatusGainSelf=0，
    // 鎖操作來自它給自己的狀態 4235（Status 表 LockControl=True），時長不在任何表裡，實機量約 10 秒。
    private const float RageLockSeconds = 10f;
    private static readonly ActionID RageAction = ActionID.MakeSpell(Data.PhantomID.Rage);
    private ActionID _lockHoldAction;
    private DateTime _lockHoldSince, _lockHoldLogTime;
    private int _lockHoldSuppressed;

    /// <summary>「現在負責移動」那一份尋路的 Context；null＝不看閃避預判。由 Draw 回呼在 <see cref="UpdateCastHazard"/> 之前寫（主執行緒）。</summary>
    public Pathfinding.NavigationDecision.Context? CastHazardNavigation;

    private const double CastHoldLogInterval = 5;
    private ActionID _castHoldAction;
    private DateTime _castHoldSince, _castHoldLogTime;
    private int _castHoldSuppressed;

    // 執行前才發現詠唱時間變長而先不放（HoldAtExecution）的診斷節流：同一招 CastHoldLogInterval 秒內只印一次
    private ActionID _swapHoldAction;
    private DateTime _swapHoldLogTime;
    private int _swapHoldSuppressed;

    // 佇列裡的位移技被落點檢查擋下時的診斷節流：同一招、同一個原因，DashBlockLogInterval 秒內只印一次
    private const double DashBlockLogInterval = 5;
    private ActionID _dashBlockAction;
    private int _dashBlockReason;
    private DateTime _dashBlockLogTime;
    private DateTime _dashBlockSince;
    private int _dashBlockSuppressed;

    // 節流：同一支技能、同一組拒絕原因，EmergencyLogInterval 秒內只印一次。原因變了、換技能、
    // 或送出去／過期時立刻重置，所以「原因變了」與「恢復正常」都不會被吃掉；
    // 「卡了多久」則由 FlushEmergencyLog 的收尾行保住。
    private const double EmergencyLogInterval = 5;
    private ActionID _emergencyLogAction;
    private uint _emergencyLogKey;
    private int _emergencyLogNormalMask;
    private DateTime _emergencyLogTime;
    private int _emergencySuppressed;

    public void RemoveExpired()
    {
        if (_emergencyMode && _queue.Count == 0)
        {
            // 不變式是「緊急模式時 _queue[0] 就是那一發」；萬一被打破就沒有對象可緊急，退出並留一行紀錄
            Service.Logger.Information("[MAO] 緊急模式開著但手動佇列是空的（不應發生），已退出緊急模式");
            FlushEmergencyLog(_emergencyLogAction, "dropped");
            _emergencyMode = false;
        }
        else if (_emergencyMode && _queue[0].Expired(ws.CurrentTime))
        {
            Service.Log($"[MAO] Emergency {_queue[0].Action} expired");
            FlushEmergencyLog(_queue[0].Action, "expired");
            _emergencyMode = false;
        }

        bool checkExpired(Entry e)
        {
            if (e.Expired(ws.CurrentTime))
            {
                Service.Log($"[MAO] Action {e.Action} @ {e.Target} expired");
                FlushDashBlockLog(e.Action, "過期，這一發沒有放出");
                FlushCastHoldLog(e.Action, "過期，這一發沒有放出");
                FlushPyreticHoldLog(e.Action, "過期，這一發沒有放出");
                return true;
            }
            return false;
        }
        // 每幀都會走到：佇列是空的時候不必為了問一遍而建委派
        if (_queue.Count != 0)
            _queue.RemoveAll(checkExpired);
    }

    public void FillQueue(ActionQueue queue)
    {
        _offered.Clear();
        if (_emergencyMode && _queue.Count > 0)
        {
            ref var entry = ref _queue.Ref(0);
            if (!LockHeld(entry.Action))
            {
                queue.Push(entry.Action, entry.Target, ActionQueue.Priority.ManualEmergency, 0, 0, 0, entry.TargetPos, entry.FacingAngle, true);
                _offered.Add((entry.Action, entry.Original));
            }
        }
        else
        {
            float expireOrder = 0; // we don't actually care about values, only ordering...
            foreach (ref var e in _queue.AsSpan())
            {
                // 瞬發的鎖操作技能沒有 MaxCastTime 擋：讓位時乾脆不交給 FindBest
                if (LockHeld(e.Action))
                    continue;
                queue.Push(e.Action, e.Target, e.Definition.IsGCD ? ActionQueue.Priority.ManualGCD : ActionQueue.Priority.ManualOGCD, expireOrder++, 0, e.CastTime, e.TargetPos, e.FacingAngle, true);
                _offered.Add((e.Action, e.Original));
            }
        }
    }

    private static float LockAfterUse(ActionID action) => action == RageAction ? RageLockSeconds : 0f;

    // 讀 UpdateCastHazard 這一幀寫的期限；AI 關或逃生鍵按著時期限是 MaxValue ⇒ 永遠 false
    private bool LockHeld(ActionID action)
    {
        var lockTime = LockAfterUse(action);
        if (lockTime <= 0 || ws.CurrentTime.AddSeconds(lockTime) <= _castDeadline)
            return false;
        if (action != _lockHoldAction || _lockHoldSince == default)
            LogLockHold(action, lockTime); // 只記開始：沒有重送時也留得下一行，之後的節流行交給 Push
        return true;
    }

    // 呼叫端送的就是要排的技能、沒有另外的按鈕時：原始按鈕＝排的技能
    public bool Push(ActionID action, ulong targetId, float castTime, bool allowTargetOverride, Func<(ulong, Vector3?)> getAreaTarget, Func<ulong> targetNearest)
        => Push(action, action, targetId, castTime, allowTargetOverride, getAreaTarget, targetNearest);

    /// <summary>
    /// <paramref name="pressed"/>＝UseAction 收到、還沒換過的 ID；<paramref name="action"/>＝換過之後要排的技能（目標、冷卻、解鎖都照它判）。
    /// </summary>
    public bool Push(ActionID action, ActionID pressed, ulong targetId, float castTime, bool allowTargetOverride, Func<(ulong, Vector3?)> getAreaTarget, Func<ulong> targetNearest)
    {
        if (!_config.UseManualQueue)
            return false; // we don't use queue at all

        var player = ws.Party.Player();
        if (player == null)
            return false; // player is unknown, skip

        var def = ActionDefinitions.Instance[action];
        if (def == null)
            return false; // unknown action, let native queue handle it instead

        var isGCD = def.IsGCD;
        var expire = isGCD ? 1.0f : 3.0f;
        if (def.ReadyIn(ws.Client.Cooldowns, ws.Client.DutyActions) > expire)
            return false; // don't bother trying to queue something that's on cd

        if (!ResolveTarget(def, player, targetId, getAreaTarget, targetNearest, allowTargetOverride, out var target, out var targetPos))
            return false; // failed to resolve target

        var angleOverride = def.TransformAngle?.Invoke(ws, player, target, hints);
        // 只有技能從按鈕重新換；一般動作（極限技、衝刺）換成技能的那一步照舊用排隊時的結果
        var original = pressed.Type == ActionType.Spell && action.Type == ActionType.Spell ? pressed : action;

        var expireAt = ws.CurrentTime.AddSeconds(expire);
        var index = _queue.FindIndex(e => e.Definition.MainCooldownGroup == def.MainCooldownGroup); // TODO: what about alt groups and duty actions?..
        if (index < 0)
        {
            Service.Log(original == action ? $"[MAO] Queueing {action} @ {target}" : $"[MAO] Queueing {action} @ {target}（原始按鈕 {original}）");
            _queue.Add(new(action, target, targetPos, angleOverride, def, expireAt, castTime, ws.CurrentTime) { Original = original });
            return true;
        }

        ref var e = ref _queue.Ref(index);
        // e.CastTime 是上一幀照「放出時會換成的技能」刷新過的值：送來的 castTime 是對排隊時的技能算的，被別的插件換成硬詠唱時會是 0
        var effectiveCast = Math.Max(castTime, e.CastTime);
        if (e.Action != action || e.Target != target)
        {
            Service.Log($"[MAO] Replacing queued {e.Action} with {action} @ {target}");
            FlushCastHoldLog(e.Action, "被換成別的技能");
            FlushPyreticHoldLog(e.Action, "被換成別的技能，這一發沒有放出");
            _queue.RemoveAt(index);
            _queue.Add(new(action, target, targetPos, angleOverride, def, expireAt, castTime, ws.CurrentTime) { Original = original });
        }
        else if (HoldDashEmergency && IsDashCheckedBeforeRelease(def))
        {
            // 只刷新過期時間：留在一般優先度，放出前每一幀照 ForbidExecute 檢查落點（危險區、場地邊界、擊退）。
            // 先前按著逃生鍵進的緊急模式（_queue[0] 就是這一發）在放開後的下一次重送就退出。
            e = e with { ExpireAt = expireAt, Original = original };
            if (_emergencyMode && index == 0)
            {
                FlushEmergencyLog(action, "returned to normal queue");
                _emergencyMode = false;
            }
        }
        else if (Math.Max(effectiveCast, LockAfterUse(action)) is var holdTime && holdTime > 0 && ws.CurrentTime.AddSeconds(holdTime) > _castDeadline)
        {
            // 硬詠唱讓位給閃避：留在一般佇列（MaxCastTime 照舊把關；鎖操作技能由 FillQueue 擋）；詠唱時間跟著刷新，免得沿用第一次送出時的瞬發值。
            // 已在緊急模式的同一發退回一般佇列；危險過了下一次重送照常升緊急
            e = e with { ExpireAt = expireAt, CastTime = effectiveCast, Original = original };
            if (LockAfterUse(action) is var lockTime && lockTime > 0)
                LogLockHold(action, lockTime);
            else
                LogCastHold(action, effectiveCast, e.Adjusted);
            if (_emergencyMode && index == 0)
            {
                FlushEmergencyLog(action, "returned to normal queue");
                _emergencyMode = false;
            }
        }
        else
        {
            // 這是「e.Action == action && e.Target == target」的 else，兩者可互換
            var queuedAt = e.QueuedAt; // Clear 會把 e 指到的槽清成 default，先抄
            FlushCastHoldLog(action, "不再讓位（危險已過，或逃生鍵按著／AI 關了），照常升緊急模式");
            LogEmergency(action, target, def, effectiveCast, targetPos, angleOverride, queuedAt);
            if (_pyreticHoldAction != action)
                FlushPyreticHoldLog(_pyreticHoldAction, "被換成別的技能，這一發沒有放出");
            // spamming oGCD - enter emergency mode
            _queue.Clear();
            _queue.Add(new(action, target, targetPos, angleOverride, def, expireAt, castTime, queuedAt) { Original = original });
            _emergencyMode = true;
        }
        return true;
    }

    public void Pop(ActionID action)
    {
        // 遊戲回報的是換過之後的技能，所以也比 Adjusted（否則那一項會留到過期）；但先找排的就是這一招的那一項，
        // 否則別項（例如還在等 GCD 的）恰好會換成這一招時，會被當成已放出而移掉
        var index = _queue.FindIndex(e => e.Action == action);
        if (index < 0)
            index = _queue.FindIndex(e => e.Adjusted == action);
        if (index >= 0)
        {
            var queued = _queue[index].Action;
            var original = _queue[index].Original;
            Service.Log(queued == action && original == action ? $"[MAO] Executed {action}"
                : queued == action ? $"[MAO] Executed {action}（原始按鈕 {original}）"
                : $"[MAO] Executed {action}（排隊時是 {queued}、原始按鈕 {original}）");
            _queue.RemoveAt(index);
            FlushEmergencyLog(queued, "executed");
            FlushDashBlockLog(queued, "放出");
            FlushCastHoldLog(queued, "放出");
            FlushPyreticHoldLog(queued, "放出");
        }

        if (_emergencyMode && index == 0)
            _emergencyMode = false;
    }

    /// <summary>
    /// 每幀在 <c>ActionManagerEx.UpdateDetour</c>（框架執行緒、放出佇列之前）呼叫：照「現在從原始按鈕換過之後的技能」刷新每一項的詠唱時間。
    /// </summary>
    /// <remarks>
    /// 排隊時的詠唱時間是對當時的技能算的；別的插件 hook 了 GetAdjustedActionId（例如 WrathCombo 的連段按鈕，有節流快取）時，
    /// 放出那一刻可能換成硬詠唱。<paramref name="adjust"/> 必須與放出時用的換法相同（<see cref="ResolveForExecution"/>）；只存值，不存任何原生指標。
    /// </remarks>
    public void RefreshCastTimes(Func<ActionID, ActionID> adjust, Func<ActionID, float> castSeconds)
    {
        foreach (ref var e in _queue.AsSpan())
        {
            var adjusted = adjust(e.Original);
            if (CooldownGroupChanged(e.Action, adjusted))
                adjusted = e.Action; // 與 ResolveForExecution 同一條退回規則：詠唱時間照真的會送出的那一招
            var cast = castSeconds(adjusted);
            if (adjusted != e.Adjusted || cast != e.CastTime)
                e = e with { Adjusted = adjusted, CastTime = cast };
        }
    }

    /// <summary>
    /// 放出（含 UpdateDetour 預判「馬上要放」）時要從哪個 ID 換：手動佇列挑出的項＝它的原始按鈕，
    /// 其餘（BMR 自己的循環、AI，送的本來就是最終 ID）＝排的技能本身，與改動前相同。框架執行緒、只讀。
    /// </summary>
    public ActionID ExecutionSource(ActionQueue.Entry chosen)
    {
        if (!chosen.Manual)
            return chosen.Action;
        foreach (ref var e in _queue.AsSpan())
            if (e.Action == chosen.Action)
                return e.Original;
        foreach (var o in _offered)
            if (o.Action == chosen.Action)
                return o.Original;
        return chosen.Action; // 兩邊都找不到（不應發生）：照舊換排的技能
    }

    /// <summary>
    /// 放出那一刻（UpdateDetour、框架執行緒）決定送給遊戲的技能：從 <see cref="ExecutionSource"/> 換一次，拿到當下最新的替換
    /// （連段推進、別的插件的 GetAdjustedActionId hook）。不對已換過的技能再換——那會被同一個 hook 再換成別招。
    /// 手動項把結果記進 Adjusted，讓 <see cref="Pop"/> 認得放出的是哪一項。
    /// </summary>
    public ActionID ResolveForExecution(ActionQueue.Entry chosen, Func<ActionID, ActionID> adjust)
    {
        var adjusted = adjust(ExecutionSource(chosen));
        if (!chosen.Manual)
            return adjusted;
        if (CooldownGroupChanged(chosen.Action, adjusted))
        {
            LogCooldownGroupFallback(chosen.Action, adjusted);
            adjusted = chosen.Action;
        }
        foreach (ref var e in _queue.AsSpan())
        {
            if (e.Action != chosen.Action)
                continue;
            if (e.Adjusted != adjusted)
                e = e with { Adjusted = adjusted };
            break;
        }
        return adjusted;
    }

    // 排隊時驗的是排的那一招的冷卻；按鈕換出別的主冷卻組（例：單鍵替換在 GCD 與能力技之間改主意）時退回排的那一招，
    // 否則不是被拒絕到過期，就是 Pop 把 GCD 項當成已放出。任一邊沒有技能定義時判不了，照舊用換出來的。
    private static bool CooldownGroupChanged(ActionID queued, ActionID adjusted)
        => adjusted != queued && ActionDefinitions.Instance[queued] is { } q && ActionDefinitions.Instance[adjusted] is { } a && q.MainCooldownGroup != a.MainCooldownGroup;

    private ActionID _groupFallbackAction, _groupFallbackAdjusted;
    private DateTime _groupFallbackLogTime;

    private void LogCooldownGroupFallback(ActionID queued, ActionID adjusted)
    {
        var now = ws.CurrentTime;
        if (queued == _groupFallbackAction && adjusted == _groupFallbackAdjusted && (now - _groupFallbackLogTime).TotalSeconds < CastHoldLogInterval)
            return;
        _groupFallbackAction = queued;
        _groupFallbackAdjusted = adjusted;
        _groupFallbackLogTime = now;
        Service.Log($"[MAO] 放出時按鈕換成 {adjusted}，與排的 {queued} 主冷卻組不同 ⇒ 改送排的那一招");
    }

    /// <summary>
    /// 放出前最後一關（<c>UpdateDetour</c>、<see cref="RefreshCastTimes"/> 之後）：<paramref name="chosen"/> 是上一次 Draw 從手動佇列挑的，
    /// 刷新後的詠唱時間比挑的時候長就照現在的值重判；true＝這一幀先不放。
    /// </summary>
    /// <remarks>
    /// 一般佇列：超過 <paramref name="maxCastTime"/> 就不放（下一幀 FindBest 自己會跳過它）。緊急模式本來就不看 MaxCastTime：
    /// 只有「放出時會換成別的技能」而且讓位期限擋得到時才退回一般佇列；沒換技能的緊急模式照舊放。
    /// </remarks>
    public bool HoldAtExecution(ActionQueue.Entry chosen, float maxCastTime)
    {
        if (!chosen.Manual)
            return false;
        var index = -1;
        for (var i = 0; i < _queue.Count; ++i)
        {
            if (_queue[i].Action == chosen.Action)
            {
                index = i;
                break;
            }
        }
        if (index < 0)
            return false;
        ref var e = ref _queue.Ref(index);
        if (!(e.CastTime > chosen.CastTime))
            return false;
        if (_emergencyMode && index == 0)
        {
            if (e.Adjusted == default || e.Adjusted == e.Action || !(ws.CurrentTime.AddSeconds(e.CastTime) > _castDeadline))
                return false;
            LogCastHold(e.Action, e.CastTime, e.Adjusted);
            FlushEmergencyLog(e.Action, "returned to normal queue");
            _emergencyMode = false;
            return true;
        }
        if (!(e.CastTime > maxCastTime))
            return false;
        LogSwapHold(e.Action, e.Adjusted, e.CastTime, maxCastTime);
        return true;
    }

    private void LogSwapHold(ActionID action, ActionID adjusted, float castTime, float maxCastTime)
    {
        var now = ws.CurrentTime;
        if (action == _swapHoldAction && (now - _swapHoldLogTime).TotalSeconds < CastHoldLogInterval)
        {
            ++_swapHoldSuppressed;
            return;
        }
        var repeats = action == _swapHoldAction && _swapHoldSuppressed > 0 ? $"（上一行之後另有 {_swapHoldSuppressed} 次未印）" : "";
        _swapHoldAction = action;
        _swapHoldLogTime = now;
        _swapHoldSuppressed = 0;
        var what = adjusted != default && adjusted != action ? $"放出時會換成「{adjusted.Name()}」（{adjusted}），" : "";
        Service.Logger.Information($"[MAO][詠唱時間] 「{action.Name()}」（{action}）排隊時算的詠唱時間比現在短：{what}詠唱 {castTime:f2} 秒，超過這一幀的 MaxCastTime {maxCastTime:f2} ⇒ 先不放，照詠唱技處理（連送時照常升緊急模式、詠唱讓位照判）。{repeats}");
    }

    private void LogEmergency(ActionID action, Actor? target, ActionDefinition def, float castTime, Vector3 targetPos, Angle? angleOverride, DateTime queuedAt)
    {
        var info = describeBlockers?.Invoke(action, target);
        if (info != null)
            info = info.Value with { Range = def.Range };

        var key = info?.BlockKey ?? 0u;
        var now = ws.CurrentTime;
        var normalMask = NormalModeBlockMask(def, target, castTime, targetPos, angleOverride, info);
        var sameCause = action == _emergencyLogAction && key == _emergencyLogKey && normalMask == _emergencyLogNormalMask;
        if (sameCause && (now - _emergencyLogTime).TotalSeconds < EmergencyLogInterval)
        {
            ++_emergencySuppressed;
            return;
        }

        var repeats = sameCause ? $"（前 {(now - _emergencyLogTime).TotalSeconds:f0}s 內另有 {_emergencySuppressed} 次同因未印）" : "";
        _emergencyLogAction = action;
        _emergencyLogKey = key;
        _emergencyLogNormalMask = normalMask;
        _emergencyLogTime = now;
        _emergencySuppressed = 0;
        var normal = DescribeNormalModeBlock(normalMask, def, target, castTime, targetPos, info, (now - queuedAt).TotalMilliseconds);
        Service.Log($"[MAO] Entering emergency mode for {action}: {info?.Describe() ?? "拒絕原因不可得"}{repeats}；一般模式 {normal}");
    }

    // 「照一般模式（不升緊急）的話，這一發會被哪一道檢查擋住」：照 ActionQueue.FindBest／CanExecute 的順序逐條重算，只讀狀態。
    // 位元：1 未解鎖、2 MaxCastTime、4 未轉好（動畫鎖／冷卻）、8 坐騎、16 射程、32 ForbidExecute、64 遊戲回報不可用、128 熱病將至；0＝都沒擋、-1＝算不出來
    private const int NormalLocked = 1, NormalMaxCast = 2, NormalNotReady = 4, NormalMount = 8, NormalRange = 16, NormalForbid = 32, NormalGameStatus = 64, NormalPyretic = 128;

    private int NormalModeBlockMask(ActionDefinition def, Actor? target, float castTime, Vector3 targetPos, Angle? angleOverride, ActionBlockInfo? info)
    {
        try
        {
            var player = ws.Party.Player();
            if (player == null)
                return -1;
            var mask = 0;
            if (!def.IsUnlocked(ws, player))
                mask |= NormalLocked;
            if (castTime > hints.MaxCastTime)
                mask |= NormalMaxCast;
            if (Math.Max(info?.AnimationLock ?? 0, def.ReadyIn(ws.Client.Cooldowns, ws.Client.DutyActions)) > 0.05f)
                mask |= NormalNotReady;
            if (!_config.AutoDismount && AutoDismountTweak.IsMountPreventingAction(ws, def.ID))
                mask |= NormalMount;
            if (def.Range > 0)
            {
                var to = target?.Position ?? new(targetPos.XZ());
                var effRange = def.Range + player.HitboxRadius + (target?.HitboxRadius ?? 0);
                if ((to - player.Position).LengthSq() > effRange * effRange)
                    mask |= NormalRange;
            }
            var entry = new ActionQueue.Entry(def.ID, target, def.IsGCD ? ActionQueue.Priority.ManualGCD : ActionQueue.Priority.ManualOGCD, 0, 0, castTime, targetPos, angleOverride, true);
            if (def.ForbidExecute != null && def.ForbidExecute(ws, player, entry, hints))
                mask |= NormalForbid;
            if (info?.Status is > 0)
                mask |= NormalGameStatus;
            if (PyreticImminent())
                mask |= NormalPyretic;
            return mask;
        }
        catch (Exception)
        {
            return -1; // 只是診斷：算不出來就寫「原因=?」，不影響佇列
        }
    }

    // 名=值格式給 log 工具解析；原因照 FindBest 的檢查順序列，逗號分隔
    private string DescribeNormalModeBlock(int mask, ActionDefinition def, Actor? target, float castTime, Vector3 targetPos, ActionBlockInfo? info, double queuedMs)
    {
        if (mask < 0)
            return $"原因=? 排隊={queuedMs:f0}ms";
        if (mask == 0)
            return $"原因=都沒擋 排隊={queuedMs:f0}ms";
        try
        {
            var sb = new StringBuilder("原因=");
            void Add(string name)
            {
                if (sb.Length > 3)
                    sb.Append(',');
                sb.Append(name);
            }
            if ((mask & NormalLocked) != 0)
                Add("未解鎖");
            if ((mask & NormalMaxCast) != 0)
                Add("MaxCastTime");
            if ((mask & NormalNotReady) != 0)
                Add("未轉好");
            if ((mask & NormalMount) != 0)
                Add("坐騎");
            if ((mask & NormalRange) != 0)
                Add("射程");
            if ((mask & NormalForbid) != 0)
                Add("ForbidExecute");
            if ((mask & NormalGameStatus) != 0)
                Add("遊戲不可用");
            if ((mask & NormalPyretic) != 0)
                Add("熱病");
            if ((mask & NormalMaxCast) != 0)
                sb.Append($" 詠唱={castTime:f2} MaxCastTime={(hints.MaxCastTime == float.MaxValue ? "無上限" : hints.MaxCastTime.ToString("f2"))}");
            if ((mask & NormalNotReady) != 0)
                sb.Append($" 動畫鎖={info?.AnimationLock ?? 0:f2} 冷卻={def.ReadyIn(ws.Client.Cooldowns, ws.Client.DutyActions):f2}");
            if ((mask & NormalMount) != 0)
                sb.Append($" 坐騎={ws.Party.Player()?.MountId ?? 0} 自動下馬=關");
            if ((mask & NormalRange) != 0)
            {
                var player = ws.Party.Player();
                var to = target?.Position ?? new(targetPos.XZ());
                var dist = player != null ? (to - player.Position).Length() - player.HitboxRadius - (target?.HitboxRadius ?? 0) : 0;
                sb.Append($" 距離={dist:f2} 射程={def.Range:f0}");
            }
            if ((mask & NormalForbid) != 0)
                sb.Append($" 條件={(ActionDefinitions.TryGetDashGeometry(def.ForbidExecute, out var geo) ? $"位移安全({geo.Geometry})" : "技能自帶條件")}");
            if ((mask & NormalGameStatus) != 0)
                sb.Append($" 狀態={info?.Status ?? 0}");
            if ((mask & NormalPyretic) != 0)
                sb.Append($" 熱病剩={(hints.ImminentSpecialMode.activation - ws.CurrentTime).TotalSeconds:f2}");
            sb.Append($" 排隊={queuedMs:f0}ms");
            return sb.ToString();
        }
        catch (Exception)
        {
            return $"原因=? 排隊={queuedMs:f0}ms";
        }
    }

    // 有了節流之後「數 log 行數」不再等於「卡了多久」，所以結束時把吞掉的次數補報一行
    private void FlushEmergencyLog(ActionID action, string outcome)
    {
        if (_emergencySuppressed > 0)
            Service.Log($"[MAO] Emergency {action} {outcome}，期間另有 {_emergencySuppressed} 次同因未印");
        _emergencyLogAction = default;
        _emergencyLogKey = 0;
        _emergencyLogNormalMask = 0;
        _emergencySuppressed = 0;
    }

    // 與各位移 ForbidExecute 自己看的設定一致：衝向目標、朝目標固定距離兩型只要 DashSafety，其餘另外要 DashSafetyExtra
    private bool IsDashCheckedBeforeRelease(ActionDefinition def)
    {
        if (!_config.DashSafety || !ActionDefinitions.TryGetDashGeometry(def.ForbidExecute, out var geo))
            return false;
        return geo.Geometry is ActionDefinitions.DashGeometry.ToTargetHitbox or ActionDefinitions.DashGeometry.TowardTargetFixed || _config.DashSafetyExtra;
    }

    /// <summary>手動佇列裡的動作被 ForbidExecute 擋下時由 <c>ActionQueue.CanExecute</c> 呼叫（每幀、遊戲主執行緒）。只寫診斷，不影響判定。</summary>
    public void OnManualForbidden(ActionQueue.Entry entry, ActionDefinition def, Actor player, AIHints hints)
    {
        if (!ActionDefinitions.TryGetDashGeometry(def.ForbidExecute, out var geo))
            return;

        var now = ws.CurrentTime;
        int reason;
        string detail;
        try
        {
            reason = ActionDefinitions.DescribeDashDanger(geo, player, entry, hints, now, out detail);
        }
        catch (Exception ex)
        {
            // 跑在 ActionQueue.FindBest 裡：診斷擲例外會讓這一幀整個佇列選不出動作，所以就地吞掉
            reason = -2;
            detail = $"原因算不出來（{ex.GetType().Name}）";
        }
        if (entry.Action != _dashBlockAction || _dashBlockSince == default)
        {
            _dashBlockAction = entry.Action;
            _dashBlockSince = now;
            _dashBlockReason = -1;
            _dashBlockSuppressed = 0;
        }
        if (reason == _dashBlockReason && (now - _dashBlockLogTime).TotalSeconds < DashBlockLogInterval)
        {
            ++_dashBlockSuppressed;
            return;
        }

        var repeats = _dashBlockSuppressed > 0 ? $"（上一行之後另有 {_dashBlockSuppressed} 幀被擋未印）" : "";
        _dashBlockReason = reason;
        _dashBlockLogTime = now;
        _dashBlockSuppressed = 0;
        Service.Logger.Information($"[MAO][位移安全] 「{entry.Action.Name()}」（{entry.Action}）留在佇列、這一幀不放出：{detail}。安全時會自動放出；要強制放出請按住逃生鍵。{repeats}");
    }

    private void FlushDashBlockLog(ActionID action, string outcome)
    {
        if (action != _dashBlockAction || _dashBlockSince == default)
            return;
        var repeats = _dashBlockSuppressed > 0 ? $"（上一行之後另有 {_dashBlockSuppressed} 幀被擋未印）" : "";
        Service.Logger.Information($"[MAO][位移安全] 「{action.Name()}」（{action}）被落點檢查擋了 {(ws.CurrentTime - _dashBlockSince).TotalSeconds:f1} 秒後{outcome}。{repeats}");
        _dashBlockAction = default;
        _dashBlockSince = default;
        _dashBlockReason = -1;
        _dashBlockSuppressed = 0;
    }

    // 與 ActionManagerEx.FinishActionGather 擋一般模式的熱病判斷逐字相同（門檻 0＝關）
    private bool PyreticImminent() => _config.PyreticThreshold > 0 && hints.ImminentSpecialMode.mode == AIHints.SpecialMode.Pyretic && hints.ImminentSpecialMode.activation < ws.FutureTime(_config.PyreticThreshold);

    /// <summary>
    /// 緊急模式那一發被 FindBest 選中後由 <c>ActionManagerEx.FinishActionGather</c> 呼叫（Draw 回呼、主執行緒）：
    /// 熱病將至且逃生鍵沒按著 ⇒ true＝這一幀不放、留在佇列（熱病過了照常放出）。判斷擲例外回 false，照舊放出。
    /// </summary>
    public bool HoldEmergencyForPyretic(ActionQueue.Entry chosen)
    {
        bool imminent;
        try
        {
            imminent = PyreticImminent();
        }
        catch (Exception)
        {
            return false;
        }
        if (!imminent)
            return false;
        try
        {
            LogPyreticHold(chosen.Action, (hints.ImminentSpecialMode.activation - ws.CurrentTime).TotalSeconds, EscapeHatchHeld);
        }
        catch (Exception)
        {
            // 只是診斷：寫不出來不改判定
        }
        return !EscapeHatchHeld;
    }

    private void LogPyreticHold(ActionID action, double secondsLeft, bool forced)
    {
        var now = ws.CurrentTime;
        if (action != _pyreticHoldAction)
        {
            FlushPyreticHoldLog(_pyreticHoldAction, "被換成別的技能，這一發沒有放出");
            _pyreticHoldAction = action;
        }
        if (!forced && _pyreticHoldSince == default)
            _pyreticHoldSince = now;
        if (forced == _pyreticHoldForced && _pyreticHoldLogTime != default && (now - _pyreticHoldLogTime).TotalSeconds < PyreticHoldLogInterval)
        {
            ++_pyreticHoldSuppressed;
            return;
        }

        var repeats = _pyreticHoldSuppressed > 0 ? $"（上一行之後另有 {_pyreticHoldSuppressed} 幀未印）" : "";
        _pyreticHoldForced = forced;
        _pyreticHoldLogTime = now;
        _pyreticHoldSuppressed = 0;
        Service.Logger.Information(forced
            ? $"[MAO][熱病] 熱病將至（{secondsLeft:f2} 秒後到期），逃生鍵按著 ⇒ 緊急模式照常放出：「{action.Name()}」（{action}）。{repeats}"
            : $"[MAO][熱病] 熱病將至（{secondsLeft:f2} 秒後到期），緊急模式也先不放：「{action.Name()}」（{action}）留在佇列，熱病過了照常放出；要強制放出請按住逃生鍵。{repeats}");
    }

    private void FlushPyreticHoldLog(ActionID action, string outcome)
    {
        if (action != _pyreticHoldAction || _pyreticHoldAction == default)
            return;
        if (_pyreticHoldSince != default)
        {
            var repeats = _pyreticHoldSuppressed > 0 ? $"（上一行之後另有 {_pyreticHoldSuppressed} 幀未印）" : "";
            var forced = _pyreticHoldForced ? "（逃生鍵按著）" : "";
            Service.Logger.Information($"[MAO][熱病] 「{action.Name()}」（{action}）被熱病檢查擋了 {(ws.CurrentTime - _pyreticHoldSince).TotalSeconds:f1} 秒後{outcome}{forced}。{repeats}");
        }
        _pyreticHoldAction = default;
        _pyreticHoldSince = default;
        _pyreticHoldLogTime = default;
        _pyreticHoldForced = false;
        _pyreticHoldSuppressed = 0;
    }

    /// <summary>
    /// 每幀在 Draw 回呼裡、<c>AIHintsBuilder.Update</c> 之後、循環模組之前呼叫（主執行緒）：算出硬詠唱最晚何時結束，
    /// 並把 <c>hints.MaxCastTime</c> 壓到不超過它（一般佇列與 BMR 自己的循環也不起會吃掉動身時間的詠唱）。
    /// <paramref name="active"/> 為 false（AI 關著或逃生鍵按著）時不讓位、不動 MaxCastTime，升緊急模式照舊。
    /// 已在詠唱而期限早於它的滑步點時設 <c>hints.ForceCancelCast</c>（ActionManagerEx 的 Update 路徑下一幀取消）。
    /// </summary>
    /// <remarks>
    /// 生效時間與 <c>NavigationDecision.RasterizeForbiddenZones</c> 相同（改那邊要跟著改）：夾在 現在～現在+120 秒、0.5 秒內成群取最早、
    /// 再減 <c>ActivationTimeCushion</c>（自動移動用的預設 1 秒）。腳下＝玩家座標在區內（形狀距離 &lt; 0），走出去的時間＝直線；
    /// 負責移動的尋路發布了腳下格子的餘裕（繞路、走出去又落進別的區、中心剛出界但格子還沒走出去）時取兩者較早的。
    /// </remarks>
    public void UpdateCastHazard(bool active)
    {
        _castDeadline = DateTime.MaxValue;
        _castHazardLookahead = false;
        _castHazardPath = false;
        _castHazardUnderfoot = false;
        _castHazardLatched = false;
        var player = active ? ws.Party.Player() : null;
        if (player == null)
        {
            _cancelLatchUntil = default; // 逃生鍵按著或 AI 關 ⇒ 閂鎖作廢
            return;
        }
        var zones = hints.ForbiddenZones;
        var count = zones.Count;
        var now = ws.CurrentTime;
        if (count == 0)
        {
            // 危險區一幀內全部不見（模組或 hints 那一幀擲例外也是這樣）時閂鎖照舊有效
            ApplyCancelLatch(now);
            return;
        }

        var speed = ws.Client.MoveSpeed;
        if (speed is not (> 0.1f and < 200f)) // 與 NavigationDecision 換成名目速度的範圍相同
            speed = Pathfinding.NavigationDecision.NominalPlayerSpeed;
        var pos = player.Position;
        DateTime globalEnd = now.AddSeconds(120d), clusterEnd = default, clusterStart = default;
        try
        {
            for (var i = 0; i < count; ++i)
            {
                var z = zones[i];
                var activation = z.activation.Clamp(now, globalEnd);
                if (activation > clusterEnd)
                {
                    clusterStart = activation;
                    clusterEnd = activation.AddSeconds(0.5d);
                }
                var d = z.shapeDistance(pos);
                if (!(d < 0f))
                    continue;
                var exit = float.IsFinite(d) ? -d : 0f;
                var g = Math.Max(0d, (clusterStart - now).TotalSeconds - Pathfinding.NavigationDecision.ActivationTimeCushion);
                var deadline = now.AddSeconds(g - exit / speed);
                if (deadline < _castDeadline)
                {
                    _castDeadline = deadline;
                    _castHazardActivation = clusterStart;
                    _castHazardExit = exit;
                    _castHazardSpeed = speed;
                }
            }
            _castHazardUnderfoot = _castDeadline != DateTime.MaxValue;
            var nav = CastHazardNavigation;
            // 直線走出這一區是最樂觀的估計；中心剛出界、格子還沒走出去時也照尋路（這時起詠唱會把人釘在邊上）
            if (nav != null && nav.TryGetPathDeadline(now, out var pathBy) && pathBy < _castDeadline)
            {
                _castDeadline = pathBy;
                _castHazardPath = true;
            }
            // 取消進行中的詠唱只看到這裡為止：閃避預判比較悲觀，拿它取消會打斷其實來得及的詠唱
            var cancelBy = _castDeadline;
            var cancelPath = _castHazardPath;
            // 閃避預判要求在某個時間前動身（目前位置接不上下一波，或接得上但還要走一段）：詠唱要在那之前結束
            if (nav != null && nav.TryGetLookaheadDeadline(now, out var moveBy) && moveBy < _castDeadline)
            {
                _castDeadline = moveBy;
                _castHazardLookahead = true;
            }
            ApplyCancelLatch(now);
            if (cancelBy != DateTime.MaxValue && player.CastInfo is { } cast && WantCancelCast(cast, cancelBy, now))
            {
                hints.ForceCancelCast = true;
                // 閂到那一波生效為止（中心已出界、只剩尋路期限時沒有生效時間，用期限加回緩衝估）；至少 0.5 秒、至多 5 秒。
                // 取消生效前會連續幾幀成立：已閂住時只延長、不縮短
                var until = (_castHazardUnderfoot ? _castHazardActivation : cancelBy.AddSeconds(Pathfinding.NavigationDecision.ActivationTimeCushion)).Clamp(now.AddSeconds(0.5d), now.AddSeconds(5d));
                var latched = now < _cancelLatchUntil;
                _cancelLatchUntil = latched && _cancelLatchUntil > until ? _cancelLatchUntil : until;
                _cancelLatchDeadline = latched && _cancelLatchDeadline < cancelBy ? _cancelLatchDeadline : cancelBy;
                LogCastCancel(cast, cancelBy, cancelPath, now);
                // 詠唱中重送的硬詠唱已升了緊急模式（緊急不看 MaxCastTime）：不退回一般佇列的話，取消後下一幀就又起詠唱
                if (_emergencyMode && _queue.Count > 0 && Math.Max(_queue[0].CastTime, LockAfterUse(_queue[0].Action)) > 0)
                {
                    FlushEmergencyLog(_queue[0].Action, "returned to normal queue");
                    _emergencyMode = false;
                }
            }
        }
        catch
        {
            _castDeadline = DateTime.MaxValue; // 算不出來＝不讓位（舊行為）；例外交給 DrawUI 的節流 log
            throw;
        }
    }

    // 取消過詠唱、那一波還沒過去 ⇒ 期限至少閂在取消時的值；再把 MaxCastTime 壓進期限（緊急模式不看 MaxCastTime，那一支由 Push 擋）
    private void ApplyCancelLatch(DateTime now)
    {
        if (now < _cancelLatchUntil && _cancelLatchDeadline < _castDeadline)
        {
            _castDeadline = _cancelLatchDeadline;
            _castHazardLatched = true;
        }
        if (_castDeadline != DateTime.MaxValue)
            hints.MaxCastTime = Math.Min(hints.MaxCastTime, Math.Max(0f, (float)(_castDeadline - now).TotalSeconds));
    }

    // 只取消技能的詠唱（坐騎、道具不碰）；刻意不取消傳送 5／返回 6／新月島亞返回 41343：別的插件會自動重試、取消會跟它來回，
    // 而且唸完人就被傳走，本身就是在躲
    private const uint TeleportSpell = 5, ReturnSpell = 6, OccultReturnSpell = 41343;

    private static bool WantCancelCast(ActorCastInfo cast, DateTime cancelBy, DateTime now)
        => !cast.EventHappened && cast.Action.Type == ActionType.Spell && cast.Action.ID is not (TeleportSpell or ReturnSpell or OccultReturnSpell)
        && cancelBy < now.AddSeconds(cast.RemainingTime - CastCancelSlideSeconds);

    // 同一發詠唱只印一行（取消生效前會連續幾幀成立）；不同發之間至少隔 1 秒，吞掉的次數補在下一行
    private void LogCastCancel(ActorCastInfo cast, DateTime cancelBy, bool path, DateTime now)
    {
        var castStart = now.AddSeconds(-cast.ElapsedTime);
        if (cast.Action == _cancelLogAction && Math.Abs((castStart - _cancelLogCastStart).TotalSeconds) < 0.1d)
            return;
        _cancelLogAction = cast.Action;
        _cancelLogCastStart = castStart;
        if (_cancelLogTime != default && (now - _cancelLogTime).TotalSeconds < 1d)
        {
            ++_cancelLogSuppressed;
            return;
        }
        var repeats = _cancelLogSuppressed > 0 ? $"（上一行之後另有 {_cancelLogSuppressed} 發取消未印）" : "";
        _cancelLogTime = now;
        _cancelLogSuppressed = 0;
        var moveIn = Math.Max(0d, (cancelBy - now).TotalSeconds);
        var late = cast.RemainingTime - CastCancelSlideSeconds - (cancelBy - now).TotalSeconds;
        var inSec = Math.Max(0d, (_castHazardActivation - now).TotalSeconds);
        var why = path
            ? (_castHazardUnderfoot
                ? $"腳下的危險區 {inSec:f2} 秒後生效，安全處要繞路或得穿過別的危險區，尋路要求 {moveIn:f2} 秒內動身"
                : $"腳下貼著危險區邊緣（中心已在外、尋路的格子還沒走出去），尋路要求 {moveIn:f2} 秒內動身")
            : $"腳下的危險區 {inSec:f2} 秒後生效（尋路要求提早 {Pathfinding.NavigationDecision.ActivationTimeCushion:f1} 秒離開），走出去 {_castHazardExit:f1} 碼約 {_castHazardExit / _castHazardSpeed:f2} 秒";
        Service.Logger.Information($"[MAO][詠唱取消] 「{cast.Action.Name()}」（{cast.Action}）還要詠唱 {cast.RemainingTime:f2} 秒，來不及閃（原因=腳下危險區）：{why}；可以滑步的那一刻比動身期限晚 {late:f2} 秒 ⇒ 取消詠唱讓 AI 去閃，那一波過去前重送的硬詠唱照讓位處理。要硬撐完請按住逃生鍵。{repeats}");
    }

    private void LogCastHold(ActionID action, float castTime, ActionID adjusted = default)
    {
        var now = ws.CurrentTime;
        if (action != _castHoldAction || _castHoldSince == default)
        {
            _castHoldAction = action;
            _castHoldSince = now;
            _castHoldLogTime = default;
            _castHoldSuppressed = 0;
        }
        if (_castHoldLogTime != default && (now - _castHoldLogTime).TotalSeconds < CastHoldLogInterval)
        {
            ++_castHoldSuppressed;
            return;
        }

        var repeats = _castHoldSuppressed > 0 ? $"（上一行之後另有 {_castHoldSuppressed} 次重送未印）" : "";
        _castHoldLogTime = now;
        _castHoldSuppressed = 0;
        var swap = adjusted != default && adjusted != action ? $"，放出時會換成「{adjusted.Name()}」" : "";
        if (_castHazardLatched)
        {
            Service.Logger.Information($"[MAO][詠唱讓位] 「{action.Name()}」（{action}{swap}）詠唱 {castTime:f2} 秒會來不及閃（原因=剛為了閃避取消詠唱）：{LatchDetail(now)} ⇒ 不升緊急模式、留在佇列，那一波過去照常放出。要強制放出請按住逃生鍵。{repeats}");
            return;
        }
        if (_castHazardLookahead)
        {
            Service.Logger.Information($"[MAO][詠唱讓位] 「{action.Name()}」（{action}{swap}）詠唱 {castTime:f2} 秒會來不及閃（原因=接不上下一波）：閃避預判要求 {Math.Max(0d, (_castDeadline - now).TotalSeconds):f2} 秒內動身才接得上下一波 ⇒ 不升緊急模式、留在佇列，來得及了照常放出。要強制放出請按住逃生鍵。{repeats}");
            return;
        }
        var inSec = Math.Max(0d, (_castHazardActivation - now).TotalSeconds);
        if (_castHazardPath)
        {
            Service.Logger.Information($"[MAO][詠唱讓位] 「{action.Name()}」（{action}{swap}）詠唱 {castTime:f2} 秒會來不及閃（原因=腳下危險區）：{PathHoldDetail(now, inSec)} ⇒ 不升緊急模式、留在佇列，危險過了照常放出。要強制放出請按住逃生鍵。{repeats}");
            return;
        }
        Service.Logger.Information($"[MAO][詠唱讓位] 「{action.Name()}」（{action}{swap}）詠唱 {castTime:f2} 秒會來不及閃（原因=腳下危險區）：腳下的危險區 {inSec:f2} 秒後生效（尋路要求提早 {Pathfinding.NavigationDecision.ActivationTimeCushion:f1} 秒離開），走出去 {_castHazardExit:f1} 碼約 {_castHazardExit / _castHazardSpeed:f2} 秒 ⇒ 不升緊急模式、留在佇列，危險過了照常放出。要強制放出請按住逃生鍵。{repeats}");
    }

    private string LatchDetail(DateTime now)
        => $"取消詠唱時算的動身期限還剩 {Math.Max(0d, (_cancelLatchDeadline - now).TotalSeconds):f2} 秒，那一波約 {Math.Max(0d, (_cancelLatchUntil - now).TotalSeconds):f2} 秒後才過去";

    private string PathHoldDetail(DateTime now, double inSec) => _castHazardUnderfoot
        ? $"腳下的危險區 {inSec:f2} 秒後生效，安全處要繞路或得穿過別的危險區，尋路要求 {Math.Max(0d, (_castDeadline - now).TotalSeconds):f2} 秒內動身（直線走出這一區只要 {_castHazardExit:f1} 碼約 {_castHazardExit / _castHazardSpeed:f2} 秒）"
        : $"腳下貼著危險區邊緣（中心已在外、尋路的格子還沒走出去），尋路要求 {Math.Max(0d, (_castDeadline - now).TotalSeconds):f2} 秒內動身";

    private void FlushCastHoldLog(ActionID action, string outcome)
    {
        FlushLockHoldLog(action, outcome);
        if (action != _castHoldAction || _castHoldSince == default)
            return;
        var repeats = _castHoldSuppressed > 0 ? $"（上一行之後另有 {_castHoldSuppressed} 次重送未印）" : "";
        Service.Logger.Information($"[MAO][詠唱讓位] 「{action.Name()}」（{action}）讓位 {(ws.CurrentTime - _castHoldSince).TotalSeconds:f1} 秒後{outcome}。{repeats}");
        _castHoldAction = default;
        _castHoldSince = default;
        _castHoldLogTime = default;
        _castHoldSuppressed = 0;
    }

    // 與 LogCastHold 分開節流：硬詠唱與狂怒可能同時在佇列裡讓位，共用一組狀態會兩邊輪流重置、每幀各印一行
    private void LogLockHold(ActionID action, float lockTime)
    {
        var now = ws.CurrentTime;
        if (action != _lockHoldAction || _lockHoldSince == default)
        {
            _lockHoldAction = action;
            _lockHoldSince = now;
            _lockHoldLogTime = default;
            _lockHoldSuppressed = 0;
        }
        if (_lockHoldLogTime != default && (now - _lockHoldLogTime).TotalSeconds < CastHoldLogInterval)
        {
            ++_lockHoldSuppressed;
            return;
        }

        var repeats = _lockHoldSuppressed > 0 ? $"（上一行之後另有 {_lockHoldSuppressed} 次重送未印）" : "";
        _lockHoldLogTime = now;
        _lockHoldSuppressed = 0;
        var why = _castHazardLatched
            ? $"（原因=剛為了閃避取消詠唱）：{LatchDetail(now)}"
            : _castHazardLookahead
            ? $"（原因=接不上下一波）：閃避預判要求 {Math.Max(0d, (_castDeadline - now).TotalSeconds):f2} 秒內動身才接得上下一波"
            : _castHazardPath
                ? $"（原因=腳下危險區）：{PathHoldDetail(now, Math.Max(0d, (_castHazardActivation - now).TotalSeconds))}"
                : $"（原因=腳下危險區）：腳下的危險區 {Math.Max(0d, (_castHazardActivation - now).TotalSeconds):f2} 秒後生效（尋路要求提早 {Pathfinding.NavigationDecision.ActivationTimeCushion:f1} 秒離開），走出去 {_castHazardExit:f1} 碼約 {_castHazardExit / _castHazardSpeed:f2} 秒";
        Service.Logger.Information($"[MAO][鎖操作讓位] 「{action.Name()}」（{action}）放出後約 {lockTime:f0} 秒不能操作，會來不及閃{why} ⇒ 留在佇列、先不放出，來得及了照常放出。要強制放出請按住逃生鍵。{repeats}");
    }

    private void FlushLockHoldLog(ActionID action, string outcome)
    {
        if (action != _lockHoldAction || _lockHoldSince == default)
            return;
        var repeats = _lockHoldSuppressed > 0 ? $"（上一行之後另有 {_lockHoldSuppressed} 次重送未印）" : "";
        Service.Logger.Information($"[MAO][鎖操作讓位] 「{action.Name()}」（{action}）讓位 {(ws.CurrentTime - _lockHoldSince).TotalSeconds:f1} 秒後{outcome}。{repeats}");
        _lockHoldAction = default;
        _lockHoldSince = default;
        _lockHoldLogTime = default;
        _lockHoldSuppressed = 0;
    }

    private bool ResolveTarget(ActionDefinition def, Actor player, ulong targetId, Func<(ulong, Vector3?)> getAreaTarget, Func<ulong> targetNearest, bool allowSmartTarget, out Actor? target, out Vector3 targetPos)
    {
        target = null;
        targetPos = default;

        // ground targeted actions that must target specific objects
        if (def.ID.ID == (uint)BLM.AID.BetweenTheLines)
        {
            var playerLL = ws.Actors.FirstOrDefault(act => act.OwnerID == player.InstanceID && act.OID == 0x179);
            if (playerLL == null)
                return false;

            targetPos = playerLL.PosRot.XYZ();
            return true;
        }

        if (def.ID.ID == (uint)RPR.AID.Regress)
        {
            var playerGate = ws.Actors.FirstOrDefault(act => act.OwnerID == player.InstanceID && act.OID == 0x4C3);
            if (playerGate == null)
                return false;

            targetPos = playerGate.PosRot.XYZ();
            return true;
        }

        if (def.AllowedTargets.HasFlag(ActionTargets.Area))
        {
            // GT actions with range 0 must be cast on player - there are only a few of these (BLM leylines, PCT leylines, PCT PVP limit break)
            if (def.Range == 0)
            {
                targetPos = player.PosRot.XYZ();
                return true;
            }

            // ground-targeted actions have special targeting
            var (gtTarget, gtPos) = getAreaTarget();
            if (gtPos != null)
            {
                // auto cast at cursor
                targetPos = gtPos.Value;
                return true;
            }
            else if (gtTarget is not 0 and not 0xE0000000)
            {
                var t = ws.Actors.Find(gtTarget);
                if (t != null)
                {
                    // auto cast at target's position
                    targetPos = t.PosRot.XYZ();
                    return true;
                }
                return false; // if target isn't found in world, bail
            }
            else
            {
                return false; // manual targeting desired
            }
        }

        if (def.AllowedTargets == ActionTargets.Self)
        {
            // the action can only target player, don't bother with other logic...
            target = player;
            return true;
        }

        target = ws.Actors.Find(targetId);
        if (target == null && targetId is not 0u and not 0xE0000000)
            return false; // target is valid, but not found in world, bail... (TODO this shouldn't be happening really)

        // custom smart-targeting
        if (allowSmartTarget && _config.SmartTargets && def.SmartTarget != null)
            target = def.SmartTarget(ws, player, target, hints);

        // fallback: if requested, use native "target nearest" function to try to find a valid hostile target
        // this conditional ensures we don't get a false positive for holmgang (can target self or hostile) or phantom oracle invuln (can target ally, but not self)
        if (target == null && def.AllowedTargets.HasFlag(ActionTargets.Hostile) && !def.AllowedTargets.HasFlag(ActionTargets.Self))
        {
            target = ws.Actors.Find(targetNearest());
            return true;
        }

        // smart-targeting fallback: cast on self if target is not valid
        var targetInvalid = target == null || !def.AllowedTargets.HasFlag(ActionTargets.Hostile) && !target.IsAlly;
        if (targetInvalid && def.AllowedTargets.HasFlag(ActionTargets.Self))
            target = player;

        return true;
    }
}
