using System.Threading;

namespace BossMod.Pathfinding;

// utility for selecting player's navigation target
// there are several goals that navigation has to meet, in following rough priority
// 1. stay away from aoes; tricky thing is that sometimes it is ok to temporarily enter aoe, if we're sure we'll exit it in time
// 2. maintain uptime - this is represented by being in specified range of specified target, and not moving to interrupt casts unless needed
// 3. execute positionals - this is strictly less important than points above, we only do that if we can meet other conditions
// 4. be in range of healers - even less important, but still nice to do
public struct NavigationDecision
{
    // context that allows reusing large memory allocations
    public sealed class Context
    {
        public float[] Scratch = [];
        public (Func<WPos, float> shapeDistance, float g)[] ZonesScratch = [];
        public Map Map = new();
        public ThetaStar ThetaStar = new();
        public DodgeLookahead.Buffers? Lookahead; // 第一次真的有未來危險區時才建
        public string LogTag = "";
        public bool LogLookahead = true;

        /// <summary>搬走 <paramref name="donor"/> 的大緩衝（地圖兩張表、節點表、光柵化暫存），其餘狀態維持新建時的值。</summary>
        /// <remarks>
        /// 契約：呼叫時已經沒有任何執行緒會再拿 donor 尋路（背景工作已結束）；donor 之後不可再拿來尋路。
        /// 這些緩衝本來就是「只增不減、每次用前把要用的範圍整段重新初始化」，所以結果與新建的 Context 逐位元相同。
        /// </remarks>
        internal void AdoptBuffers(Context donor)
        {
            if (donor.Map.PixelMaxG.Length > Map.PixelMaxG.Length)
                (Map.PixelMaxG, donor.Map.PixelMaxG) = (donor.Map.PixelMaxG, Map.PixelMaxG);
            if (donor.Map.PixelPriority.Length > Map.PixelPriority.Length)
                (Map.PixelPriority, donor.Map.PixelPriority) = (donor.Map.PixelPriority, Map.PixelPriority);
            if (donor.Scratch.Length > Scratch.Length)
                (Scratch, donor.Scratch) = (donor.Scratch, Scratch);
            ThetaStar.AdoptNodes(donor.ThetaStar);
        }

        // 給詠唱讓位讀的「上一次 Build 的閃避預判」：高位＝算的時間（毫秒）、低 17 位＝最晚幾毫秒後要動身（全 1＝不用趕）。
        // 寫的人可能是背景執行緒（AIBehaviour），所以只用單一 long 的 Volatile 讀寫：讀不到半寫、不配置。
        private long _lookaheadSignal;
        // 同一種編碼：玩家腳下那一格會被打時，沿尋路選的路最晚幾毫秒後要動身（路上穿過的每一區都算進去）
        private long _pathSignal;
        private const long NoDeadline = 0x1FFFF;
        private const double SignalMaxAgeSeconds = 0.5;
        // 位移閃避讀的三格（各自 Volatile、不保證來自同一次 Build；差一兩幀可接受）：
        // 尋路餘裕帶正負（_pathSignal 把負值夾成 0，分不出「剛好」與「晚很多」）、那次用的生效緩衝、目的地。
        private const long SignedOffset = 0xFFFF;
        private const long NoDestination = 0x7FC000007FC00000; // X、Z 都是 NaN
        private long _pathLeewaySignal;
        private int _pathCushionBits;
        private long _destinationSignal = NoDestination;

        internal void PublishLookahead(DateTime now, float moveBySeconds) => Volatile.Write(ref _lookaheadSignal, Encode(now, moveBySeconds));

        internal void PublishPath(DateTime now, float leewaySeconds) => Volatile.Write(ref _pathSignal, Encode(now, leewaySeconds));

        /// <param name="leewaySeconds">腳下這一格會被打時沿尋路選的路的最小餘裕（相對「生效時間減 <paramref name="cushion"/>」）；不是有限值＝不發布，超出可表示範圍的夾到兩端。</param>
        internal void PublishPathLeeway(DateTime now, float leewaySeconds, float cushion, WPos? destination)
        {
            // 先以 float 夾限再轉整數：極大的正值（例如 float.MaxValue）先轉 long 再加偏移會溢位成負數，落到下限
            var field = float.IsFinite(leewaySeconds) ? (long)Math.Clamp(MathF.Round(leewaySeconds * 1000f) + SignedOffset, 0f, (float)(NoDeadline - 1)) : NoDeadline;
            Volatile.Write(ref _pathLeewaySignal, ((now.Ticks / TimeSpan.TicksPerMillisecond) << 17) | field);
            Volatile.Write(ref _pathCushionBits, BitConverter.SingleToInt32Bits(cushion));
            Volatile.Write(ref _destinationSignal, destination is WPos d ? ((long)BitConverter.SingleToInt32Bits(d.X) << 32) | (uint)BitConverter.SingleToInt32Bits(d.Z) : NoDestination);
        }

        /// <summary>上一次 Build 時腳下那一格會被打：照尋路選的路最晚 <paramref name="moveBy"/> 要動身（可能早於現在＝已經晚了），<paramref name="cushion"/> 是那次的生效緩衝。條件同 <see cref="TryGetPathDeadline"/>。</summary>
        public bool TryGetPathLeeway(DateTime now, out DateTime moveBy, out float cushion)
        {
            var v = Volatile.Read(ref _pathLeewaySignal);
            cushion = BitConverter.Int32BitsToSingle(Volatile.Read(ref _pathCushionBits));
            moveBy = DateTime.MaxValue;
            var field = v & NoDeadline;
            if (field == NoDeadline || v == 0)
                return false;
            var built = new DateTime((v >> 17) * TimeSpan.TicksPerMillisecond);
            var age = (now - built).TotalSeconds;
            if (age is < -SignalMaxAgeSeconds or > SignalMaxAgeSeconds)
                return false;
            moveBy = built.AddMilliseconds(field - SignedOffset);
            return true;
        }

        /// <summary>上一次 Build 選的目的地；沒有＝null。</summary>
        public WPos? LastDestination
        {
            get
            {
                var v = Volatile.Read(ref _destinationSignal);
                var x = BitConverter.Int32BitsToSingle((int)(v >> 32));
                var z = BitConverter.Int32BitsToSingle((int)v);
                return float.IsFinite(x) && float.IsFinite(z) ? new WPos(x, z) : null;
            }
        }

