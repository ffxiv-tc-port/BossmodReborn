using System.Threading;
using BossMod.AI;
using BossMod.Pathfinding;

namespace BossMod.Autorotation.MiscAI;

public sealed class NormalMovement : RotationModule
{
    public enum Track { Destination, Range, Cast, SpecialModes, ForbiddenZoneCushion }
    public enum DestinationStrategy { None, Pathfind, Explicit }
    public enum RangeStrategy { Any, MaxRange, GreedGCDExplicit, GreedLastMomentExplicit, GreedAutomatic }
    public enum CastStrategy { Leeway, Explicit, Greedy, FinishMove, DropMove, FinishInstants, DropInstants }
    // Track 列舉本身刻意不動 —— 成員名是 preset 的序列化鍵，而且索引要對得上 Configs 順序。
    public enum SpecialModesStrategy { Automatic, Ignore }

    public const float GreedTolerance = 0.15f;

    public static NormalMovement? Instance;

    // ---- 移動擁有權（給舊的 AI 走位讓路用）----

    private bool _ownsMovement;

    /// <summary>
    /// 這一幀「自動移動」模組是不是移動的<b>唯一擁有者</b>。
    /// </summary>
    /// <remarks>
    /// 🔴 存在的理由是<b>兩套走位會跨幀交替接管，表現成角色抖動</b>：<c>Plugin.DrawUI</c> 先跑 <c>_rotation.Update()</c>
    /// （本模組寫 <c>Hints.ForcedMovement</c>），再跑 <c>_ai.Update()</c>（<c>AIController.Update</c> 只在
    /// <c>ForcedMovement == null</c> 時寫<b>它自己另外算的</b>目的地）。本模組在詠唱擋門、已站到位、擊退未結算、
    /// Pyretic 將至等情況會讓出那一幀，舊 AI 就用<b>不同的演算法、不同的 MoveDelay</b> 算出的目的地接手。
    /// 接管權逐幀交替＝方向逐幀跳動＝使用者看到的抖動。
    /// <para>
    /// ⚠️ 判準刻意是「<b>本模組會不會做移動決策</b>」而不是「本模組存不存在」：
    /// 使用者把 <c>Destination</c> 軌設成 <see cref="DestinationStrategy.None"/> 時，
    /// 本模組整場不碰移動，這時必須讓舊 AI 照舊接管，否則兩邊都不動。
    /// </para>
    /// <para>
    /// 🔴🔴 <b>擁有權每幀都必須重新舉手，不是設一次就永久成立。</b>AI 讀到的不一定是本幀的值：
    /// <c>AIBehaviour.Execute</c> 把 <c>NavigationDecision.Build</c> 丟到背景，讀本旗標的 <c>UpdateMovement</c> 通常跑在<b>執行緒集區、尋路做完之後</b>；
    /// 而排在前面的模組或本模組自己的 <see cref="Execute"/> 擲例外時，這一幀不會舉手，但 <see cref="Instance"/> 還在。
    /// 旗標若留在最後一次的 true，AI 就永遠讓位 ⇒ 兩邊都不動、而且完全不報錯。所以
    /// <see cref="ReleaseMovementOwnership"/> 由 <c>RotationModuleManager.Update</c> 在跑模組迴圈<b>之前</b>
    /// 把旗標放下，<see cref="Execute"/> 再重新舉起來；<see cref="Execute"/> 半路擲例外時也會放下。
    /// </para>
    /// <para>
    /// ⚠️ 舉手刻意放在<b>所有提早 return 之前</b>：
    /// 那些 return 是「這一幀不移動」而不是「不再負責移動」，拿它們當判準會讓舊 AI 正好在本模組
    /// 刻意站住不動的那些幀插進來（例如擊退結算中）。
    /// </para>
    /// <para>
    /// 🔴🔴 <b>唯一的例外是「尋路連續 <see cref="NoDestinationHoldSeconds"/> 秒回不出目的地」</b>：那不是「我決定不動」而是<b>「我沒有意見」</b>。
    /// <c>AIBehaviour</c> 自己的目標區（跟隨主人、閃避方向偏好、pre-dodge 錨點、「沒人給目標區就走向目標」的退路）是在
    /// <c>_ai.Update()</c> 才加進 <c>hints</c> 的，本模組<b>這一幀根本讀不到</b>；沒有意見時繼續佔著擁有權＝兩邊都不動，而且完全不報錯。
    /// 單一擁有者原則沒有被破壞：只要算得出目的地就仍然獨佔。
    /// 🔴 交還必須遲滯：立刻交還會逐幀換手＝兩套不同的權重場輪流寫方向＝走走停停，見 <see cref="NoDestinationHoldSeconds"/>。
    /// </para>
    /// <para>
    /// 📌 模組不在啟用中的預設集裡時 <see cref="Instance"/> 會在 <see cref="Dispose"/> 被清成 null
    /// （<c>RotationModuleManager.DirtyActiveModules</c> 會 Dispose 掉舊模組），於是自動退回舊行為。
    /// </para>
    /// </remarks>
    public static bool OwnsMovement => Instance?._ownsMovement ?? false;

    /// <summary>目前這一段交還擁有權的原因是「沒有人要求移動」（閒置常態）。</summary>
    /// <remarks>給 <c>AIBehaviour.LogMovementOwnership</c> 挑 log 等級用：兩邊講的是同一次換手，只降一邊會讓另一邊變成沒有說明的孤兒行。</remarks>
    public static bool IdleNoDestination => Instance?._idleNoDestination ?? false;

    /// <summary>
    /// 把移動擁有權放下。由 <c>RotationModuleManager.Update</c> 在跑模組迴圈<b>之前</b>呼叫，
    /// 本模組若這一幀真的有跑，會在 <see cref="Execute"/> 最前面重新舉手。
    /// </summary>
    /// <remarks>
    /// ⚠️ 放下與重新舉起之間會有一小段「無人舉手」的空窗（同一次同步的 <c>Update()</c> 之內，
    /// 排在本模組前面的其他模組執行的那段時間，量級是微秒）。AI 的接續剛好落在那個空窗時，
    /// 最多讓舊 AI 多寫一次導航目標＝<b>最多一次的抖動</b>；相對於舊寫法的<b>永久卡死</b>，這個取捨是刻意的。
    /// </remarks>
    public static void ReleaseMovementOwnership()
    {
        if (Instance is { } m)
            m._ownsMovement = false;
    }

    // ---- 顯示層（純顯示，不參與任何移動決策）----

