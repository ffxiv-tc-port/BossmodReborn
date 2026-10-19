namespace BossMod;

/// <summary>
/// 「位移技危險攔截」：在 <c>ActionManager::UseAction</c> 這一關把「會把自己送進危險區」的位移技吞掉。
/// </summary>
/// <remarks>
/// 🔑 存在的理由是**既有的位移安全檢查只在「東西進得了 BMR 的佇列」時才生效**：BMR 本來就有一整組
/// 落點判定式（<see cref="ActionDefinitions.DashToTargetCheck"/> 那幾條，掛在
/// <c>ActionDefinition.ForbidExecute</c> 上），但它們只在 <c>ActionQueue.FindBest</c> 裡被呼叫。
/// <para>
/// 外部外掛呼叫 <c>ActionManager::UseAction</c> 並不會繞過 BMR：
/// 我們的 <c>ActionManagerEx.UseActionDetour</c> 掛的就是 <c>ActionManager::UseAction</c> 本身，
/// 所以 WrathCombo 這類外掛的呼叫**一樣會經過這一關**；而且當
/// <see cref="ActionTweaksConfig.UseManualQueue"/> 開著時，那一發會被 <c>ManualActionQueueTweak.Push</c>
/// 收進 BMR 自己的佇列 —— 於是它**照樣吃得到** <c>ForbidExecute</c> 的落點判定，不需要這個 tweak。
/// </para>
/// <para>
/// 🔑 因此這個 tweak 不可替代的場合是那一發沒進 BMR 的佇列、落回遊戲原生路徑（<c>UseActionDetour</c> 裡 <c>queued</c> 為 false）：
/// <list type="bullet">
/// <item><see cref="ActionTweaksConfig.UseManualQueue"/> 關著（預設值 ⇒ 多數使用者的常態）</item>
/// <item><c>mode != UseActionMode.None</c>（地面放置技的預覽/確定那兩段）或動作類型不是 Spell／Item</item>
/// <item><c>Push</c> 回 false（<c>ActionDefinitions</c> 沒登記這個動作、冷卻剩餘超過佇列視窗、目標解析失敗），或 <c>UseActionDetour</c> 的 <c>try</c> 擲例外走進退化路徑</item>
/// </list>
/// </para>
/// <para>
/// 📌 順序上這一關**排在** <c>Push</c> 之前（<c>queued = !dashBlocked &amp;&amp; …</c>），
/// 所以兩條路徑不會重複攔截：被這裡吞掉的那一發根本不會進佇列。
/// </para>
/// <para>
/// 🔴 落點算法**不另建一張表**：直接沿用上面那幾條判定式登記的分類與距離
/// （<see cref="ActionDefinitions.TryGetDashGeometry"/>）。兩份表遲早會漂移，而漂移的失敗形式是靜默的。
/// </para>
/// <para>
/// 🔴 執行緒約定：<see cref="Update"/> 只在 Dalamud 的 Draw 回呼（<c>Plugin.DrawUI</c>）裡跑，
/// 把該幀的 <see cref="AIHints.ForbiddenZones"/> 複製成一份**唯讀陣列**後整份換上；
/// <see cref="ShouldBlock"/> 只讀那份快照（開頭一次讀進區域變數），**從頭到尾不碰活的 AIHints**。
/// UseAction 實務上就在主執行緒，但這樣寫的話「萬一不是」也只會讀到上一幀的**完整**資料，
/// 不會撞上 <c>ForbiddenZones.Clear()</c> 進行到一半的中間狀態。
/// ⚠️ 這裡不宣稱 shapeDistance 閉包本身跨執行緒安全。
/// </para>
/// <para>
/// ⚠️ 場地邊界只在 <see cref="AIHints.PathfindMapBoundsIsArena"/> 為 true（正式模組、F.A.T.E. 半徑）時才檢查。
/// 野外預設的 30 碼方形、障礙圖視窗、<c>SimpleBossModule</c> 都是跟著玩家走的尋路範圍，不是場地，
/// 拿來判會把長突進誤判成出界。只判落點；起點已經在界外時不判（邊界可能還沒對準）。
/// </para>
/// </remarks>
public sealed class DashInterceptTweak
{
    /// <summary>某一幀的唯讀快照。整份物件建好之後不再改，發佈只靠一次欄位指派。</summary>
    private sealed class FrameSnapshot(
        (Func<WPos, float> Shape, DateTime Activation)[] zones, DateTime now, bool aiActive, bool escapeHatchHeld, float threshold, ArenaBounds? bounds, WPos center)
    {
        public readonly (Func<WPos, float> Shape, DateTime Activation)[] Zones = zones;
        public readonly DateTime Now = now;
        public readonly bool AIActive = aiActive;
        public readonly bool EscapeHatchHeld = escapeHatchHeld;
        public readonly float Threshold = threshold;
        public readonly ArenaBounds? Bounds = bounds; // null＝這一幀沒有可信的場地邊界
        public readonly WPos Center = center;
    }