        private static long Encode(DateTime now, float seconds)
        {
            var field = seconds * 1000f < NoDeadline ? Math.Max(0L, (long)(seconds * 1000f)) : NoDeadline;
            return ((now.Ticks / TimeSpan.TicksPerMillisecond) << 17) | field;
        }

        /// <summary>上一次 Build 的閃避預判要求在 <paramref name="deadline"/> 前動身（才接得上下一波）；不用趕、或那次結果超過 0.5 秒＝false。</summary>
        public bool TryGetLookaheadDeadline(DateTime now, out DateTime deadline) => TryDecode(Volatile.Read(ref _lookaheadSignal), now, out deadline);

        /// <summary>上一次 Build 時玩家腳下那一格會被打，尋路要求在 <paramref name="deadline"/> 前動身；腳下安全、生效時間被壓平、或那次結果超過 0.5 秒＝false。</summary>
        public bool TryGetPathDeadline(DateTime now, out DateTime deadline) => TryDecode(Volatile.Read(ref _pathSignal), now, out deadline);

        private static bool TryDecode(long v, DateTime now, out DateTime deadline)
        {
            deadline = DateTime.MaxValue;
            var field = v & NoDeadline;
            if (field == NoDeadline || v == 0)
                return false;
            var built = new DateTime((v >> 17) * TimeSpan.TicksPerMillisecond);
            var age = (now - built).TotalSeconds;
            if (age is < -SignalMaxAgeSeconds or > SignalMaxAgeSeconds)
                return false;
            deadline = built.AddMilliseconds(field);
            return true;
        }
    }

    public WPos? Destination;
    public WPos? NextWaypoint;
    public float LeewaySeconds; // can be used for finishing casts / slidecasting etc.
    public float TimeToGoal;

    #region 診斷（純輸出，不參與任何決策）

    /// <summary>這一次真正拿去 rasterize 的目標區數量（<b>快照</b>長度，不是還活著的那份 List）。</summary>
    public int DiagGoalZones;

    /// <summary>目標區有沒有真的被畫上權重場。詠唱中、玩家格出視窗、玩家腳下本來就危險時都<b>不會</b>畫。</summary>
    public bool DiagGoalsRasterized;

    /// <summary>玩家所在的格子在不在尋路視窗內。</summary>
    public bool DiagPlayerInWindow;

    /// <summary>玩家格的危險度（<c>float.MaxValue</c>＝安全、0＝現在就危險、負＝不可通行）。</summary>
    public float DiagPlayerMaxG;

    /// <summary>玩家格的目標權重。</summary>
    public float DiagPlayerPriority;

    /// <summary>整張權重場的最高權重（<b>含走不到的格子</b>）。</summary>
    public float DiagMaxPriority;

    /// <summary>這一次搜尋真的走得到的格子裡最高的權重。見 <see cref="ThetaStar.MaxReachedPriority"/>。</summary>
    public float DiagReachablePriority;

    /// <summary>整個可達區域都探過了（開放清單掃空）。</summary>
    public bool DiagSearchExhausted;

    /// <summary>這一次尋路展開了幾格。</summary>
    public int DiagSearchSteps;

    /// <summary>呼叫端傳進來的<b>原始</b>移動速度（碼／秒），沒有被夾限前的值。</summary>
    public float DiagRawSpeed;

    /// <summary>原始速度不在合理範圍，這一次改用 <see cref="NominalPlayerSpeed"/> 算。見那裡的說明。</summary>
    public bool DiagSpeedSubstituted;

    /// <summary>閃避預判這一次有扣分（安全格裡同時有接得上與接不上下一波的）。</summary>
    public bool DiagLookaheadApplied;

    /// <summary>有扣分，而且玩家腳下這一格接不上下一波。</summary>
    public bool DiagLookaheadSteering;

    /// <summary>這一次算不出目的地的原因是「場上根本沒有目標區」＝<b>沒有人要求移動</b>（閒置常態）。</summary>
    /// <remarks>🔴 條件與 <see cref="DiagSummary"/> 裡「場上沒有任何目標區」那一支<b>逐字相同</b>，改一邊要改另一邊。</remarks>
    public readonly bool DiagIdleNoGoals => DiagPlayerInWindow && !DiagGoalsRasterized && DiagGoalZones == 0;

    /// <summary>把「尋路這一次到底看到了什麼」寫成一行。</summary>
    /// <remarks>
    /// 🔴 <b>只在狀態翻轉時呼叫</b>：這支會組字串，每幀叫等於每幀配置記憶體，而且會把 log 洗掉。
    /// 「角色不走」的互斥成因外觀完全相同（回 null、不報錯）：玩家格落在尋路視窗外／目標區整段沒被畫上權重場（詠唱中、玩家腳下已經在危險區裡）／
    /// 權重場在玩家腳下是平的（玩家格的權重已經等於場上最高，平台型的目標區把玩家包在裡面就會這樣）／更好的格子被禁區或障礙物隔開／
    /// 移動速度讀到 0（見 <see cref="NominalPlayerSpeed"/>）。
    /// </remarks>
    public readonly string DiagSummary()
    {
        var why = !DiagPlayerInWindow
            ? "玩家格落在尋路視窗外 ⇒ 目標區對玩家目前的位置完全沒有作用"
            : !DiagGoalsRasterized
                ? DiagGoalZones == 0
                    ? "場上沒有任何目標區 ⇒ 沒有人要求移動"
                    : "目標區沒有被畫上權重場（詠唱中，或玩家腳下已經在危險區裡，安全優先）"
                : DiagMaxPriority <= DiagPlayerPriority
                    ? "權重場在玩家腳下是平的（玩家格的權重已經等於場上最高）⇒ 尋路認定「已經在最佳位置」"
                    // 🔑 「更好的格子到不了」有兩種完全不同的成因，
                    //    分辨它們的唯一數字就是「走得到的最高權重」——見 ThetaStar.MaxReachedPriority。
                    : DiagReachablePriority <= DiagPlayerPriority + 1e-4f
                        ? (DiagSearchExhausted
                            ? "整個可達區域都探過了，走得到的最高權重就是玩家腳下這一格 ⇒ 玩家卡在局部最高點（更高的權重被禁區／障礙物隔在外面）"
                            : "走得到的最高權重就是玩家腳下這一格，但搜尋沒有掃完可達區域 ⇒ 提早收斂")
                        : "走得到更好的格子，尋路卻沒有採用 ⇒ 這是尋路自己的問題，不是目標區的問題";
        return $"目標區 {DiagGoalZones} 個、已畫上權重場={(DiagGoalsRasterized ? "是" : "否")}、玩家格在視窗內={(DiagPlayerInWindow ? "是" : "否")}、" +
            $"玩家格危險度={FormatMaxG(DiagPlayerMaxG)}、玩家格權重={DiagPlayerPriority:f2}、場上最高權重={DiagMaxPriority:f2}、" +
            $"走得到的最高權重={DiagReachablePriority:f2}、展開 {DiagSearchSteps} 格{(DiagSearchExhausted ? "（已掃完）" : "")}、" +
            $"移動速度={DiagRawSpeed:f2}{(DiagSpeedSubstituted ? $"（不合理，改用 {NominalPlayerSpeed:f0}）" : "")} ⇒ {why}";
    }

