using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Ipc.Exceptions;

namespace BossMod;

/// <summary>
/// 位移閃避：AI 開著、照尋路選的路走出腳下的危險區會來不及時，推一發落點安全的位移技（短兵相接、移轉、新月島踢擊這類以目標為基準的）。
/// </summary>
/// <remarks>
/// 只在 Draw 回呼（<c>ActionManagerEx.FinishActionGather</c>，主執行緒）呼叫。優先度高於緊急模式 ⇒ <c>ActionQueue.CanExecute</c>
/// 不跑射程、<c>ForbidExecute</c>、熱病與禁打目標，這些全在這裡自己判；任何一項判不出來（含例外、IPC 失敗）都是不放。
/// 每幀重算、不跨幀存 Actor，推進佇列的那一發只活一幀。
/// </remarks>
public sealed class DashDodgeTweak
{
    public const float Priority = 9500;

    /// <summary>呼叫端每幀抄好的狀態（讀 ImGui／vnavmesh 資料分享的那幾個只能在呼叫端讀）。</summary>
    public readonly record struct Gates(bool AIActive, bool EscapeHatchHeld, bool UserMoving, bool AutoMovePaused, bool FollowPathActive, float AnimationLock);

    private enum Reason
    {
        None,
        NotReady,
        NoTarget,
        OutOfRange,
        TooLate,
        LandingDanger,
        PathDanger,
        OutOfArena,
        Forbidden,
        NoMesh,
        Wall,
        GameRejected,
    }

    private readonly record struct Candidate(ActionDefinition Def, ActionDefinitions.DashGeometryInfo Geo);

    // 衝向目標型照使用者的評估用 0.5 秒；其餘沒量過，用動畫鎖（移轉 0.8）保守估
    private const float MinDashSeconds = 0.5f;
    private const float LandingClearance = 1f; // 落點離每個危險區邊緣至少這麼遠（目標會動、形狀有誤差）
    private const float MeshTolerance = 1f; // 野外：網格上最近的點離落點不超過這麼遠
    private const double LockoutSeconds = 2; // 放出後這段時間不再推（落地前還在區裡，別把第二格充能也用掉）
    private const int StuckFrames = 20;
    private const double LogInterval = 5;

    private readonly WorldState _ws;
    private readonly ActionTweaksConfig _config = Service.Config.Get<ActionTweaksConfig>();
    private readonly AI.AIConfig _aiConfig = Service.Config.Get<AI.AIConfig>();
    private readonly Func<ActionID, ulong, uint> _actionStatus;
    private readonly Func<Vector3, Vector3, bool> _pathBlocked;
    private readonly Func<Vector3, Vector3?>? _meshNearest;
    private readonly Candidate[] _candidates;
    private readonly (ActionID Action, Reason Reason, float Value)[] _reasons;
    private readonly Dictionary<(int, uint), DateTime> _lastLog = [];

    private ActionID _pushed;
    private int _pushedFrames, _stuckFrames;
    private string _pushedDetail = "";
    private DateTime _lockoutUntil;
    private DateTime _meshIpcFailedAt;

    /// <param name="actionStatus">遊戲的 <c>GetActionStatus</c>（0＝可以用）。</param>
    /// <param name="pathBlocked">兩點之間有沒有地形擋著（true＝擋著）。</param>
    /// <param name="meshNearest">vnavmesh 網格上離這一點最近的點；null 委派或回 null＝不在網格上。</param>
    public DashDodgeTweak(WorldState ws, Func<ActionID, ulong, uint> actionStatus, Func<Vector3, Vector3, bool> pathBlocked, Func<Vector3, Vector3?>? meshNearest)
    {
        _ws = ws;
        _actionStatus = actionStatus;
        _pathBlocked = pathBlocked;
        _meshNearest = meshNearest;
        List<Candidate> list = [];
        foreach (var def in ActionDefinitions.Instance.Definitions)
        {
            if (!ActionDefinitions.TryGetDashGeometry(def.ForbidExecute, out var geo))
                continue;
            if (geo.Geometry is not (ActionDefinitions.DashGeometry.ToTargetHitbox or ActionDefinitions.DashGeometry.AwayFromTarget or ActionDefinitions.DashGeometry.TowardTargetFixed))
                continue;
            if ((def.AllowedTargets & ActionTargets.Hostile) == 0 || def.IsGCD || def.CastTime > 0 || def.ID.Type != ActionType.Spell)
                continue;
            list.Add(new(def, geo));
        }
        _candidates = [.. list];
        _reasons = new (ActionID, Reason, float)[_candidates.Length];
    }

    public int CandidateCount => _candidates.Length;