    // 本幀移動決策的快照，給世界疊加層畫路徑用。Execute 與繪製端（UIRotationWindow.PreOpenCheck）同在 UiBuilder.Draw 這一個回呼中依序執行，兩者之間不需要同步。
    // 🔴 但**第三個寫入端 Dispose 不在那條時序上**：AIBehaviour.Execute 在背景尋路之後的整段通常跑在**執行緒集區**上，其中的 `autorot.Preset = ...`
    //    會走 RotationModuleManager.DirtyActiveModules → Module.Dispose() ⇒ 本模組的 Dispose 會在集區執行緒上清這個欄位。
    // ⇒ 這個欄位必須是**單一引用**（record class 而不是 record struct）：可空結構的指派不是原子操作，跨執行緒讀得到撕裂值；
    //    再用 Interlocked.Exchange 讓「讀取後清空」也變成不可分割的一步。
    public sealed record class MovementVisualization(WPos Destination, WPos? NextWaypoint, bool Urgent);

    private static MovementVisualization? _pendingVisualization;

    // 🔴 刻意做成「讀取後清空」而不是留著上一次的值。
    // Execute 有多條提早 return 的路徑（移動被別的模組接管、沒有目的地、已經站到位、擊退尚未結算、
    // Pyretic 將至…），而且這個模組不在啟用中的預設集裡時根本不會被呼叫。
    // 若沿用舊值，上述每一種情況都會讓上一幀的線繼續畫在早已過期的位置上。
    // Interlocked.Exchange：讀取與清空是不可分割的一步，沒有「讀完到寫 null 之間被 Execute 插進來」的遺失更新。
    public static MovementVisualization? ConsumeVisualization() => Interlocked.Exchange(ref _pendingVisualization, null);

    // LeewaySeconds 低於這個值就換成危險色。取 1 秒是對齊 NavigationDecision.ActivationTimeCushion
    // 的預設值（同樣是 1 秒）—— 那是尋路自己認定的安全緩衝，低於它代表已經在吃緩衝了。
    public const float UrgentLeewaySeconds = 1f;

    // ⚠️ 刻意用實例欄位而不是 static：static 欄位初始設在 beforefieldinit 的型別上，
    // 可能早在 RotationModuleRegistry 用反射呼叫 Definition() 掃描模組時就被觸發，
    // 而 ConfigRoot.Get 是字典索引 —— 若那時 Service.Config.Initialize() 還沒跑就會擲
    // KeyNotFoundException，整個外掛載入失敗。實例只在預設集啟用這個模組時才建立，必定在初始化之後。
    private readonly AutorotationConfig _visualConfig = Service.Config.Get<AutorotationConfig>();
    private readonly AIConfig _aiConfig = Service.Config.Get<AIConfig>();

    public NormalMovement(RotationModuleManager manager, Actor player) : base(manager, player)
    {
        Instance = this;
        // 建構子只在主執行緒（重建模組時）跑：舊實例的 Execute 不會再被呼叫，背景工作做完了就不會再碰它的 Context
        if (Interlocked.Exchange(ref _retired, null) is { } r)
        {
            _navCtx.AdoptBuffers(r.Nav);
            if (r.Job == null || !r.Job.Running)
                _bgCtx.AdoptBuffers(r.Bg);
        }
    }

    /// <summary>上一個被 Dispose 的實例的兩份尋路 Context；預設集的模組常被整批重建（例如隊伍格變動），下一個實例沿用它們的大緩衝。</summary>
    /// <remarks>Dispose 可能跑在執行緒集區上，所以這裡只登記、由建構子接手。</remarks>
    private sealed record class RetiredBuffers(NavigationDecision.Context Nav, NavigationDecision.Context Bg, BackgroundJob? Job);
    private static RetiredBuffers? _retired;

    public override void Dispose()
    {
        Instance = null;
        // 這一行可能跑在執行緒集區上（見上方 MovementVisualization 的說明）。
        Interlocked.Exchange(ref _pendingVisualization, null);
        Volatile.Write(ref _retired, new(_navCtx, _bgCtx, _bgJob));
        base.Dispose();
    }

    public static RotationModuleDefinition Definition()
    {
        var res = new RotationModuleDefinition("Automatic movement", "Automatically move character based on pathfinding or explicit coordinates.", "AI", "veyn", RotationModuleQuality.Good, new(~0ul), 1000, 1, RotationModuleOrder.Movement, CanUseWhileRoleplaying: true);
        res.Define(Track.Destination).As<DestinationStrategy>("Destination", "Destination", 30)
            .AddOption(DestinationStrategy.None, "No automatic movement")
            .AddOption(DestinationStrategy.Pathfind, "Use standard pathfinding to find best position")
            .AddOption(DestinationStrategy.Explicit, "Move to specific point", supportedTargets: ActionTargets.Area);

        // note that these options used to be melee-specific - internal names are kept unchanged for convenience
        res.Define(Track.Range).As<RangeStrategy>("Range", "Range", 20)
            .AddOption(RangeStrategy.Any, "Go directly to destination")
            .AddOption(RangeStrategy.MaxRange, "Stay within maximum effective range of target closest to destination", supportedTargets: ActionTargets.Hostile)
            .AddOption(RangeStrategy.GreedGCDExplicit, "Stay within effective range until last GCD; ensure destination is reached by the plan entry end", supportedTargets: ActionTargets.Hostile)
            .AddOption(RangeStrategy.GreedLastMomentExplicit, "Stay within effective range until last possible moment; ensure destination is reached by the plan entry end", supportedTargets: ActionTargets.Hostile)
            .AddOption(RangeStrategy.GreedAutomatic, "Stay within effective range as long as possible; try to ensure safety is reached before mechanic resolves", supportedTargets: ActionTargets.Hostile)
            /*.AddOption(RangeStrategy.Drag, "Drag the target to specified spot, but maintain gcd uptime", supportedTargets: ActionTargets.Hostile)*/; // TODO

        res.Define(Track.Cast).As<CastStrategy>("Cast", "Cast", 10)
            .AddOption(CastStrategy.Leeway, "Continue slidecasting as long as there is enough time to get to safety")
            .AddOption(CastStrategy.Explicit, "Continue slidecasting as long as there is enough time to reach destination by the plan entry end")
            .AddOption(CastStrategy.Greedy, "Don't stop casting, even when it risks getting clipped by aoes")
            .AddOption(CastStrategy.FinishMove, "Start moving as soon as cast ends, use instants until destination is reached")
            .AddOption(CastStrategy.DropMove, "Start moving asap, interrupting casts if necessary, use instants until destination is reached")
            .AddOption(CastStrategy.FinishInstants, "Don't use any more casts after current cast ends")
            .AddOption(CastStrategy.DropInstants, "Don't cast, interrupt current cast if needed");
        res.Define(Track.SpecialModes).As<SpecialModesStrategy>("SpecialModes", "Special", -1)
            .AddOption(SpecialModesStrategy.Automatic, "Automatically deal with special conditions (knockbacks, pyretics, etc)")
            .AddOption(SpecialModesStrategy.Ignore, "Ignore any special conditions (knockbacks, pyretics, etc)");
        // Overdodge 是公尺數滑桿（取代以前的 4 個固定檔位 0 / 0.5 / 1.5 / 3 公尺）。
        // 🔴 defaultValue 明寫 0：舊的第一個選項是 None，而列舉軌道的空值就是 Option = 0
        //    ⇒ 沒設過這條軌的人拿到的是「不使用緩衝」，不是中間檔。這是回歸錨。
        // ⚠️ 上限 5 公尺是有意義的界限而不是隨手取：緩衝是加在禁區半徑上的，開太大會讓可站位置
        //    在小場地整個消失，尋路就回不出目的地 —— 那正是「算不出目的地＝角色站著不動」那條路徑。
        res.DefineFloat(Track.ForbiddenZoneCushion, "Overdodge", 0f, 5f, 25f, defaultValue: 0f, speed: 0.1f);
        return res;
    }