    private static string FormatMaxG(float g) => g == float.MaxValue ? "安全" : g < 0f ? "不可通行" : $"{g:f2}s";

    #endregion

    public const float ActivationTimeCushion = 1f; // reduce time between now and activation by this value in seconds; increase for more conservativeness

    /// <summary>
    /// 移動速度讀不出合理值時拿來代打的名目速度（碼／秒）。與 <see cref="Build"/> 的
    /// <c>playerSpeed</c> 預設值相同，也就是未改造的角色跑步速度。
    /// </summary>
    /// <remarks>
    /// 🔴🔴 <b>speed 讀到 0 會讓整個尋路靜默失效，而且症狀完全不像「速度」的問題</b>：<c>ThetaStar.Start(map, pos, 1f / speed)</c>
    /// 的 <c>gMultiplier</c> 變成 +∞ ⇒ 每一步的 g 值都是 ∞ ⇒ 餘裕是 -∞ ⇒ 每一格的評分都輸給起點
    /// ⇒ 把整個可達區域掃完後回起點 ⇒ <b><c>Destination == null</c>：角色不走、不畫標線、不報錯。</b>
    /// ⚠️ 夾限不會動到戰鬥閃避：對任何物理上可能的速度（走路 ~2、跑步 6、衝刺 ~10、坐騎 ~20）判斷式恆為真、輸出逐字相同；
    /// 會改變輸出的只有 0／負數／NaN／∞／荒謬值，而那些輸入不夾限時的輸出就是「完全不閃避」。
    /// 📌 刻意<b>不</b>去動 <c>ClientState.MoveSpeed</c> 本身：<c>MainDebugWindow</c> 繼續顯示<b>真值</b>，原始值另寫進 <see cref="DiagRawSpeed"/> 供離線判讀。
    /// </remarks>
    public const float NominalPlayerSpeed = 6f;

    /// <summary>速度低於這個值就當作「沒讀到」。比任何實際的減速都低。</summary>
    private const float MinValidPlayerSpeed = 0.1f;

    /// <summary>速度高於這個值就當作「沒讀到」。比任何坐騎都高，同時擋掉 ∞。</summary>
    private const float MaxValidPlayerSpeed = 200f;

    /// <summary>閃避預判裡「上一波打完到動身」預留的反應時間（秒）。</summary>
    /// <remarks>刻意不跟 activationTimeCushion：那是「多早離開危險區」的偏好，拿來扣兩波之間的時間會把
    /// 2 秒左右的連段整串判成接不起來（緩衝 2 秒時 2.1 秒的圓→甜甜圈只剩 0.1 秒），預判就整段失效。</remarks>
    private const float LookaheadReactionSeconds = 0.5f;

    /// <summary>伺服器判定比畫面早的量（秒）：只扣在發布給詠唱讓位的期限上，不影響尋路與 <see cref="LeewaySeconds"/>。</summary>
    /// <remarks>伺服器判定移動中的角色用的是稍舊的位置（站著的不受影響）。閃避預判的期限對的是沒有緩衝的生效時間，整段扣；
    /// 尋路餘裕已含生效緩衝，只補緩衝不足的部分。</remarks>
    public const float ServerJudgementLeadSeconds = 0.3f;

    /// <param name="futureZones">未來危險區快照。null＝在呼叫端執行緒當場從 hints 取；背景執行緒呼叫時必須自己在主執行緒先取好。</param>
    public static NavigationDecision Build(Context ctx, WorldState ws, AIHints hints, Actor player, float playerSpeed = 6f, float forbiddenZoneCushion = default, bool avoidFutureAOEs = false, float activationTimeCushion = ActivationTimeCushion,
        (Func<WPos, float> shapeDistance, DateTime activation)[]? futureZones = null)
    {
        // build a pathfinding map: rasterize all forbidden zones and goals
        hints.InitPathfindMap(ctx.Map);
        // local copies of forbidden zones and goals to ensure no race conditions during async pathfinding
        (Func<WPos, float>, DateTime, ulong)[] localForbiddenZones = [.. hints.ForbiddenZones];
        Func<WPos, float>[] localGoalZones = [.. hints.GoalZones];
        return BuildFromSnapshot(ctx, ws.CurrentTime, player.Position, player.CastInfo != null, localForbiddenZones, localGoalZones, futureZones, hints, playerSpeed, forbiddenZoneCushion, avoidFutureAOEs, activationTimeCushion);
    }