    public void FillActions(Actor player, AIHints hints, Pathfinding.NavigationDecision.Context? nav, in Gates gates)
    {
        var pushed = false;
        try
        {
            pushed = TryFill(player, hints, nav, gates);
        }
        catch (Exception ex)
        {
            pushed = false;
            if (CanLog(-1, 0))
                Service.Logger.Information($"[BMR][位移閃避] 判斷時擲例外，這一幀不放：{ex.GetType().Name}: {ex.Message}");
        }
        if (!pushed && _pushedFrames > 0)
        {
            if (CanLog(-2, _pushed.ID))
                Service.Logger.Information($"[BMR][位移閃避] 「{_pushed.Name()}」（{_pushed}）推了 {_pushedFrames} 幀還沒放出，條件就不成立了（{_pushedDetail}）");
            _pushedFrames = _stuckFrames = 0;
            _pushed = default;
        }
    }

    /// <summary>遊戲收下一發技能請求時呼叫（<c>ActionManagerEx.HandleActionRequest</c>，主執行緒）。</summary>
    public void OnActionRequested(ActionID action)
    {
        if (_pushedFrames == 0 || action != _pushed)
            return;
        _lockoutUntil = _ws.CurrentTime.AddSeconds(LockoutSeconds);
        Service.Logger.Information($"[BMR][位移閃避] 用「{action.Name()}」（{action}）閃：{_pushedDetail}（推了 {_pushedFrames} 幀後放出）");
        _pushedFrames = _stuckFrames = 0;
        _pushed = default;
    }

    private bool TryFill(Actor player, AIHints hints, Pathfinding.NavigationDecision.Context? nav, in Gates gates)
    {
        if (!_config.DashDodge || !gates.AIActive || nav == null || player.IsDead)
            return false;
        var now = _ws.CurrentTime;
        if (!nav.TryGetPathLeeway(now, out var moveBy, out var cushion))
            return false;

        var pos = player.Position;
        var zones = hints.ForbiddenZones;
        var activation = DateTime.MaxValue;
        for (var i = 0; i < zones.Count; ++i)
        {
            var z = zones[i];
            if (z.activation < activation && z.shapeDistance(pos) < 0f)
                activation = z.activation;
        }
        if (activation == DateTime.MaxValue)
            return false; // 中心不在任何區內（只有格子擦到邊）：不算要閃
        var toHit = (float)(activation - now).TotalSeconds;
        if (toHit <= MinDashSeconds + Pathfinding.NavigationDecision.ServerJudgementLeadSeconds)
            return false; // 已生效或快生效：什麼位移都來不及，不是「想放」
        // 尋路的格子生效時間是 max(0, 生效−現在−緩衝)：還沒進緩衝時把緩衝加回來，進了就以真正剩下的時間為準
        var margin = Math.Min(toHit, cushion) + (float)(moveBy - now).TotalSeconds - Pathfinding.NavigationDecision.ServerJudgementLeadSeconds;
        if (!(margin < _config.DashDodgeTriggerSlack))
            return false;
        if (now < _lockoutUntil)
            return false;

        var gate = gates.EscapeHatchHeld ? "逃生鍵按著"
            : gates.UserMoving ? "使用者正在手動移動"
            : gates.AutoMovePaused ? "「暫停自動移動」鍵按著"
            : gates.FollowPathActive ? "vnavmesh 正在走路線"
            : _aiConfig.ForbidMovement ? "AI 設定禁止移動"
            : player.MountId != 0 ? "騎乘中"
            : player.CastInfo != null ? "詠唱中"
            : player.PendingKnockbacks.Count > 0 ? "身上有待生效的擊退"
            : hints.ImminentSpecialMode.mode != AIHints.SpecialMode.Normal && hints.ImminentSpecialMode.activation <= activation.AddSeconds(2d) ? $"特殊機制（{hints.ImminentSpecialMode.mode}）將至"
            : GazeSoon(hints, activation) ? "凝視將至（位移要轉向目標）"
            : null;
        if (gate != null)
        {
            if (CanLog(1, 0))
                Service.Logger.Information($"[BMR][位移閃避] 走路來不及（餘裕 {margin:f2} 秒、{toHit:f2} 秒後生效）但不放：{gate}");
            return false;
        }

        var target = _ws.Actors.Find(player.TargetID);
        var targetOk = target != null && !ReferenceEquals(target, player) && !target.IsAlly && target.IsTargetable && !target.IsDead
            && (hints.FindEnemy(target)?.Priority ?? AIHints.Enemy.PriorityForbidden) > AIHints.Enemy.PriorityUndesirable;

        var dest = nav.LastDestination;
        var from3 = player.PosRot.XYZ();
        var bestIndex = -1;
        var bestScore = float.MaxValue;
        WPos bestTo = default;
        var nr = 0;
        for (var c = 0; c < _candidates.Length; ++c)
        {
            var (def, geo) = _candidates[c];
            if (!def.IsUnlocked(_ws, player))
                continue;
            var (reason, value, to) = targetOk ? Evaluate(def, geo, player, target!, hints, now, toHit - Math.Max(0f, gates.AnimationLock), from3) : (Reason.NoTarget, 0f, default);
            _reasons[nr++] = (def.ID, reason, value);
            if (reason != Reason.None)
                continue;
            var score = dest is WPos d ? (to - d).LengthSq() : -value;
            if (score < bestScore)
            {
                bestScore = score;
                bestIndex = c;
                bestTo = to;
            }
        }

        if (bestIndex < 0)
        {
            if (nr > 0 && CanLog(2, 0))
                Service.Logger.Information($"[BMR][位移閃避] 走路來不及（餘裕 {margin:f2} 秒、{toHit:f2} 秒後生效）但沒有能放的位移技：{DescribeReasons(nr)}");
            return false;
        }

        var best = _candidates[bestIndex].Def;
        var facing = Angle.FromDirection(target!.Position - pos);
        hints.ActionsToExecute.Push(best.ID, target, Priority, 0, 0, 0, default, facing);
        if (_pushed != best.ID)
        {
            _pushed = best.ID;
            _pushedFrames = _stuckFrames = 0;
            _pushedDetail = $"走路來不及（餘裕 {margin:f2} 秒、{toHit:f2} 秒後生效），落點 ({bestTo.X:f1}, {bestTo.Z:f1}) 安全，位移 {(bestTo - pos).Length():f1} 碼";
        }
        ++_pushedFrames;
        // 動畫鎖還沒過的幀本來就放不出去，不算卡住
        if (gates.AnimationLock <= 0 && ++_stuckFrames == StuckFrames && CanLog(3, best.ID.ID))
            Service.Logger.Information($"[BMR][位移閃避] 「{best.ID.Name()}」（{best.ID}）推了連續 {StuckFrames} 幀沒放出（{_pushedDetail}）");
        return true;
    }