    private readonly NavigationDecision.Context _navCtx = new();
    private NavigationDecision.Context? _usedCtx;

    /// <summary>本模組這一幀的尋路結果出自哪一份 Context（詠唱讓位讀它發布的閃避預判與腳下餘裕；只讀那幾個 Volatile 欄位）。</summary>
    public static NavigationDecision.Context? NavigationContext => Instance is { } m ? m._usedCtx ?? m._navCtx : null;

    #region 背景尋路（沿用 AI 那一步發動的背景結果）

    /// <summary>背景結果最多容許多舊（秒）；超過就當場在主執行緒重算。</summary>
    /// <remarks>只收「上一幀」發出去的那一次，這道是給掉幀用的：卡頓時一幀的時間差可能很大，那時寧可多算一次。</remarks>
    public const double BackgroundMaxAgeSeconds = 0.1d;

    // 背景那份 Context：只有背景工作寫；主執行緒只在工作完成後、下一次發動之前讀（都在主執行緒上依序發生）
    private readonly NavigationDecision.Context _bgCtx = new();
    private BackgroundJob? _bgJob;
    // 這一幀要不要發背景尋路、用哪一組輸入（Execute 寫、LaunchBackgroundNavigation 讀，都在主執行緒）
    private bool _bgReqValid;
    private uint _bgReqFrame;
    private float _bgReqSpeed, _bgReqCushion;
    private int _bgReqForbidden, _bgReqGoals, _bgReqFuture;
    // 已發出去的那一次的輸入：發動前在主執行緒寫好，背景工作只讀；工作沒做完之前不會再發動
    private uint _bgFrame;
    private DateTime _bgTime;
    private Actor? _bgPlayer;
    private WPos _bgPosition;
    private bool _bgCasting;
    private float _bgSpeed, _bgCushion;
    private (Func<WPos, float>, DateTime, ulong)[] _bgForbidden = [];
    private Func<WPos, float>[] _bgGoals = [];
    private (Func<WPos, float> shapeDistance, DateTime activation)[] _bgFuture = [];
    private int _bgReused;
    private bool _bgFaultLogged;

    private static readonly MinuteLogDigest _bgDigest = new("[自動移動][背景尋路][每分鐘彙總]",
        ["背景尋路還沒算完、改在主執行緒算", "背景尋路擲例外、改在主執行緒算"], [-1, -1], []);

    /// <summary>
    /// 把本幀 <see cref="Execute"/> 記下的尋路請求丟到執行緒集區算，下一幀直接沿用結果。
    /// </summary>
    /// <remarks>
    /// 契約：只能在主執行緒、本幀 <see cref="Execute"/> 之後呼叫（<c>AIBehaviour.Execute</c> 的開頭）。AI 沒開就沒有人呼叫，
    /// 本模組每幀照舊在主執行緒算。危險區／目標區取的是本模組當時看到的前 N 筆，之後別人加的不算，與當場算的輸入相同。
    /// </remarks>
    public void LaunchBackgroundNavigation()
    {
        if (!_bgReqValid || _bgReqFrame != World.Frame.Index)
            return;
        _bgReqValid = false;
        _bgJob ??= new(this);
        if (_bgJob.Running)
            return;
        var forbidden = Hints.ForbiddenZones;
        var goals = Hints.GoalZones;
        var future = Hints.FutureForbiddenZones;
        if (forbidden.Count < _bgReqForbidden || goals.Count < _bgReqGoals || future.Count < _bgReqFuture)
            return;
        try
        {
            // 長度與上一次相同就就地覆寫：工作沒在算，上一次的已經讀完（BuildFromSnapshot 只看長度，所以長度要剛好）
            if (_bgForbidden.Length != _bgReqForbidden)
                _bgForbidden = _bgReqForbidden == 0 ? [] : new (Func<WPos, float>, DateTime, ulong)[_bgReqForbidden];
            forbidden.CopyTo(0, _bgForbidden, 0, _bgReqForbidden);
            if (_bgGoals.Length != _bgReqGoals)
                _bgGoals = _bgReqGoals == 0 ? [] : new Func<WPos, float>[_bgReqGoals];
            goals.CopyTo(0, _bgGoals, 0, _bgReqGoals);
            // 與 Build 相同：沒有危險區或沒開閃避預判時根本不取未來危險區
            var futureCount = _bgReqForbidden != 0 && AIHints.LookaheadEnabled ? _bgReqFuture : 0;
            if (_bgFuture.Length != futureCount)
                _bgFuture = futureCount == 0 ? [] : new (Func<WPos, float> shapeDistance, DateTime activation)[futureCount];
            future.CopyTo(0, _bgFuture, 0, futureCount);
            Hints.InitPathfindMap(_bgCtx.Map);
        }
        catch (Exception ex)
        {
            // 這支跑在 AI 的 Execute 開頭：不能讓它擲出去把 AI 這一幀整段跳過
            LogBackgroundFault(ex);
            return;
        }
        _bgFrame = World.Frame.Index;
        _bgTime = World.CurrentTime;
        _bgPlayer = Player;
        _bgPosition = Player.Position;
        _bgCasting = Player.CastInfo != null;
        _bgSpeed = _bgReqSpeed;
        _bgCushion = _bgReqCushion;
        _bgJob.Start();
    }

    /// <summary>每個模組實例重用同一個，不配置。狀態只有三種，主執行緒只在「不在算」時改它，背景只做「算 → 做完」那一步。</summary>
    private sealed class BackgroundJob(NormalMovement owner) : IThreadPoolWorkItem
    {
        private const int Idle = 0, Busy = 1, Done = 2;
        private int _state;
        private NavigationDecision _result;
        private Exception? _error;