    private const float SampleStep = 1f; // 路徑取樣間距（碼）
    private const int MaxSamples = 64; // 最長的位移技也就 25 碼，這個上限只是防呆
    private static readonly TimeSpan BlockLogThrottle = TimeSpan.FromSeconds(3d);
    private static readonly TimeSpan NoticeLogThrottle = TimeSpan.FromSeconds(60d);

    private readonly WorldState _ws;
    private readonly ActionTweaksConfig _config = Service.Config.Get<ActionTweaksConfig>();
    private readonly HashSet<ActionID> _dashActions = [];
    private readonly Dictionary<ActionID, DateTime> _lastBlockLog = [];
    private FrameSnapshot? _snapshot;
    private string? _lastNotice;
    private DateTime _nextNoticeAt;

    public DashInterceptTweak(WorldState ws)
    {
        _ws = ws;

        // 🔴 清單在載入時建一次：熱路徑上只剩一個 HashSet 查詢，不是位移技就零成本直通。
        //    來源是 ActionDefinitions 已經登記好的落點判定式，所以「哪些算位移技」與自動循環那一關完全同源。
        int toTarget = 0, toGround = 0, fixedDist = 0, backdash = 0, towardTarget = 0;
        foreach (var def in ActionDefinitions.Instance.Definitions)
        {
            if (!ActionDefinitions.TryGetDashGeometry(def.ForbidExecute, out var geo))
                continue;
            _dashActions.Add(def.ID);
            // 節流表在這裡就把所有鍵放齊：之後 LogBlock 只會覆寫既有項目的值（8 位元組寫入），
            // 不會發生擴容／rehash —— 萬一 UseAction 真的從別的執行緒進來，最壞也只是多印或少印一行。
            _lastBlockLog[def.ID] = DateTime.MinValue;
            switch (geo.Geometry)
            {
                case ActionDefinitions.DashGeometry.ToTargetHitbox:
                    ++toTarget;
                    break;
                case ActionDefinitions.DashGeometry.ToGroundTarget:
                    ++toGround;
                    break;
                case ActionDefinitions.DashGeometry.FixedFromFacing:
                    ++fixedDist;
                    break;
                case ActionDefinitions.DashGeometry.AwayFromTarget:
                    ++backdash;
                    break;
                case ActionDefinitions.DashGeometry.TowardTargetFixed:
                    ++towardTarget;
                    break;
            }
        }
        // 要使用者回報的診斷：這行是「涵蓋了幾顆」的唯一憑據
        Service.Logger.Information($"[BMR][位移攔截] 位移技清單建立完成：共 {_dashActions.Count} 顆（衝向目標 {toTarget}、地面指定 {toGround}、定距離 {fixedDist}、以目標為基準後跳 {backdash}、朝目標固定距離 {towardTarget}）。地面指定型在 UseAction 這一關拿不到座標，一律不攔。");
    }