    // 回傳 Value：落點離最近危險區邊緣的距離（不危險時）或該原因的數值
    private (Reason, float, WPos) Evaluate(ActionDefinition def, ActionDefinitions.DashGeometryInfo geo, Actor player, Actor target, AIHints hints, DateTime now, float toHit, Vector3 from3)
    {
        var ready = def.ReadyIn(_ws.Client.Cooldowns, _ws.Client.DutyActions);
        if (ready > 0.05f)
            return (Reason.NotReady, ready, default);
        var dashTime = geo.Geometry == ActionDefinitions.DashGeometry.ToTargetHitbox ? MinDashSeconds : MathF.Max(MinDashSeconds, def.InstantAnimLock);
        if (toHit <= dashTime + Pathfinding.NavigationDecision.ServerJudgementLeadSeconds)
            return (Reason.TooLate, toHit, default);
        var dist = player.DistanceToHitbox(target);
        if (dist > def.Range)
            return (Reason.OutOfRange, dist, default);

        var entry = new ActionQueue.Entry(def.ID, target, Priority, 0, 0, 0, default, null, false);
        if (!ActionDefinitions.TryGetDashLanding(geo, player, entry, out var to))
            return (Reason.NoTarget, 0, default);
        var from = player.Position;
        var delta = to - from;
        var len = delta.Length();
        if (len < 1f)
            return (Reason.LandingDanger, float.NaN, to);

        var zones = hints.ForbiddenZones;
        var clearance = float.MaxValue;
        for (var i = 0; i < zones.Count; ++i)
            clearance = MathF.Min(clearance, zones[i].shapeDistance(to));
        if (clearance < LandingClearance)
            return (Reason.LandingDanger, clearance, to);

        var liveBy = now.AddSeconds(dashTime + Pathfinding.NavigationDecision.ServerJudgementLeadSeconds);
        var steps = Math.Min(64, (int)len + 1);
        for (var k = 1; k <= steps; ++k)
        {
            var p = from + delta * ((float)k / steps);
            for (var i = 0; i < zones.Count; ++i)
                if (zones[i].activation <= liveBy && zones[i].shapeDistance(p) < 0f)
                    return (Reason.PathDanger, k * len / steps, to);
        }

        var center = hints.PathfindMapCenter;
        var bounds = hints.PathfindMapBounds;
        if (hints.PathfindMapBoundsIsArena && (!bounds.Contains(to - center) || ActionDefinitions.PathCrossesCustomBounds(bounds, from - center, delta)))
            return (Reason.OutOfArena, 0, to);
        if (def.ForbidExecute != null && def.ForbidExecute(_ws, player, entry, hints))
            return (Reason.Forbidden, 0, to);

        var to3 = new Vector3(to.X, from3.Y, to.Z);
        if (!hints.PathfindMapBoundsIsArena)
        {
            Vector3? onMesh = null;
            if (_meshIpcFailedAt != now)
            {
                try
                {
                    onMesh = _meshNearest?.Invoke(to3);
                }
                catch (IpcError)
                {
                    _meshIpcFailedAt = now; // 介面對不上與落點無關：這一幀其餘候選不再問
                }
                catch (Exception)
                {
                    // 提供端內部的例外可能只跟這個落點有關：只當這一招不在網格上，其他候選照問
                }
            }
            if (onMesh is not Vector3 m)
                return (Reason.NoMesh, -1, to);
            var off = new Vector2(m.X - to.X, m.Z - to.Z).Length();
            if (off > MeshTolerance)
                return (Reason.NoMesh, off, to);
            to3.Y = m.Y;
        }
        bool blocked;
        try
        {
            blocked = _pathBlocked(from3, to3);
        }
        catch (Exception)
        {
            blocked = true;
        }
        if (blocked)
            return (Reason.Wall, 0, to);
        uint status;
        try
        {
            status = _actionStatus(def.ID, target.InstanceID);
        }
        catch (Exception)
        {
            status = uint.MaxValue;
        }
        if (status != 0)
            return (Reason.GameRejected, status, to);
        return (Reason.None, clearance, to);
    }