    /// <summary>與 <see cref="Build"/> 相同，但輸入全部由呼叫端先取好：<c>ctx.Map</c> 已經 <c>InitPathfindMap</c> 過，不讀 WorldState、不讀 Actor。</summary>
    /// <param name="futureZones">未來危險區快照；null 時才從 <paramref name="futureSource"/> 當場取（兩者都 null＝沒有未來危險區）。</param>
    public static NavigationDecision BuildFromSnapshot(Context ctx, DateTime now, WPos playerPosition, bool playerCasting,
        (Func<WPos, float>, DateTime, ulong)[] localForbiddenZones, Func<WPos, float>[] localGoalZones,
        (Func<WPos, float> shapeDistance, DateTime activation)[]? futureZones, AIHints? futureSource,
        float playerSpeed, float forbiddenZoneCushion, bool avoidFutureAOEs, float activationTimeCushion)
    {
        // 🔴 閘門必須看**快照**，不能看還活著的那份 List。
        //    這支會被 AIBehaviour 丟到執行緒集區跑，而主執行緒每幀都會
        //    AIHintsBuilder.Update → hints.Clear() 把 ForbiddenZones/GoalZones 清空。
        //    快照拿到之後、閘門判斷之前若剛好被清掉，就會出現「快照裡明明有目標區、卻整段不 rasterize」
        //    ⇒ 權重場全平 ⇒ Destination 為 null ⇒ 角色不走、也不畫任何標線，而且完全不報錯。
        var flattened = false;
        if (localForbiddenZones.Length != 0)
        {
            var fullyBlocked = RasterizeForbiddenZones(ctx.Map, localForbiddenZones, now, ref ctx.Scratch, ref ctx.ZonesScratch, activationTimeCushion);
            // 「全部視為立即生效」＝把每格的生效時間壓成 0（與先壓時間再光柵化逐位元相同）。
            // 全圖被蓋滿時刻意不壓：壓平後「最晚才被打」就是全部危險格，會被一起當成安全格而整幀不閃。
            flattened = avoidFutureAOEs && !fullyBlocked;
            if (flattened)
                FlattenActivationTimes(ctx.Map);
        }
        // WorldToGrid 不做夾限，玩家在格線外時 x/y 會是負數，GridToIndex 因此算出負的索引（只檢查上界的守衛擋不住）。
        // 實際發生條件：AIHints.Clear() 把 PathfindMapCenter 歸零，而只有 CalculateAutoHints 會重設它；
        // 有 active boss module 且該模組沒設定中心時，中心就停在 (0,0)，於是遠離原點的區域必爆。
        // 用 InBounds 同時驗兩個軸——只檢查 index >= 0 是不夠的：x = -1、y = 1 會算出「看似合法」但屬於前一列的索引。
        // 📌 這幾個值同時也是下面診斷欄位的來源，所以提到迴圈外算一次。
        var (playerGridX, playerGridY) = ctx.Map.WorldToGrid(playerPosition);
        var playerInWindow = ctx.Map.InBounds(playerGridX, playerGridY);
        var playerCell = playerInWindow ? ctx.Map.GridToIndex(playerGridX, playerGridY) : -1;
        var goalsRasterized = false;
        if (!playerCasting) // don't rasterize goal zones if casting or if inside a very dangerous pixel
        {
            if (playerInWindow && ctx.Map.PixelMaxG[playerCell] is >= 1f or < 0f) // prioritize safety over uptime, still needs to be active for below 0 MaxG to go back inside arena bounds if needed
            {
                if (localGoalZones.Length != 0) // 同上：看快照，不要看還在被主執行緒清空的那份 List
                {
                    RasterizeGoalZones(ctx.Map, localGoalZones);
                    goalsRasterized = true;
                }
                if (forbiddenZoneCushion > 0)
                {
                    AvoidForbiddenZone(ctx.Map, forbiddenZoneCushion);
                }
            }
        }
        // 🔴 speed 不合理時一律用名目速度 —— 完整推導與「為什麼閃避不受影響」見 NominalPlayerSpeed。
        //    ⚠️ 寫成 `is > x and < y` 而不是 `!(...)`：NaN 對這個模式回 false，會正確落到代打那邊。
        var speedValid = playerSpeed is > MinValidPlayerSpeed and < MaxValidPlayerSpeed;
        var effectiveSpeed = speedValid ? playerSpeed : NominalPlayerSpeed;
        // 閃避預判：沒有未來危險區（或沒有已登記禁區、或設定關閉）時整段不執行、不配置
        var lookahead = default(DodgeLookahead.Result);
        var lookaheadFailed = false;
        if (localForbiddenZones.Length != 0 && AIHints.LookaheadEnabled && (futureZones ?? futureSource?.SnapshotFutureZones()) is { Length: > 0 } future)
        {
            ctx.Lookahead ??= new();
            var startCell = ctx.Map.GridToIndex(ctx.Map.ClampToGrid((playerGridX, playerGridY)));
            var plateau = Math.Max(DodgeLookahead.MinPlateauYalms, forbiddenZoneCushion + ctx.Map.Resolution);
            try
            {
                lookahead = DodgeLookahead.Apply(ctx.Lookahead, ctx.Map, localForbiddenZones, future, now, effectiveSpeed, LookaheadReactionSeconds, plateau, startCell);
            }
            catch (Exception ex)
            {
                // 預判只動權重：出錯就當這一次沒有預判（權重放回），照舊尋路；Steering 不翻轉
                DodgeLookahead.RestoreIfDirty(ctx.Lookahead, ctx.Map);
                lookahead = default;
                lookaheadFailed = true;
                LogLookaheadError(ctx, ex);
            }
        }
        // execute pathfinding
        ctx.ThetaStar.Start(ctx.Map, playerPosition, 1.0f / effectiveSpeed);
        var bestNodeIndex = ctx.ThetaStar.Execute();
        ref var bestNode = ref ctx.ThetaStar.NodeByIndex(bestNodeIndex);
        var waypoints = GetFirstWaypoints(ctx.ThetaStar, ctx.Map, bestNodeIndex, playerPosition);
        var leeway = bestNode.PathLeeway;
        var steering = lookahead.Applied && !lookahead.StartGood;
        // 接不上：照併入的餘裕動身；接得上但還要走一段：照預判算的最晚動身時間（詠唱太長也會趕不上）
        var lookaheadBy = steering ? leeway : lookahead.StartGood && lookahead.MoveBySeconds != float.MaxValue ? lookahead.MoveBySeconds - ServerJudgementLeadSeconds : float.MaxValue;
        // 要在第一波之前站到接得上的位置 ⇒ 剩餘時間併入餘裕（詠唱、衝刺、動身時機都看它）
        if (steering && DodgeLookahead.IsGood(ctx.Lookahead!, bestNodeIndex))
        {
            var firstWave = Math.Max(0f, lookahead.FirstWaveIn - bestNode.GScore);
            leeway = Math.Min(leeway, firstWave);
            lookaheadBy = Math.Min(bestNode.PathLeeway, firstWave - ServerJudgementLeadSeconds);
        }
        ctx.PublishLookahead(now, lookaheadBy);
        // 腳下這一格會被打：發布尋路的餘裕（含繞路、路上穿過的其他區）；壓平過的生效時間不是真的時間，不發布
        var startMaxG = playerInWindow ? ctx.Map.PixelMaxG[playerCell] : float.MaxValue;
        var startHit = !flattened && startMaxG >= 0f && startMaxG != float.MaxValue;
        var pathLeeway = startHit ? LeewayAlongPath(ctx.ThetaStar, ctx.Map, bestNodeIndex) : float.NaN;
        var pathBy = startHit ? pathLeeway - Math.Max(0f, ServerJudgementLeadSeconds - activationTimeCushion) : float.MaxValue;
        ctx.PublishPath(now, pathBy);
        ctx.PublishPathLeeway(now, pathLeeway, activationTimeCushion, waypoints.first);
        if (!lookaheadFailed && ctx.Lookahead != null && steering != ctx.Lookahead.Steering)
        {
            ctx.Lookahead.Steering = steering;
            if (ctx.LogLookahead)
                LogLookahead(ctx, in lookahead, steering, waypoints.first, bestNode.GScore, leeway);
        }
        return new()
        {
            Destination = waypoints.first,
            NextWaypoint = waypoints.second,
            LeewaySeconds = leeway,
            TimeToGoal = bestNode.GScore,
            DiagLookaheadApplied = lookahead.Applied,
            DiagLookaheadSteering = steering,
            // 診斷：全部取自本次真正用到的地圖，不重算、不猜。玩家格出視窗時不去讀陣列（那正是上面守衛擋的東西）。
            DiagGoalZones = localGoalZones.Length,
            DiagGoalsRasterized = goalsRasterized,
            DiagPlayerInWindow = playerInWindow,
            DiagPlayerMaxG = playerInWindow ? ctx.Map.PixelMaxG[playerCell] : default,
            DiagPlayerPriority = playerInWindow ? ctx.Map.PixelPriority[playerCell] : default,
            DiagMaxPriority = ctx.Map.MaxPriority,
            DiagReachablePriority = ctx.ThetaStar.MaxReachedPriority,
            DiagSearchExhausted = ctx.ThetaStar.OpenListExhausted,
            DiagSearchSteps = ctx.ThetaStar.NumSteps,
            DiagRawSpeed = playerSpeed,
            DiagSpeedSubstituted = !speedValid
        };
    }