        public bool Running => Volatile.Read(ref _state) == Busy;

        public void Start()
        {
            _error = null;
            Volatile.Write(ref _state, Busy);
            ThreadPool.UnsafeQueueUserWorkItem(this, preferLocal: false);
        }

        public void Execute()
        {
            try
            {
                _result = owner.RunBackgroundNavigation();
            }
            catch (Exception ex)
            {
                _error = ex;
            }
            Volatile.Write(ref _state, Done);
        }

        /// <summary>做完了就取走（之後回到閒置）；還在算或沒發過回 false。</summary>
        public bool TryTake(out NavigationDecision result, out Exception? error)
        {
            result = default;
            error = null;
            if (Volatile.Read(ref _state) != Done)
                return false;
            result = _result;
            error = _error;
            Volatile.Write(ref _state, Idle);
            return true;
        }
    }

    private NavigationDecision RunBackgroundNavigation()
        => NavigationDecision.BuildFromSnapshot(_bgCtx, _bgTime, _bgPosition, _bgCasting, _bgForbidden, _bgGoals, _bgFuture, null, _bgSpeed, _bgCushion, false, NavigationDecision.ActivationTimeCushion);

    private void RequestBackgroundNavigation(float speed, float cushion)
    {
        _bgReqValid = true;
        _bgReqFrame = World.Frame.Index;
        _bgReqSpeed = speed;
        _bgReqCushion = cushion;
        _bgReqForbidden = Hints.ForbiddenZones.Count;
        _bgReqGoals = Hints.GoalZones.Count;
        _bgReqFuture = Hints.FutureForbiddenZones.Count;
    }

    /// <summary>上一幀發出去、輸入與這一幀要算的相同（同一個玩家、速度、緩衝）而且已經算完的背景結果。</summary>
    private bool TakeBackgroundNavigation(float speed, float cushion, out NavigationDecision navi)
    {
        navi = default;
        if (_bgJob == null)
            return false;
        if (_bgJob.Running)
        {
            _bgDigest.Record(0, World.CurrentTime, $"本次載入累計沿用背景結果 {_bgReused} 次");
            return false;
        }
        if (!_bgJob.TryTake(out var result, out var error))
            return false;
        if (error != null)
        {
            LogBackgroundFault(error);
            return false;
        }
        if (_bgFrame + 1 != World.Frame.Index || (World.CurrentTime - _bgTime).TotalSeconds > BackgroundMaxAgeSeconds
            || _bgPlayer != Player || _bgSpeed != speed || _bgCushion != cushion)
            return false;
        navi = result;
        ++_bgReused;
        return true;
    }

    private void LogBackgroundFault(Exception? ex)
    {
        _bgDigest.Record(1, World.CurrentTime);
        if (_bgFaultLogged)
            return;
        _bgFaultLogged = true;
        Service.Logger.Warning(ex, "[NormalMovement] 背景尋路擲例外，這一幀改在主執行緒算；之後再發生只計入每分鐘彙總");
    }

    #endregion

    public const float MeleeRange = 2.6f; // Note: melee range is always hitbox radius + 2.6 for auto attacks, doesn't matter if skills have 3 range...
    public const float CasterRange = 25;

    public override void Execute(StrategyValues strategy, Actor? primaryTarget, float estimatedAnimLockDelay, bool isMoving)
    {
        // 🔴 先算移動擁有權，而且刻意放在所有提早 return 之前 —— 見 OwnsMovement 的說明。
        //    下面每一條提早 return（別的模組已在移動、擊退未結算、Pyretic 將至、沒有目的地…）
        //    都是「這一幀不移動」而不是「不再負責移動」；用它們當判準會讓舊 AI 在正是本模組
        //    刻意站住不動的那些幀插進來，那恰好是最不該移動的時候。
        var destinationOpt = strategy.Option(Track.Destination);
        var destinationStrategy = destinationOpt.As<DestinationStrategy>();
        _ownsMovement = destinationStrategy != DestinationStrategy.None;
        MovementLog.Digest.Tick(World.CurrentTime);

        // 🔴 半路擲例外＝這一幀我們什麼都沒決定，就不能繼續佔著移動擁有權：
        //    true 留在旗標上的話，Execute 之後每幀都在同一個地方爆掉 ⇒ AI 永久讓位、
        //    兩邊都不動。刻意**不吞**例外（照樣往上丟給 Dalamud 記 log），只是先把手放下。
        try
        {
            ExecuteCore(strategy, primaryTarget, destinationOpt, destinationStrategy);
        }
        catch
        {
            _ownsMovement = false;
            throw;
        }
    }

    // 委派只讀建立時抄下的目標座標：背景尋路在執行緒池上求值時，主執行緒可能已經把 Hints.InteractWithTarget 清成 null
    private readonly GoalMemo _interactGoal = new();

    private static Func<WPos, float> InteractGoal(WPos target, float _, float __) => p =>
    {
        var length = (p - target).LengthSq();

        // 99% of eventobjects have an interact range of 3.5y, while the rest have a range of 2.09y
        // checking only for the shorter range here would be fine in the vast majority of cases, but it can break interact pathfinding in the case that the target object is partially covered by a forbidden zone with a radius between 2.1 and 3.5
        // this is specifically an issue in the metal gear thancred solo duty in endwalker
        return length <= 4.3681f ? 101f : length <= 12.25f ? 100f : 0;
    };