    private static bool GazeSoon(AIHints hints, DateTime activation)
    {
        var dirs = hints.ForbiddenDirections;
        for (var i = 0; i < dirs.Count; ++i)
            if (dirs[i].activation <= activation.AddSeconds(1d))
                return true;
        return false;
    }

    private string DescribeReasons(int n)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < n; ++i)
        {
            var (action, reason, value) = _reasons[i];
            if (i > 0)
                sb.Append('；');
            sb.Append($"「{action.Name()}」");
            sb.Append(reason switch
            {
                Reason.NotReady => $"沒充能（{value:f1} 秒後）",
                Reason.NoTarget => "沒目標（硬目標不是已在交戰的敵人）",
                Reason.OutOfRange => $"射程外（距離 {value:f1} 碼）",
                Reason.TooLate => $"來不及（{value:f2} 秒後生效）",
                Reason.LandingDanger => float.IsNaN(value) ? "落點危險（位移後幾乎沒有移動）" : value < 0 ? "落點危險（落點在危險區內）" : $"落點危險（離危險區邊緣只有 {value:f1} 碼）",
                Reason.PathDanger => $"落點危險（路上第 {value:f1} 碼經過生效中的危險區）",
                Reason.OutOfArena => "落點危險（出界或穿過場地邊界）",
                Reason.Forbidden => "落點危險（技能自帶的落點檢查擋下）",
                Reason.NoMesh => value < 0 ? "不在網格上（vnavmesh 沒有回應或沒載入）" : $"不在網格上（最近的網格點離落點 {value:f1} 碼）",
                Reason.Wall => "路上有地形擋著",
                Reason.GameRejected => $"遊戲說現在不能用（status {value:f0}）",
                _ => "?",
            });
        }
        return sb.ToString();
    }

    // 同一種訊息、同一招 LogInterval 秒內只印一次；先判再組字串
    private bool CanLog(int kind, uint action)
    {
        var now = _ws.CurrentTime;
        if (_lastLog.TryGetValue((kind, action), out var last) && (now - last).TotalSeconds < LogInterval)
            return false;
        _lastLog[(kind, action)] = now;
        return true;
    }

    private static ICallGateSubscriber<Vector3, float, float, Vector3?>? _vnavNearest;
    private static long _vnavNearestRefetchAt;

    /// <summary>vnavmesh <c>Query.Mesh.NearestPoint</c>（水平半徑 1、垂直半徑 3）；沒載入回 null，擲例外由呼叫端當作不在網格上。只在主執行緒呼叫。</summary>
    public static Vector3? VnavNearestPoint(Vector3 p)
    {
        var sub = _vnavNearest;
        if (sub == null || !sub.HasFunction)
        {
            // 沒有提供端時隔幾秒重取一次訂閱：頻道可能被 Dalamud 資料分享頁的「清除空頻道」換掉，舊訂閱會永遠看不到新註冊
            var t = Environment.TickCount64;
            if (sub != null && t < _vnavNearestRefetchAt)
                return null;
            _vnavNearestRefetchAt = t + 5000;
            sub = _vnavNearest = Service.PluginInterface?.GetIpcSubscriber<Vector3, float, float, Vector3?>("vnavmesh.Query.Mesh.NearestPoint");
            if (sub == null || !sub.HasFunction)
                return null;
        }
        return sub.InvokeFunc(p, 1f, 3f);
    }
}