    // 每個 Context 只記第一次，之後靜默（每幀都會再算一次，照記會洗掉 log）
    private static void LogLookaheadError(Context ctx, Exception ex)
    {
        if (ctx.Lookahead == null || ctx.Lookahead.ErrorLogged)
            return;
        ctx.Lookahead.ErrorLogged = true;
        var tag = ctx.LogTag.Length != 0 ? $"[{ctx.LogTag}]" : "";
        Service.Logger.Warning(ex, $"[閃避預判]{tag} 計算失敗，這一幀改用不預判的尋路；之後再失敗不重複記錄");
    }

    // 只在「目前位置接不接得上下一波」翻轉時寫一行（Debug），不每幀印
    private static void LogLookahead(Context ctx, in DodgeLookahead.Result r, bool steering, WPos? destination, float timeToGoal, float leeway)
    {
        var tag = ctx.LogTag.Length != 0 ? $"[{ctx.LogTag}]" : "";
        Service.Logger.Debug(steering
            ? $"[閃避預判]{tag} 目前位置接不上下一波：共 {r.Waves} 波、第一波 {r.FirstWaveIn:f1} 秒後；改往 {(destination is WPos d ? $"({d.X:f1},{d.Z:f1})" : "原地")}，約 {timeToGoal:f1} 秒到、餘裕 {(leeway < 1e6f ? $"{leeway:f1} 秒" : "不限")}（接得上的安全格 {r.GoodCells}／{r.SafeCells}）"
            : $"[閃避預判]{tag} 解除");
    }

    private static void AvoidForbiddenZone(Map map, float forbiddenZoneCushion)
    {
        var d = (int)(forbiddenZoneCushion / map.Resolution);
        // 從最小值起算：整張圖的權重都低於 -1 時（只剩「別後退」的扣分），MaxPriority 必須仍等於某一格，否則尋路的啟發值沒有終點
        map.MaxPriority = float.MinValue;
        // 走訪順序與 Map.EnumeratePixels 相同（列優先），但不配置那個 Width×Height 的陣列：這裡只用到格座標，
        // 而那個陣列每次 16×格數 bytes（120×120＝230 KB，落在 LOH），這支又是每幀都跑（AI 開著就算閒置也跑）。
        var width = map.Width;
        var height = map.Height;
        for (var py = 0; py < height; ++py)
        {
            for (var px = 0; px < width; ++px)
            {
                var cellIndex = map.GridToIndex(px, py);
                if (map.PixelMaxG[cellIndex] == float.MaxValue)
                {
                    var hasDangerousNeighbour = false;
                    for (var ox = -1; ox <= 1 && !hasDangerousNeighbour; ++ox)
                    {
                        for (var oy = -1; oy <= 1; ++oy)
                        {
                            if (ox == 0 && oy == 0)
                            {
                                continue;
                            }
                            var (nx, ny) = map.ClampToGrid((px + ox * d, py + oy * d));
                            if (map.PixelMaxG[map.GridToIndex(nx, ny)] != float.MaxValue)
                            {
                                hasDangerousNeighbour = true;
                                break;
                            }
                        }
                    }

                    if (hasDangerousNeighbour)
                    {
                        map.PixelPriority[cellIndex] -= 0.125f;
                    }
                }
                map.MaxPriority = Math.Max(map.MaxPriority, map.PixelPriority[cellIndex]);
            }
        }
    }

    /// <summary>每個有危險時間的格子（0 ≤ g &lt; MaxValue）改成 0；不可通行（負值）與安全格不動。</summary>
    private static void FlattenActivationTimes(Map map)
    {
        var maxG = map.PixelMaxG;
        var len = map.Width * map.Height;
        for (var i = 0; i < len; ++i)
        {
            ref var g = ref maxG[i];
            if (g >= 0f && g != float.MaxValue)
                g = 0f;
        }
        map.MaxG = 0f;
    }