    private void ExecuteCore(StrategyValues strategy, Actor? primaryTarget, StrategyValues.OptionRef destinationOpt, DestinationStrategy destinationStrategy)
    {
        // do nothing if we're already being moved by some other module (i.e. quest battle pathfinding)
        if (Hints.ForcedMovement != null)
            return;

        var castOpt = strategy.Option(Track.Cast);
        var castStrategy = castOpt.As<CastStrategy>();
        if (castStrategy is CastStrategy.FinishInstants or CastStrategy.DropInstants)
        {
            Hints.MaxCastTime = 0;
            Hints.ForceCancelCast |= castStrategy == CastStrategy.DropInstants;
        }

        var allowSpecialModes = strategy.Option(Track.SpecialModes).As<SpecialModesStrategy>() == SpecialModesStrategy.Automatic;
        if (allowSpecialModes)
        {
            if (Player.PendingKnockbacks.Count > 0)
                return; // do not move if there are any unresolved knockbacks - the positions are taken at resolve time, so we might fuck things up

            if (Hints.ImminentSpecialMode.mode == AIHints.SpecialMode.Pyretic && Hints.ImminentSpecialMode.activation <= World.FutureTime(1d))
            {
                Hints.ForceCancelCast = true; // this is only useful if autopyretic tweak is disabled
                return; // pyretic is imminent, do not move
            }

            if (Hints.ImminentSpecialMode.mode == AIHints.SpecialMode.Freezing && Hints.ImminentSpecialMode.activation <= World.FutureTime(0.5d))
                Hints.WantJump = true;

            if (Hints.InteractWithTarget is { } interactTarget)
            {
                // strongly prefer moving towards interact target
                Hints.GoalZones.Add(_interactGoal.Get(interactTarget.Position, default, default, InteractGoal));
            }
        }

        var speed = World.Client.MoveSpeed;
        // 滑桿值直接就是公尺數（拉到 0.5 / 1.5 / 3 時與舊檔位等價）。
        var cushionSize = strategy.GetFloat(Track.ForbiddenZoneCushion);
        // 下面讀 Map／ThetaStar 的地方一律讀 ctx：沿用背景結果時那是背景那份，與 navi 出自同一次尋路
        var ctx = _navCtx;
        NavigationDecision navi;
        if (destinationStrategy == DestinationStrategy.Pathfind)
        {
            if (TakeBackgroundNavigation(speed, cushionSize, out navi))
                ctx = _bgCtx;
            else
                navi = NavigationDecision.Build(_navCtx, World, Hints, Player, speed, forbiddenZoneCushion: cushionSize);
            RequestBackgroundNavigation(speed, cushionSize);
        }
        else
        {
            navi = destinationStrategy == DestinationStrategy.Explicit ? new() { Destination = ResolveTargetLocation(destinationOpt.Value), TimeToGoal = destinationOpt.Value.ExpireIn } : default;
        }
        _usedCtx = ctx;
        _bgDigest.Tick(World.CurrentTime);
        if (destinationStrategy == DestinationStrategy.Pathfind)
            LogSpeedSubstitution(navi.DiagSpeedSubstituted, navi.DiagRawSpeed);
        if (navi.Destination == null)
        {
            // 🔴🔴 這一幀我們**什麼都沒決定**（權重場在腳下是平的、目標區沒被畫上去、或根本沒有目標區），就不能繼續佔著移動擁有權。
            //    ⚠️ 這條與上面那些提早 return **不是同一類**：上面的（別的模組在移動、擊退未結算、Pyretic 將至）是「我決定這一幀不移動」，必須繼續持有擁有權；
            //    這裡是「我沒有意見」——舊 AI 有本模組看不到的目標區（跟隨主人、閃避方向偏好、pre-dodge 錨點，都是在 _rotation.Update() **之後**才加進 hints 的），
            //    讓它試比兩邊一起站著好。只要本模組算得出目的地就仍然獨佔。
            if (destinationStrategy != DestinationStrategy.None)
            {
                // 🔴🔴 交還擁有權要**遲滯**，不能一算不出來就立刻放手（見 NoDestinationHoldSeconds）。
                _noDestinationSince ??= World.CurrentTime;
                var heldFor = (float)(World.CurrentTime - _noDestinationSince.Value).TotalSeconds;
                var hold = NoDestinationHoldFor(in navi, IsSettled(in navi) ? NearestForbiddenZoneDistance() : float.MinValue, SettledZoneClearance + _aiConfig.PreferredDistance + _aiConfig.CasualSafetyMargin);
                // 門檻會隨每幀的狀況變，所以交還後要閂住到重新算得出目的地為止，否則門檻一變長就會沒算出目的地也拿回擁有權。
                if (_noDestinationYielded || heldFor >= hold)
                {
                    _ownsMovement = false;
                    _noDestinationYielded = true;
                    LogNoDestination(true, in navi, heldFor, hold);
                }
                else
                {
                    ++_noDestinationSuppressed;
                }
            }
            return; // nothing to do
        }
        // 算得出目的地 ⇒ 遲滯計時歸零。升級永遠即時，只有降級要等——與深牢座標閘門同一套不對稱。
        // ⚠️ 計數也要在這裡歸零，不能只在 LogNoDestination 裡歸零：遲滯期間就恢復的那些循環
        //    根本不會走到那一行，計數會一路累加下去。
        _noDestinationSince = null;
        _noDestinationSuppressed = 0;
        _noDestinationYielded = false;
        LogNoDestination(false, in navi, default, default);

        // 旋轉移動只能轉向：交出朝目的地的方向就好，不設 ForcedMovement（一般移動輸入在這狀態被遊戲忽略）。
        if (Hints.SpinDirection == null && AIHints.IsSpinning(World, Player))
        {
            Hints.SpinDirection = Player.DirectionTo(navi.Destination.Value).ToAngle();
            return;
        }

        // 顯示層：下面的 Range 策略可能把 Destination 換成「維持輸出距離」的位置，換掉之後
        // NextWaypoint 就不再是同一條路徑上的下一點，照畫會多出一段指向舊路徑的假線。
        // 先記下原值，稍後比對，不相等就只畫第一段。
        var showPath = _visualConfig.ShowMovementPath;
        var preRangeDestination = showPath ? navi.Destination : null;

        var rangeOpt = strategy.Option(Track.Range);
        var rangeStrategy = rangeOpt.As<RangeStrategy>();
        if (rangeStrategy != RangeStrategy.Any)
        {
            var rangeReference = ResolveTargetOverride(rangeOpt.Value) ?? primaryTarget;
            if (rangeReference != null)
            {
                // TODO: instead of hardcoding, is it possible to reuse goal zones for this purpose?
                // it would allow greeding AOE actions as well, but requires modification to NavigationDecision to avoid duplicating work
                var effectiveRange = Player.Role is Role.Tank or Role.Melee ? MeleeRange : CasterRange;
                var toDestination = navi.Destination.Value - rangeReference.Position;
                var maxRange = Player.HitboxRadius + rangeReference.HitboxRadius + effectiveRange - GreedTolerance;
                var range = toDestination.Length();
                if (range > maxRange)
                {
                    var uptimePosition = rangeReference.Position + maxRange / range * toDestination;
                    var uptimeToDestinationTime = (range - maxRange) / speed;
                    switch (rangeStrategy)
                    {
                        case RangeStrategy.MaxRange:
                            navi.Destination = uptimePosition;
                            navi.LeewaySeconds -= uptimeToDestinationTime; // assume we'll want to reach destination later, so leeway has to be reduced
                            break;
                        case RangeStrategy.GreedGCDExplicit:
                        case RangeStrategy.GreedLastMomentExplicit:
                            navi.LeewaySeconds = destinationOpt.Value.ExpireIn - uptimeToDestinationTime;
                            if (navi.LeewaySeconds > (rangeStrategy == RangeStrategy.GreedGCDExplicit ? GCD : 0))
                                navi.Destination = uptimePosition;
                            break;
                        case RangeStrategy.GreedAutomatic:
                            // 🔴 uptimePosition 是「目標身邊那個圈」上的一點，可能落在尋路視窗之外（目標很遠的近戰／坦克最容易：MeleeRange 讓 uptime 點停在目標身邊）。
                            //    Map.WorldToGrid 不夾限，GridToIndex 又只是 `y * Width + x`：出界時不是擲 IndexOutOfRangeException（Execute 中途死掉 ⇒ Hints.ForcedMovement 沒被設），
                            //    就是 x 出界、y 沒出界落到**別的列**上、靜默拿到另一格的危險度 ⇒ 一律用 InBounds 驗兩個軸。
                            //    出界代表「這一點安不安全我們不知道」⇒ **完全不調整**，navi.Destination 維持尋路算出來的目的地。
                            //    刻意**不**夾進視窗再比：那是拿另一格的危險度冒充這一格的答案，會在「uptime 點其實站不得」時把角色送過去。
                            var uptimeGrid = ctx.Map.WorldToGrid(uptimePosition);
                            var uptimeInWindow = ctx.Map.InBounds(uptimeGrid.x, uptimeGrid.y);
                            LogGreedWindow(uptimeInWindow);
                            // curCell 由 ThetaStar.Start 的 ClampToGrid 產生，本身在界內；這裡的長度檢查擋的是
                            // 「這一幀沒跑過尋路」（Destination=Explicit 時 Map 是空的、StartNodeIndex 是上次的殘值）。
                            var curCell = ctx.ThetaStar.StartNodeIndex;
                            if (navi.LeewaySeconds > 0 && uptimeInWindow && (uint)curCell < (uint)ctx.Map.PixelMaxG.Length)
                            {
                                var uptimeCell = ctx.Map.GridToIndex(uptimeGrid.x, uptimeGrid.y);
                                if (ctx.Map.PixelMaxG[uptimeCell] >= ctx.Map.PixelMaxG[curCell])
                                    navi.Destination = uptimePosition;
                                else if (Player.DistanceToHitbox(primaryTarget) <= maxRange)
                                    navi.Destination = Player.Position;
                            }
                            break;
                    }
                }
                // else: destination is already in our effective range, nothing to adjust here
            }
        }

        var dir = navi.Destination.Value - Player.Position;
        var distSq = dir.LengthSq();
        if (distSq <= 0.01f)
        {
            // we're already very close to destination
            // TODO: what should we do if forced-movement is already set to something?.. not sure who could set it, some other module?..
            Hints.ForcedMovement = default;
            return;
        }

        if (showPath)
        {
            // 這裡 navi.Destination 已經套完 Range 的調整，而且確定「要往那裡走」（已站到位的情況上面已 return）。
            // ⚠️ 只有 Pathfind 會產生有意義的 LeewaySeconds：Explicit 分支沒有設這個欄位（結構預設 0），
            // 直接拿去比會讓明明不趕時間的手動指定座標永遠顯示成急迫。
            var urgent = destinationStrategy == DestinationStrategy.Pathfind && navi.LeewaySeconds < UrgentLeewaySeconds;
            Volatile.Write(ref _pendingVisualization, new(navi.Destination.Value, navi.Destination == preRangeDestination ? navi.NextWaypoint : null, urgent));
        }

        // we want to move somewhere, check whether we're allowed to
        if (allowSpecialModes && Hints.ImminentSpecialMode.mode == AIHints.SpecialMode.Misdirection && Hints.ImminentSpecialMode.activation <= World.CurrentTime)
        {
            // special case for misdirection
            // assume it's always fine to drop casts during misdirection (add new option to the specialmode track if it's ever not the case, i guess...)
            // we have only two options really - either move to the current forced direction, or wait (and this direction will change) - so see whether moving now brings us closer to the destination
            // if our destination is not the last one (turn != 0), we can only move if it will move us *further* from second-next point - otherwise we're moving towards the wall
            // the tolerance angle can be inferred from following consideration: in the worst case our movement should keep us at the same distance to destination (or it can move us closer)
            // so let's consider isosceles triangle with legs equal to distance to target, and base equal to distance we move over a period of time - the base angle is then our threshold
            // this means that cos(threshold) = speed * dt / 2 / distance
            // assuming we wanna move at least for a second, speed is standard 6, threshold of 60 degrees would be fine for distances >= 6
            // for micro adjusts, if we move for 1 frame (1/60s), threshold of 60 degrees would be fine for distance 0.1, which is our typical threshold
            var threshold = 30f.Degrees();
            var allowMovement = World.Client.ForcedMovementDirection.AlmostEqual(Angle.FromDirection(dir), threshold.Rad);
            if (allowMovement && destinationStrategy == DestinationStrategy.Pathfind)
            {
                // if we have a map, we can try to see if current direction has long enough unobstructed path
                // TODO: maybe just check a single closest grid cell that we would intersect if we go forward?..
                allowMovement = CalculateUnobstructedPathLength(ctx, World.Client.ForcedMovementDirection) >= Math.Min(4, distSq);
            }
            Hints.ForcedMovement = allowMovement ? World.Client.ForcedMovementDirection.ToDirection().ToVec3(Player.PosRot.Y) : default;

            //var halfThreshold = Hints.MisdirectionThreshold; // even much smaller threshold seems to work fine in practice (TODO: reconsider...)
            //var idealDir = Angle.FromDirection(dir);
            //if (destinationStrategy == DestinationStrategy.Pathfind)
            //{
            //    var lenL = CalculateUnobstructedPathLength(idealDir + halfThreshold);
            //    var lenR = CalculateUnobstructedPathLength(idealDir - halfThreshold);
            //    if (lenL < 4)
            //        idealDir -= halfThreshold;
            //    if (lenR < 4)
            //        idealDir += halfThreshold;
            //}
            //var withinThreshold = World.Client.ForcedMovementDirection.AlmostEqual(idealDir, halfThreshold.Rad);
            //Hints.ForcedMovement = withinThreshold ? World.Client.ForcedMovementDirection.ToDirection().ToVec3(Player.PosRot.Y) : default;
        }
        else
        {
            // fine to move if we won't interrupt cast or only just started casting (or are explicitly allowed to)
            var allowMovement = Player.CastInfo == null || Player.CastInfo.EventHappened || Player.CastInfo.ElapsedTime <= 1.0f || castStrategy is CastStrategy.DropMove or CastStrategy.DropInstants;
            Hints.ForcedMovement = allowMovement ? dir.ToVec3(Player.PosRot.Y) : default;
        }

        var maxCastTime = castStrategy switch
        {
            CastStrategy.Leeway => navi.LeewaySeconds,
            CastStrategy.Explicit => castOpt.Value.ExpireIn,
            CastStrategy.Greedy => float.MaxValue,
            _ => 0,
        };
        Hints.MaxCastTime = Math.Max(0, Math.Min(Hints.MaxCastTime, maxCastTime));
        Hints.ForceCancelCast |= castStrategy == CastStrategy.DropMove;
        if (castStrategy is CastStrategy.Leeway && Player.CastInfo is { } castInfo)
        {
            var effectiveCastRemaining = Math.Max(0, castInfo.RemainingTime - 0.5d);
            if (Hints.MaxCastTime < effectiveCastRemaining)
            {
                Hints.ForceCancelCast = true;
                // no leeway, cast might have been initiated by user, keep moving
                Hints.ForcedMovement = dir.ToVec3(Player.PosRot.Y);
            }
        }
    }