    /// <summary>
    /// 每幀在繪製執行緒上重建快照。<paramref name="escapeHatchHeld"/> 由呼叫端傳入
    /// （<c>MovementOverride.IsForceUnblocked</c>），攔截的 detour 只讀這份快照、不自己查按鍵。
    /// </summary>
    public void Update(AIHints hints, bool escapeHatchHeld)
    {
        if (!_config.DashSafety || !_config.DashSafetyBlockExternal)
        {
            _snapshot = null; // 關掉時連快照都不留：熱路徑第一個判斷就出去
            return;
        }

        var now = _ws.CurrentTime;
        var threshold = _config.DashSafetyActivationThreshold;
        var horizon = now.AddSeconds(threshold);
        var src = hints.ForbiddenZones;
        var count = src.Count;

        // 只留「已生效或即將引爆」的區。更遠的區站進去 AI 會自己走出來，攔了只是讓循環白白卡住。
        // 註：沒帶 activation 的區是 default(DateTime)（= MinValue），會被當成「已生效」留下來，這是刻意的。
        var kept = 0;
        for (var i = 0; i < count; ++i)
            if (src[i].activation <= horizon)
                ++kept;

        // 有可信的場地邊界時，沒有危險區也要建快照（出界本身就要擋）
        var bounds = hints.PathfindMapBoundsIsArena ? hints.PathfindMapBounds : null;
        if (kept == 0 && bounds == null)
        {
            _snapshot = null;
            return;
        }

        var zones = new (Func<WPos, float> Shape, DateTime Activation)[kept];
        var j = 0;
        for (var i = 0; i < count; ++i)
        {
            var z = src[i];
            if (z.activation <= horizon)
                zones[j++] = (z.shapeDistance, z.activation);
        }

        _snapshot = new(zones, now, AI.AIManager.Instance?.Beh != null, escapeHatchHeld, threshold, bounds, hints.PathfindMapCenter);
    }