    /// <returns>本圖每一格都在危險區或不可通行（第 4 段因此放回了「最晚才被打」的格子）時為 true。</returns>
    public static bool RasterizeForbiddenZones(Map map, (Func<WPos, float> shapeDistance, DateTime activation, ulong source)[] zones, DateTime current, ref float[] scratch, float activationTimeCushion = ActivationTimeCushion)
    {
        (Func<WPos, float> shapeDistance, float g)[] zonesScratch = [];
        return RasterizeForbiddenZones(map, zones, current, ref scratch, ref zonesScratch, activationTimeCushion);
    }

    /// <summary>與上一支相同，但分群後的危險區暫存由呼叫端提供（上一支每次新配一份）。</summary>
    /// <param name="zonesScratch">分群後的危險區暫存（只增不減、同一個 Context 一份）；用完前段清空，不留委派參照。</param>
    public static bool RasterizeForbiddenZones(Map map, (Func<WPos, float> shapeDistance, DateTime activation, ulong source)[] zones, DateTime current, ref float[] scratch,
        ref (Func<WPos, float> shapeDistance, float g)[] zonesScratch, float activationTimeCushion = ActivationTimeCushion)
    {
        // 1) Cluster activation times
        // very slight difference in activation times cause issues for pathfinding - cluster them together
        var lenZonesFixed = zones.Length;
        if (zonesScratch.Length < lenZonesFixed)
            zonesScratch = new (Func<WPos, float> shapeDistance, float g)[lenZonesFixed];
        var zonesFixed = zonesScratch;
        DateTime clusterEnd = default, globalStart = current, globalEnd = current.AddSeconds(120d);
        float clusterG = 0;
        for (var i = 0; i < lenZonesFixed; ++i)
        {
            ref var zone = ref zones[i];
            var activation = zone.activation.Clamp(globalStart, globalEnd);
            if (activation > clusterEnd)
            {
                clusterG = ActivationToG(activation, current, activationTimeCushion);
                clusterEnd = activation.AddSeconds(0.5d);
            }
            zonesFixed[i] = (zone.shapeDistance, clusterG);
        }

        var width = map.Width;
        var height = map.Height;
        // 只走本圖範圍：緩衝會重複使用、長度可能比本圖大，尾段是上一張圖的殘值。
        // 走進尾段會把殘值算進「全圖蓋滿」的格數與第 4 段的最大值，蓋滿時就放不回任何格子。
        var lenPixelMaxG = width * height;

        var resolution = map.Resolution;
        var cushion = resolution * 0.5f;
        map.MaxG = clusterG;

        // 🔴 `ref` 不能省：傳值的話下面的 `scratch = new float[...]` 只改到本地參數，呼叫端的 ctx.Scratch 永遠停在 `[]`
        //    ⇒ 這個條件**每次都成立**，等於每幀白配一個 lenPixelMaxG 大小的 float 陣列再整個丟掉。
        // ⚠️ 緩衝區常駐在 Context 上**不會製造新的資料競態**：同一個 Context 的 map.PixelMaxG／PixelPriority 本來就是同樣
        //    「只增不減、跨呼叫重複使用」的緩衝區。單次呼叫之內也安全：ChunkedParallel.For 會等全部段做完才返回，
        //    pass #1 寫、pass #2 才讀，兩者之間有隱含的屏障。
        if (scratch.Length < lenPixelMaxG)
            scratch = new float[lenPixelMaxG];
        // ref 參數不能存進欄位，所以取一個本地別名交給下面的分段工作。
        // 它與 scratch 指向**同一個陣列**，而成長過的那份在上一行已經回寫給呼叫端了。
        var buf = scratch;
        // 只填這次真的會用到的前段（與 Map.Init 對 PixelMaxG 的做法一致）：
        // 緩衝區可能比這次的地圖大，尾段不會被任何一個 pass 讀到。
        Array.Fill(buf, float.MaxValue, 0, lenPixelMaxG);

        var dy = map.LocalZDivRes * resolution * resolution;
        var dx = dy.OrthoL();
        var topLeft = map.Center - (width >> 1) * dx - (height >> 1) * dy;

        // note that a zone can partially intersect a pixel; so what we do is check each corner and set the maxg value of a pixel equal to the minimum of 4 corners
        // to avoid 4x calculations, we do a slightly tricky loop:
        // - outer loop fills row i to with g values corresponding to the 'upper edge' of cell i
        // - inner loop calculates the g value at the left border, then iterates over all right corners and fills minimums of two g values to the cells
        // - second outer loop calculates values at 'bottom' edge and then updates the values of all cells to correspond to the cells rather than edges
        // - third loops checks center and surrounding circle until cell edge to counter small cones not intersecting corners of a cell
        // 每一段（ForbiddenZonesJob.Pass）的本體在 ForbiddenZonesJob.Run；ChunkedParallel.For 等全部段做完才返回＝段與段之間有屏障。
        var job = ForbiddenZonesJob.Local;
        job.Map = map;
        job.Zones = zonesFixed;
        job.ZoneCount = lenZonesFixed;
        job.Buf = buf;
        job.Width = width;
        job.Height = height;
        job.TopLeft = topLeft;
        job.Dx = dx;
        job.Dy = dy;
        job.Cushion = cushion;
        job.Blocked = 0;
        var runner = ChunkedParallel.Local;
        try
        {
            // PASS #1 (rows): scratch[iCell] = min(G-of-left-corner, G-of-right-corner)
            job.Pass = 1;
            runner.For(job, height);
            // PASS #2 (columns): combine top corners from scratch with bottom corners, count blocked cells
            job.Pass = 2;
            runner.For(job, width);
            // PASS #3: check each pixel center to catch partial overlaps, this is needed because small cones might not intersect corners
            // with a cushion of cellsize / 2 this ensures the entire inner circle until the edge will be safe
            job.Pass = 3;
            runner.For(job, lenPixelMaxG);

            // PASS #4: if absolutely everything is blocked, free the "least dangerous" (4a: find the real max, 4b: free cells with that max)
            var fullyBlocked = job.Blocked == width * height;
            if (fullyBlocked)
            {
                job.RealMaxG = float.MinValue;
                job.Pass = 4;
                runner.For(job, lenPixelMaxG);
                job.Pass = 5;
                runner.For(job, lenPixelMaxG);
            }
            return fullyBlocked;
        }
        finally
        {
            job.Clear();
            Array.Clear(zonesFixed, 0, lenZonesFixed);
        }
    }