    /// <summary>上一次記過的「這一段有沒有目的地」；用來只在<b>狀態翻轉</b>時記一行 log。</summary>
    private bool _loggedNoDestination;

    /// <summary>目前這一段「算不出目的地」是閒置（沒有人要求移動）＝兩行 log 都走 Verbose。</summary>
    private bool _idleNoDestination;

    /// <summary>從哪一刻起連續算不出目的地；null＝上一次算得出來。</summary>
    private DateTime? _noDestinationSince;

    /// <summary>遲滯期間吞掉了幾次「算不出目的地」；只用來在真的交還時報出規模。</summary>
    private int _noDestinationSuppressed;

    /// <summary>這一段連續算不出目的地裡已經交還過擁有權；重新算得出目的地才放開。</summary>
    private bool _noDestinationYielded;

    /// <summary>
    /// 「算不出目的地」要連續持續這麼久，才真的把移動擁有權交還給舊的 AI 走位（秒）。
    /// </summary>
    /// <remarks>
    /// 🔴 不能拿掉：沒有這道遲滯，「永久卡死」與「角色走走停停」只能二選一。
    /// 會抖是因為兩邊不是同一個尋路——<c>AIBehaviour.BuildNavigationDecision</c> 另外加自己的
    /// 目標區，禁區緩衝也用 <c>_config.PreferredDistance</c> 而不是
    /// <see cref="Track.ForbiddenZoneCushion"/> ⇒ 不同的權重場＝不同的目的地，每次換手方向就跳。
    /// <para>值的契約：要大於「換手雜訊」、遠小於「真的卡住」，兩者差三個數量級，不是險勝。</para>
    /// <para>⚠️ 遲滯期間仍持有擁有權且不寫方向＝角色最多站住這麼久，是刻意的取捨。</para>
    /// <para>📌 只擋降級：一算得出目的地就立刻拿回，不等任何時間（同 <c>CoordGateHoldSeconds</c>）。</para>
    /// </remarks>
    private const float NoDestinationHoldSeconds = 0.5f;