    /// <summary>
    /// 這一次 <c>UseAction</c> 該不該吞掉。回 true 代表攔下（呼叫端直接回 false 給遊戲，不呼叫 Original）。
    /// </summary>
    /// <remarks>🔴 任何算不出落點的情況一律回 false —— 寧可漏攔，不可誤攔。</remarks>
    public bool ShouldBlock(ActionID action, ulong targetId)
    {
        var snapshot = _snapshot; // 只讀一次，後面全部看這份
        if (snapshot == null || !_dashActions.Contains(action))
            return false; // 熱路徑出口：功能關著／這一幀沒有危險區也沒有場地邊界／不是位移技

        var player = _ws.Party.Player();
        if (player == null)
            return false;

        var def = ActionDefinitions.Instance[action];
        if (def == null || !ActionDefinitions.TryGetDashGeometry(def.ForbidExecute, out var geo))
            return false; // 照理進不來（_dashActions 就是從這裡建的），留著當防禦

        // 與既有的兩個選項語意一致：非「衝向目標」型要另外開 DashSafetyExtra（朝目標固定距離的判定式本來就只看 DashSafety）
        if (geo.Geometry is not (ActionDefinitions.DashGeometry.ToTargetHitbox or ActionDefinitions.DashGeometry.TowardTargetFixed) && !_config.DashSafetyExtra)
            return false;

        // 地面指定型（縮地／魔紋步／回歸／若隱若現）：落點是玩家等一下才會點的地面座標，
        // UseAction 這一關根本還不知道，一律不攔。擺在最前面是為了連「為什麼沒攔」都不要記。
        if (geo.Geometry == ActionDefinitions.DashGeometry.ToGroundTarget)
            return false;

        // 📌 下面兩條放行條件才開始記診斷 —— 排在幾何判定之後，是為了不對「本來就不可能攔的技能」
        //    （例如地面指定型）洗出「為什麼沒攔」的說明。
        if (!snapshot.AIActive)
        {
            // 攔截這一關分不出「誰要放這一發」，所以 AI 沒開時一律不動手 —— 那是純手動玩家的操作。
            Notice(snapshot, "[BMR][位移攔截] 目前不攔：BMR AI 沒有啟用。攔截無法分辨技能是誰要放的，AI 關著＝視為純手動操作，不介入。");
            return false;
        }

        if (snapshot.EscapeHatchHeld)
        {
            Notice(snapshot, "[BMR][位移攔截] 目前不攔：逃生鍵（設定「詠唱期間允許移動需按住的按鍵」）按著。");
            return false;
        }

        var from = player.Position;
        WPos to;
        switch (geo.Geometry)
        {
            case ActionDefinitions.DashGeometry.ToTargetHitbox:
                {
                    var target = ResolveDashTarget(def, player, targetId);
                    if (target == null)
                        return false; // 目標解不出來就算不出落點
                    to = from + player.DirectionTo(target) * MathF.Max(0f, player.DistanceToHitbox(target));
                }
                break;
            case ActionDefinitions.DashGeometry.AwayFromTarget:
                {
                    var target = ResolveDashTarget(def, player, targetId);
                    if (target == null)
                        return false;
                    to = from + target.DirectionTo(player) * geo.Distance;
                }
                break;
            case ActionDefinitions.DashGeometry.TowardTargetFixed:
                {
                    var target = ResolveDashTarget(def, player, targetId);
                    if (target == null)
                        return false;
                    to = from + player.DirectionTo(target) * geo.Distance;
                }
                break;
            case ActionDefinitions.DashGeometry.FixedFromFacing:
                // 這裡刻意用角色目前的朝向，不用 ActionDefinition.TransformAngle（那些「與攝影機方向一致」
                // 的設定只在 BMR 自己執行時才會先轉向；外部來源送出去時遊戲用的就是角色當下的朝向）。
                to = from + player.Rotation.ToDirection() * geo.Distance;
                break;
            default:
                return false; // 地面指定型：UseAction 這一關還拿不到落點座標
        }

        var zones = snapshot.Zones;
        var nz = zones.Length;
        var delta = to - from;
        var len = delta.Length();

        // 場地邊界排在「站在危險區裡就放行」之前：衝出界本身就會倒地，不算逃命
        var bounds = snapshot.Bounds;
        if (bounds != null && len > 0f && bounds.Contains(from - snapshot.Center) && !bounds.Contains(to - snapshot.Center))
        {
            LogBoundsBlock(action, geo, from, to, len, bounds, snapshot);
            return true;
        }

        // 條件三：已經站在（生效中或即將引爆的）危險區裡 —— 這一發位移很可能是在逃命，一律放行。
        for (var i = 0; i < nz; ++i)
        {
            if (zones[i].Shape(from) < 0f)
            {
                Notice(snapshot, "[BMR][位移攔截] 目前不攔：玩家已經站在危險區裡，這一發位移可能是在逃命。");
                return false;
            }
        }

        if (len <= 0f)
            return false; // 零位移（例如已經貼在目標 hitbox 上）不可能把人送進新的危險

        // 取樣整條線段，不是只看落點：突進的中途一樣會吃到傷害。起點不取（上面那道閘門已經涵蓋）。
        var steps = Math.Min(MaxSamples, (int)(len / SampleStep) + 1);
        var invSteps = 1f / steps;
        for (var k = 1; k <= steps; ++k)
        {
            var p = from + delta * (k * invSteps);
            for (var i = 0; i < nz; ++i)
            {
                if (zones[i].Shape(p) < 0f)
                {
                    LogBlock(action, geo, from, to, p, k, steps, len, zones[i].Activation, snapshot);
                    return true;
                }
            }
        }
        return false;
    }

    /// <summary>
    /// 解出「這一發位移技真正會用來算落點的目標」，解不出來就回 null（＝不攔）。
    /// </summary>
    /// <remarks>
    /// 🔴 光是 <c>Actors.Find(targetId)</c> 不夠：<c>targetId</c> 是玩家當下的硬目標，跟這顆技能收不收那種目標無關。
    /// 例如黑魔的乙太步（155）只能指向隊友，但玩家硬目標通常是敵人 —— 直接拿那個敵人算落點，
    /// 會得到一個與實際完全無關的方向，然後**誤攔**。所以這裡拿 <c>AllowedTargets</c> 做一次相容性檢查，
    /// 對不上就回 null（寧可漏攔）。
    /// </remarks>
    private Actor? ResolveDashTarget(ActionDefinition def, Actor player, ulong targetId)
    {
        var target = _ws.Actors.Find(targetId);
        if (target == null || ReferenceEquals(target, player))
            return null;

        var allowed = def.AllowedTargets;
        return target.IsAlly
            ? (allowed & (ActionTargets.Party | ActionTargets.Alliance | ActionTargets.Friendly)) != 0 ? target : null
            : (allowed & ActionTargets.Hostile) != 0 ? target : null;
    }