    public static void RasterizeGoalZones(Map map, Func<WPos, float>[] goals)
    {
        var resolution = map.Resolution;
        var width = map.Width;
        var height = map.Height;
        var dy = map.LocalZDivRes * resolution * resolution;
        var dx = dy.OrthoL();
        var topLeft = map.Center - (width >> 1) * dx - (height >> 1) * dy;
        var len = goals.Length;

        // Pass #1: row-by-row, fill in partial priorities in map.PixelPriority
        // Pass #2: column-by-column, combine top (in PixelPriority) with bottom corners and track the max
        var job = GoalZonesJob.Local;
        job.Map = map;
        job.Goals = goals;
        job.Len = len;
        job.Width = width;
        job.Height = height;
        job.TopLeft = topLeft;
        job.Dx = dx;
        job.Dy = dy;
        job.BottomRowLeft = topLeft + height * dy; // world coords for left corner of the *bottom* row
        job.GlobalMax = float.MinValue;
        var runner = ChunkedParallel.Local;
        try
        {
            job.Pass = 1;
            runner.For(job, height);
            job.Pass = 2;
            runner.For(job, width);
            map.MaxPriority = job.GlobalMax;
        }
        finally
        {
            job.Clear();
        }
    }

    // RasterizeForbiddenZones 每一段的本體（原本是 Parallel.For 的 lambda，逐式照搬）。每條呼叫端執行緒一份。
    private sealed class ForbiddenZonesJob : RangeJob
    {
        [ThreadStatic] private static ForbiddenZonesJob? _local;
        public static ForbiddenZonesJob Local => _local ??= new();

        public int Pass;
        public Map Map = null!;
        public (Func<WPos, float> shapeDistance, float g)[] Zones = [];
        public int ZoneCount; // Zones 只看前這麼多個（後段是暫存的空位）
        public float[] Buf = [];
        public int Width;
        public int Height;
        public WPos TopLeft;
        public WDir Dx;
        public WDir Dy;
        public float Cushion;
        public int Blocked;
        public float RealMaxG;

        public void Clear()
        {
            Map = null!;
            Zones = [];
            Buf = [];
        }

        public override void Run(int from, int to)
        {
            switch (Pass)
            {
                case 1:
                    RowCorners(from, to);
                    break;
                case 2:
                    ColumnCorners(from, to);
                    break;
                case 3:
                    Centers(from, to);
                    break;
                case 4:
                    FindMax(from, to);
                    break;
                case 5:
                    FreeMax(from, to);
                    break;
            }
        }

        private void RowCorners(int from, int to)
        {
            var width = Width;
            var buf = Buf;
            var zones = new ReadOnlySpan<(Func<WPos, float> shapeDistance, float g)>(Zones, 0, ZoneCount);
            for (var y = from; y < to; ++y)
            {
                var rowStart = y * width;
                var rowCorner = TopLeft + y * Dy;

                var leftPos = rowCorner;
                var leftG = CalculateMaxG(zones, leftPos);

                for (var x = 0; x < width; ++x)
                {
                    var rightPos = leftPos + Dx;
                    var rightG = CalculateMaxG(zones, rightPos);
                    buf[rowStart + x] = Math.Min(leftG, rightG);
                    leftPos = rightPos;
                    leftG = rightG;
                }
            }
        }

        private void ColumnCorners(int from, int to)
        {
            var width = Width;
            var height = Height;
            var buf = Buf;
            var map = Map;
            var localBlocked = 0;
            var zones = new ReadOnlySpan<(Func<WPos, float> shapeDistance, float g)>(Zones, 0, ZoneCount);
            for (var x = from; x < to; ++x)
            {
                // The bottom row's corner is topLeft + height*dy + x*dx
                var cyBottom = TopLeft + height * Dy + x * Dx;
                var bleftG = CalculateMaxG(zones, cyBottom);

                var columnStart = x;
                var bottomG = bleftG;
                for (var y = height - 1; y >= 0; y--)
                {
                    var jCell = columnStart + y * width;
                    // top corner from pass #1
                    var topG = buf[jCell];
                    ref var pixelMaxG = ref map.PixelMaxG[jCell];
                    var cellG = Math.Min(Math.Min(topG, bottomG), pixelMaxG);

                    pixelMaxG = cellG;
                    if (cellG != float.MaxValue)
                    {
                        map.PixelPriority[jCell] = float.MinValue;
                        localBlocked++;
                    }
                    bottomG = topG;
                }
            }
            Interlocked.Add(ref Blocked, localBlocked);
        }

        private void Centers(int from, int to)
        {
            var map = Map;
            var localBlocked = 0;
            var zones = new ReadOnlySpan<(Func<WPos, float> shapeDistance, float g)>(Zones, 0, ZoneCount);
            for (var idx = from; idx < to; ++idx)
            {
                var (px, py) = map.IndexToGrid(idx);
                var centerPos = map.GridToWorld(px, py, 0.5f, 0.5f);

                var centerG = CalculateMaxG(zones, centerPos, Cushion);
                var oldVal = map.PixelMaxG[idx];
                if (centerG < oldVal)
                {
                    map.PixelMaxG[idx] = centerG;
                    if (oldVal == float.MaxValue)
                    {
                        map.PixelPriority[idx] = float.MinValue;
                        localBlocked++;
                    }
                }
            }
            Interlocked.Add(ref Blocked, localBlocked);
        }

        private void FindMax(int from, int to)
        {
            var maxG = Map.PixelMaxG;
            var localMax = float.MinValue;
            for (var i = from; i < to; ++i)
            {
                ref var val = ref maxG[i];
                localMax = (val > localMax) ? val : localMax;
            }
            // Merge local maxima with an atomic
            float initVal, computedVal;
            do
            {
                initVal = RealMaxG;
                computedVal = Math.Max(initVal, localMax);
            }
            while (initVal != Interlocked.CompareExchange(ref RealMaxG, computedVal, initVal));
        }

        private void FreeMax(int from, int to)
        {
            var map = Map;
            var realMaxG = RealMaxG;
            for (var i = from; i < to; ++i)
            {
                ref var pixelMaxG = ref map.PixelMaxG[i];
                if (pixelMaxG == realMaxG)
                {
                    pixelMaxG = float.MaxValue;
                    map.PixelPriority[i] = 0f;
                }
            }
        }
    }