    /// <summary>已站在目標區最高分的格子上、而且附近沒有 AOE 時，交還前要等的秒數。</summary>
    /// <remarks>
    /// 這種「算不出目的地」是本模組判斷「站這裡最好」，不是「沒有意見」；0.5 秒就交還會讓目標一動就來回換手。
    /// 契約：身邊 <see cref="SettledZoneClearance"/>（加上 AI 的禁區緩衝）內只要有任何禁區，就退回 0.5 秒 ⇒ 有東西要躲時與改動前相同。
    /// </remarks>
    public const float SettledHoldSeconds = 3f;

    /// <summary>判「附近沒有 AOE」的距離（米），實際用的是它再加上 AI 設定的禁區緩衝。</summary>
    public const float SettledZoneClearance = 3f;

    /// <summary>尋路結果是不是「已站在目標區最高分、腳下完全安全」。</summary>
    public static bool IsSettled(in NavigationDecision navi)
        => navi.DiagPlayerInWindow && navi.DiagGoalsRasterized && navi.DiagPlayerMaxG == float.MaxValue
        && navi.DiagMaxPriority > 0f && navi.DiagPlayerPriority + 1e-4f >= navi.DiagMaxPriority;

    /// <summary>這一段「算不出目的地」要連續多久才交還（秒）。純函式。</summary>
    /// <param name="nearestZoneDistance">玩家到最近禁區邊緣的距離；不知道時傳 <c>float.MinValue</c>（＝當成很近）。</param>
    public static float NoDestinationHoldFor(in NavigationDecision navi, float nearestZoneDistance, float clearance)
        => IsSettled(in navi) && nearestZoneDistance >= clearance ? SettledHoldSeconds : NoDestinationHoldSeconds;

    private float NearestForbiddenZoneDistance()
    {
        var min = float.MaxValue;
        var zones = Hints.ForbiddenZones;
        for (var i = 0; i < zones.Count; ++i)
        {
            var d = zones[i].shapeDistance(Player.Position);
            if (d < min)
                min = d;
        }
        return min;
    }

    /// <summary>
    /// 把「本模組這一段算不出目的地、因此把移動擁有權交還給 AI」講出來，並且<b>一行講完為什麼</b>。
    /// </summary>
    /// <remarks>
    /// 🔑 這一行是「角色站著不動」唯一的離線證據，而不動有四種互斥成因，外觀完全相同
    /// （尋路回 <c>null</c>、不報錯、連標線都不畫）——四種的判別交給
    /// <see cref="NavigationDecision.DiagSummary"/>，那裡有本次尋路真正看到的數字。
    /// 📌 逐筆走 <c>Debug</c>，每分鐘由 <c>MovementLog</c> 彙總一行 Information（含最近一次的原因）。
    /// 🔴 只在翻轉時印。這支每幀都會被呼叫到。
    /// <para>⚠️ 例外：<see cref="NavigationDecision.DiagIdleNoGoals"/>＝沒有人要求移動，是閒置常態而不是故障，走 <c>Verbose</c>。成對的復原行要跟著同一級，否則會留下沒有起頭的孤兒行。</para>
    /// </remarks>
    private void LogNoDestination(bool stuck, in NavigationDecision navi, float heldFor, float hold)
    {
        if (stuck == _loggedNoDestination)
            return;
        _loggedNoDestination = stuck;
        if (stuck)
        {
            // 遲滯吞掉的次數要報出來：它就是「本來會發生幾次換手」的規模，也是下一輪判斷
            // NoDestinationHoldSeconds 該不該調整的唯一離線依據。
            var suppressed = _noDestinationSuppressed;
            // 「沒有人要求移動」是閒置常態,實測佔這一行的多數 ⇒ 降 Verbose、不進彙總。復原行沿用同一級以維持成對。
            _idleNoDestination = navi.DiagIdleNoGoals;
            var opened = $"[NormalMovement] 已連續 {heldFor:f1}s 算不出目的地（遲滯門檻 {hold:f1}s、" +
                $"期間吞掉 {suppressed} 幀），移動擁有權交還給 AI 自動走位：{navi.DiagSummary()}";
            if (_idleNoDestination)
            {
                Service.Logger.Verbose(opened);
            }
            else
            {
                Service.Logger.Debug(opened);
                MovementLog.Digest.Record(MovementLog.PresetGaveUp, World.CurrentTime, "最近一次自動移動交還的原因：" + opened);
            }
        }
        else
        {
            const string closed = "[NormalMovement] 重新算得出目的地，移動擁有權回到「自動移動」模組。";
            if (_idleNoDestination)
                Service.Logger.Verbose(closed);
            else
                Service.Logger.Debug(closed);
        }
    }