    private static string GeometryName(ActionDefinitions.DashGeometry g) => g switch
    {
        ActionDefinitions.DashGeometry.ToTargetHitbox => "衝向目標",
        ActionDefinitions.DashGeometry.ToGroundTarget => "地面指定",
        ActionDefinitions.DashGeometry.FixedFromFacing => "定距離",
        ActionDefinitions.DashGeometry.AwayFromTarget => "以目標為基準後跳",
        ActionDefinitions.DashGeometry.TowardTargetFixed => "朝目標固定距離",
        _ => "未知",
    };

    private void LogBoundsBlock(ActionID action, ActionDefinitions.DashGeometryInfo geo, WPos from, WPos to, float len, ArenaBounds bounds, FrameSnapshot snapshot)
    {
        var now = snapshot.Now;
        if (_lastBlockLog.TryGetValue(action, out var last) && now - last < BlockLogThrottle)
            return;
        _lastBlockLog[action] = now;

        var c = snapshot.Center;
        Service.Logger.Information(
            $"[BMR][位移攔截] 攔下位移技「{action.Name()}」（{action}，{GeometryName(geo.Geometry)}型）：" +
            $"起點 ({from.X:f1}, {from.Z:f1}) → 落點 ({to.X:f1}, {to.Z:f1})，長 {len:f1} 碼；" +
            $"落點在場地邊界外（{bounds.GetType().Name}、半徑 {bounds.Radius:f1}、中心 ({c.X:f1}, {c.Z:f1})，落點離中心 {(to - c).Length():f1} 碼）。" +
            "這一發已被吞掉；要強制放行請按住逃生鍵。");
    }

    // 一行判死：互斥成因 + 實際數字。看到這一行就不必再問「是落點還是半路」「是哪個區」「門檻夠不夠」。
    private void LogBlock(ActionID action, ActionDefinitions.DashGeometryInfo geo, WPos from, WPos to, WPos hit, int step, int steps, float len, DateTime activation, FrameSnapshot snapshot)
    {
        var now = snapshot.Now;
        if (_lastBlockLog.TryGetValue(action, out var last) && now - last < BlockLogThrottle)
            return;
        _lastBlockLog[action] = now;

        var eta = activation <= now ? 0d : (activation - now).TotalSeconds;
        var when = eta <= 0d ? "已經生效" : $"{eta:f2} 秒後引爆";
        var where = step == steps ? "落點" : $"路徑上第 {step}/{steps} 個取樣點";
        Service.Logger.Information(
            $"[BMR][位移攔截] 攔下位移技「{action.Name()}」（{action}，{GeometryName(geo.Geometry)}型）：" +
            $"起點 ({from.X:f1}, {from.Z:f1}) → 落點 ({to.X:f1}, {to.Z:f1})，長 {len:f1} 碼；" +
            $"{where} ({hit.X:f1}, {hit.Z:f1}) 落在{when}的危險區內。" +
            $"（本幀快照 {snapshot.Zones.Length} 個生效／即將生效的區、引爆門檻 {snapshot.Threshold:f2} 秒、AI 啟用中、逃生鍵未按）" +
            "這一發已被吞掉，下一幀安全就會自然放行；要強制放行請按住逃生鍵。");
    }

    // 「為什麼沒攔」只在成因改變、或距上次同一成因超過 60 秒時印一次 —— 這幾條在一場戰鬥裡會被踩很多次。
    private void Notice(FrameSnapshot snapshot, string text)
    {
        var now = snapshot.Now;
        if (text == _lastNotice && now < _nextNoticeAt)
            return;
        _lastNotice = text;
        _nextNoticeAt = now + NoticeLogThrottle;
        Service.Logger.Information(text);
    }
}