    // RasterizeGoalZones 每一段的本體（原本是 Parallel.For 的 lambda，逐式照搬）。每條呼叫端執行緒一份。
    private sealed class GoalZonesJob : RangeJob
    {
        [ThreadStatic] private static GoalZonesJob? _local;
        public static GoalZonesJob Local => _local ??= new();

        public int Pass;
        public Map Map = null!;
        public Func<WPos, float>[] Goals = [];
        public int Len;
        public int Width;
        public int Height;
        public WPos TopLeft;
        public WDir Dx;
        public WDir Dy;
        public WPos BottomRowLeft;
        public float GlobalMax;

        public void Clear()
        {
            Map = null!;
            Goals = [];
        }

        public override void Run(int from, int to)
        {
            if (Pass == 1)
                Rows(from, to);
            else
                Columns(from, to);
        }

        private void Rows(int from, int to)
        {
            var goals = Goals;
            var len = Len;
            var width = Width;
            var priority = Map.PixelPriority;
            for (var y = from; y < to; ++y)
            {
                // For row y, compute the position of the 'left corner' in world coords
                var cy = TopLeft + y * Dy;

                // Sum up all goals at the left corner (x=0)
                float leftP = 0;
                for (var i = 0; i < len; ++i)
                {
                    leftP += goals[i](cy);
                }

                // Now walk across the row from x=0..(width-1), computing right corner
                var rowStart = y * width;
                var cx = cy;
                for (var x = 0; x < width; ++x)
                {
                    cx += Dx;
                    float rightP = 0;
                    for (var i = 0; i < len; ++i)
                    {
                        rightP += goals[i](cx);
                    }

                    // Store the min in PixelPriority
                    priority[rowStart + x] = Math.Min(leftP, rightP);

                    // Shift left -> right
                    leftP = rightP;
                }
            }
        }

        private void Columns(int from, int to)
        {
            var goals = Goals;
            var len = Len;
            var width = Width;
            var height = Height;
            var map = Map;
            var localMax = float.MinValue;
            for (var x = from; x < to; ++x)
            {
                // For column x, compute the bottom-left corner
                var cyBottom = BottomRowLeft + x * Dx;

                // The 'left' bottom corner's priority
                float bleftP = 0;
                for (var i = 0; i < len; ++i)
                {
                    bleftP += goals[i](cyBottom);
                }

                var bottomP = bleftP;
                var iCell = (height - 1) * width + x;

                for (var y = height - 1; y >= 0; --y, iCell -= width)
                {
                    var topP = map.PixelPriority[iCell];

                    // If this pixel is not blocked (PixelMaxG == float.MaxValue),
                    // we keep the min of topP and bottomP. Otherwise, we set it to float.MinValue.
                    if (map.PixelMaxG[iCell] == float.MaxValue)
                    {
                        var cellP = Math.Min(topP, bottomP);
                        map.PixelPriority[iCell] = cellP;
                        if (cellP > localMax)
                            localMax = cellP;
                    }
                    else
                    {
                        map.PixelPriority[iCell] = float.MinValue;
                    }

                    // Shift bottom -> top for next iteration
                    bottomP = topP;
                }
            }
            // Final merge across threads
            float initVal, newVal;
            do
            {
                initVal = GlobalMax;
                newVal = Math.Max(initVal, localMax);
            }
            while (initVal != Interlocked.CompareExchange(ref GlobalMax, newVal, initVal));
        }
    }
    private static float ActivationToG(DateTime activation, DateTime current, float activationTimeCushion) => Math.Max(0f, (float)(activation - current).TotalSeconds - activationTimeCushion);

    private static float CalculateMaxG(ReadOnlySpan<(Func<WPos, float> shapeDistance, float g)> zones, WPos p, float cushion = 0f)
    {
        var len = zones.Length;
        var threshold = cushion;
        for (var i = 0; i < len; ++i)
        {
            ref readonly var z = ref zones[i];
            if (z.shapeDistance(p) < threshold)
                return z.g;
        }
        return float.MaxValue;
    }

    /// <summary>從 <paramref name="cell"/> 沿父節點走回起點，各節點 <c>PathLeeway</c> 的最小值＝整條路的餘裕。</summary>
    /// <remarks>單看終點節點不夠：路在安全格轉彎後，ThetaStar 的直線檢查從轉彎點重新起算，終點只剩最後一段的值
    /// （整段都是安全格時是 <c>float.MaxValue</c>，即使起點那一格會被打）。</remarks>
    private static float LeewayAlongPath(ThetaStar pf, Map map, int cell)
    {
        var leeway = float.MaxValue;
        // 次數上限與 GetFirstWaypoints 相同：父節點鏈極少數情況會成環
        for (var left = map.Width * map.Height; left > 0; --left)
        {
            ref var node = ref pf.NodeByIndex(cell);
            leeway = Math.Min(leeway, node.PathLeeway);
            if (cell == pf.StartNodeIndex)
                break;
            cell = node.ParentIndex;
        }
        return leeway;
    }

    private static (WPos? first, WPos? second) GetFirstWaypoints(ThetaStar pf, Map map, int cell, WPos startingPos)
    {
        ref var startingNode = ref pf.NodeByIndex(cell);
        var iterations = 0; // iteration counter to prevent rare cases of infinite loops
        var maxIterations = map.Width * map.Height;

        if (startingNode.GScore == 0f && startingNode.PathMinG == float.MaxValue)
            return (null, null); // we're already in safe zone

        var nextCell = cell;
        do
        {
            ref var node = ref pf.NodeByIndex(cell);
            if (pf.NodeByIndex(node.ParentIndex).GScore == 0f || ++iterations == maxIterations)
            {
                //var dest = pf.CellCenter(cell);
                // if destination coord matches player coord, do not move along that coordinate, this is used for precise positioning
                var destCoord = map.IndexToGrid(cell);
                var playerCoordFrac = map.WorldToGridFrac(startingPos);
                var playerCoord = Map.FracToGrid(playerCoordFrac);
                var dest = map.GridToWorld(destCoord.x, destCoord.y, destCoord.x == playerCoord.x ? playerCoordFrac.X - playerCoord.x : 0.5f, destCoord.y == playerCoord.y ? playerCoordFrac.Y - playerCoord.y : 0.5f);

                var next = pf.CellCenter(nextCell);
                return (dest, next);
            }
            nextCell = cell;
            cell = node.ParentIndex;
        }
        while (true);
    }
}