    /// <summary>上一次記過的「移動速度是不是被代打了」；用來只在<b>狀態翻轉</b>時記一行 log。</summary>
    private bool _loggedSpeedSubstituted;

    /// <summary>上一次真的把速度狀態寫進 log 的時刻；<c>null</c> ＝ 這場還沒寫過。</summary>
    private DateTime? _lastSpeedLogTime;

    /// <summary>冷卻期間被吞掉的<b>翻轉次數</b>；下一次真的印的時候一起報出來。</summary>
    private int _speedFlipsSuppressed;

    /// <summary>兩行速度 log 之間至少要隔這麼久。</summary>
    /// <remarks>
    /// 🔴 只靠「狀態翻轉才印」擋不住<b>每幀都在翻轉</b>的情況：欄位偏移錯的時候讀到的值在
    /// 0 與 0.45 之間逐幀交替，等於每一幀都是一次翻轉。
    /// 翻轉節流的前提是「狀態會穩定下來」，那個前提在故障時正好不成立 ⇒ 必須再加一道時間閘門。
    /// </remarks>
    private const double SpeedLogCooldownSeconds = 10d;

    /// <summary>
    /// 把「移動速度讀到不合理的值、這一段改用名目速度算路徑」講出來。
    /// </summary>
    /// <remarks>
    /// 🔑 這一行是「速度來源到底有沒有壞」唯一的離線證據，而且<b>修好之後才更需要它</b>：
    /// 夾限一旦生效，「算不出目的地」那一行就不會再出現，速度是 0 這件事會重新變成隱形的。
    /// 速度來源是 <c>WorldStateGameSync</c> 的 <c>Control.Instance() + 0x7118</c> 乘上特徵碼掃到的
    /// <c>CalculateMovementSpeedMultiplier</c>；這一行印的是<b>相乘之後的原始值</b>。<b>還在洗</b>就是「偏移假設不成立」的自證。
    /// 📌 要使用者回報的診斷（<c>Information</c>）。
    /// 🔴 這支每幀都會被呼叫到 ⇒ 翻轉閘門與時間閘門<b>兩道都要過</b>才准寫。
    /// </remarks>
    private void LogSpeedSubstitution(bool substituted, float rawSpeed)
    {
        if (substituted == _loggedSpeedSubstituted)
            return;
        // ⚠️ 狀態一律更新，不受冷卻影響：吞掉的是「寫 log」這個動作，不是狀態追蹤本身。
        //    漏更新會讓冷卻結束後的第一次比較拿舊狀態去比，把真正的翻轉再吞一次。
        _loggedSpeedSubstituted = substituted;

        var now = World.CurrentTime;
        if (_lastSpeedLogTime is { } last && (now - last).TotalSeconds < SpeedLogCooldownSeconds)
        {
            ++_speedFlipsSuppressed;
            return;
        }
        _lastSpeedLogTime = now;

        // 吞掉的次數就是「這段冷卻裡速度來源到底抖了幾次」的規模，本身是診斷資料的一部分。
        var suppressed = _speedFlipsSuppressed;
        _speedFlipsSuppressed = 0;
        var tail = suppressed > 0
            ? $"（前 {SpeedLogCooldownSeconds:f0} 秒內另有 {suppressed} 次狀態翻轉被冷卻吞掉＝速度來源正在逐幀跳動）"
            : "";

        Service.Logger.Information(substituted
            ? $"[NormalMovement] 移動速度讀到 {rawSpeed:f3}（不在合理範圍），這一段改用名目速度 {NavigationDecision.NominalPlayerSpeed:f0} 碼/秒算路徑。{tail}" +
              "沒有這道代打的話，尋路的餘裕會變成負無限大、每一格都被評成「和起點一樣不安全」，結果是算不出目的地＝角色站著不動。"
            : $"[NormalMovement] 移動速度恢復正常（{rawSpeed:f3} 碼/秒），改回用實際速度算路徑。{tail}");
    }

    /// <summary>
    /// GreedAutomatic 的 uptime 點上一次是不是落在尋路視窗內；用來只在<b>狀態翻轉</b>時記一行 log。
    /// </summary>
    private bool _greedUptimeInWindow = true;

    /// <summary>
    /// 把「貪輸出的目標點跑出尋路視窗、因此這一段不做距離調整」講出來。
    /// </summary>
    /// <remarks>
    /// 📌 要使用者回報的診斷（<c>Information</c>）：這一行正是
    /// 「自動移動不走」到底是不是這個成因的唯一離線證據。
    /// 🔴 只在翻轉時印。這支每幀都會被呼叫到，每幀印等於把 log 洗掉。
    /// </remarks>
    private void LogGreedWindow(bool inWindow)
    {
        if (inWindow == _greedUptimeInWindow)
            return;
        _greedUptimeInWindow = inWindow;
        Service.Logger.Information(inWindow
            ? "[NormalMovement] 貪輸出目標點回到尋路視窗內，恢復距離調整。"
            : "[NormalMovement] 貪輸出目標點落在尋路視窗外（目標太遠，或尋路地圖中心停在原點），這一段不做距離調整、照尋路算出來的目的地走。");
    }

    private float CalculateUnobstructedPathLength(NavigationDecision.Context ctx, Angle dir)
    {
        var start = ctx.Map.WorldToGrid(Player.Position);
        if (!ctx.Map.InBounds(start.x, start.y))
            return 0;

        var end = ctx.Map.WorldToGrid(Player.Position + 100f * dir.ToDirection());
        var startG = ctx.Map.PixelMaxG[ctx.Map.GridToIndex(start.x, start.y)];
        foreach (var p in ctx.Map.EnumeratePixelsInLine(start.x, start.y, end.x, end.y))
        {
            if (!ctx.Map.InBounds(p.x, p.y) || ctx.Map.PixelMaxG[ctx.Map.GridToIndex(p.x, p.y)] < startG)
            {
                var dest = ctx.Map.GridToWorld(p.x, p.y, 0.5f, 0.5f);
                return (dest - Player.Position).LengthSq();
            }
        }
        return float.MaxValue;
    }
}
