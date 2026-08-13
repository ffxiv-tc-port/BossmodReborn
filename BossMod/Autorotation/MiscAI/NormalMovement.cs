using BossMod.Pathfinding;

namespace BossMod.Autorotation.MiscAI;

public sealed class NormalMovement : RotationModule
{
    public enum Track { Destination, Range, Cast, SpecialModes, ForbiddenZoneCushion }
    public enum DestinationStrategy { None, Pathfind, Explicit }
    public enum RangeStrategy { Any, MaxRange, GreedGCDExplicit, GreedLastMomentExplicit, GreedAutomatic }
    public enum CastStrategy { Leeway, Explicit, Greedy, FinishMove, DropMove, FinishInstants, DropInstants }
    public enum ForbiddenZoneCushionStrategy { None, Small, Medium, Large }
    public enum SpecialModesStrategy { Automatic, Ignore }

    public const float GreedTolerance = 0.15f;

    public static NormalMovement? Instance;

    // ---- 移動擁有權（給舊的 AI 走位讓路用）----

    private bool _ownsMovement;

    /// <summary>
    /// 這一幀「自動移動」模組是不是移動的<b>唯一擁有者</b>。
    /// </summary>
    /// <remarks>
    /// 🔴 存在的理由是<b>兩套走位會跨幀交替接管，表現成角色抖動</b>：
    /// <c>Plugin.DrawUI</c> 先跑 <c>_rotation.Update()</c>（本模組寫 <c>Hints.ForcedMovement</c>），
    /// 再跑 <c>_ai.Update()</c>（<c>AIController.Update</c> 只在 <c>ForcedMovement == null</c> 時
    /// 寫<b>它自己另外算的</b>目的地）。同一幀不會雙寫，所以不是「搶」——
    /// 但本模組在詠唱擋門（<c>allowMovement == false</c> ⇒ 寫回 default ＝ null）、
    /// 已站到位、擊退未結算、Pyretic 將至等情況會讓出那一幀，
    /// 舊 AI 就用<b>不同的演算法、不同的 MoveDelay</b> 算出的目的地接手。
    /// 接管權逐幀交替＝方向逐幀跳動＝使用者看到的抖動。
    /// <para>
    /// ⚠️ 判準刻意是「<b>本模組會不會做移動決策</b>」而不是「本模組存不存在」：
    /// 使用者把 <c>Destination</c> 軌設成 <see cref="DestinationStrategy.None"/> 時，
    /// 本模組整場不碰移動，這時必須讓舊 AI 照舊接管，否則兩邊都不動。
    /// </para>
    /// <para>
    /// 📌 時序上安全：本旗標在 <see cref="Execute"/> 的<b>最前面</b>設定（早於所有提早 return），
    /// 而 <c>_rotation.Update()</c> 每幀都在 <c>_ai.Update()</c> 之前跑，所以 AI 讀到的一定是本幀的值。
    /// 模組不在啟用中的預設集裡時 <see cref="Instance"/> 會在 <see cref="Dispose"/> 被清成 null
    /// （<c>RotationModuleManager.DirtyActiveModules</c> 會 Dispose 掉舊模組），於是自動退回舊行為。
    /// </para>
    /// </remarks>
    public static bool OwnsMovement => Instance?._ownsMovement ?? false;

    // ---- 顯示層（純顯示，不參與任何移動決策）----

    // 本幀移動決策的快照，給世界疊加層畫路徑用。
    // 時序：Execute 由 Plugin.DrawUI 的 _rotation.Update() 呼叫，繪製端是同一個 DrawUI 裡稍後的
    // WindowSystem.Draw() → UIRotationWindow.PreOpenCheck()。兩者同在 UiBuilder.Draw 這一個回呼中
    // 依序執行（Plugin.cs 的 _rotation.Update() 在 Service.WindowSystem.Draw() 之前），
    // 所以這裡不需要任何執行緒同步。
    public readonly record struct MovementVisualization(WPos Destination, WPos? NextWaypoint, bool Urgent);

    private static MovementVisualization? _pendingVisualization;

    // 🔴 刻意做成「讀取後清空」而不是留著上一次的值。
    // Execute 有多條提早 return 的路徑（移動被別的模組接管、沒有目的地、已經站到位、擊退尚未結算、
    // Pyretic 將至…），而且這個模組不在啟用中的預設集裡時根本不會被呼叫。
    // 若沿用舊值，上述每一種情況都會讓上一幀的線繼續畫在早已過期的位置上。
    public static MovementVisualization? ConsumeVisualization()
    {
        var res = _pendingVisualization;
        _pendingVisualization = null;
        return res;
    }

    // LeewaySeconds 低於這個值就換成危險色。取 1 秒是對齊 NavigationDecision.ActivationTimeCushion
    // 的預設值（同樣是 1 秒）—— 那是尋路自己認定的安全緩衝，低於它代表已經在吃緩衝了。
    public const float UrgentLeewaySeconds = 1f;

    // ⚠️ 刻意用實例欄位而不是 static：static 欄位初始設在 beforefieldinit 的型別上，
    // 可能早在 RotationModuleRegistry 用反射呼叫 Definition() 掃描模組時就被觸發，
    // 而 ConfigRoot.Get 是字典索引 —— 若那時 Service.Config.Initialize() 還沒跑就會擲
    // KeyNotFoundException，整個外掛載入失敗。實例只在預設集啟用這個模組時才建立，必定在初始化之後。
    private readonly AutorotationConfig _visualConfig = Service.Config.Get<AutorotationConfig>();

    public NormalMovement(RotationModuleManager manager, Actor player) : base(manager, player)
    {
        Instance = this;
    }

    public override void Dispose()
    {
        Instance = null;
        _pendingVisualization = null;
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
        res.Define(Track.ForbiddenZoneCushion).As<ForbiddenZoneCushionStrategy>("ForbiddenZoneCushion", "Overdodge", 25)
            .AddOption(ForbiddenZoneCushionStrategy.None, "Do not use any buffer in pathfinding")
            .AddOption(ForbiddenZoneCushionStrategy.Small, "Prefer to stay 0.5y away from forbidden zones")
            .AddOption(ForbiddenZoneCushionStrategy.Medium, "Prefer to stay 1.5y away from forbidden zones")
            .AddOption(ForbiddenZoneCushionStrategy.Large, "Prefer to stay 3y away from forbidden zones");
        return res;
    }

    private readonly NavigationDecision.Context _navCtx = new();

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

            if (Hints.ImminentSpecialMode.mode == AIHints.SpecialMode.Freezing && Hints.ImminentSpecialMode.activation <= World.FutureTime(0.5f))
                Hints.WantJump = true;

            if (Hints.InteractWithTarget != null)
            {
                // strongly prefer moving towards interact target
                Hints.GoalZones.Add(p =>
                {
                    var length = (p - Hints.InteractWithTarget.Position).LengthSq();

                    // 99% of eventobjects have an interact range of 3.5y, while the rest have a range of 2.09y
                    // checking only for the shorter range here would be fine in the vast majority of cases, but it can break interact pathfinding in the case that the target object is partially covered by a forbidden zone with a radius between 2.1 and 3.5
                    // this is specifically an issue in the metal gear thancred solo duty in endwalker
                    return length <= 4.3681f ? 101f : length <= 12.25f ? 100f : 0;
                });
            }
        }

        var speed = World.Client.MoveSpeed;
        var cushionStrategy = strategy.Option(Track.ForbiddenZoneCushion).As<ForbiddenZoneCushionStrategy>();
        var cushionSize = cushionStrategy switch
        {
            ForbiddenZoneCushionStrategy.Small => 0.5f,
            ForbiddenZoneCushionStrategy.Medium => 1.5f,
            ForbiddenZoneCushionStrategy.Large => 3.0f,
            _ => 0f
        };
        var navi = destinationStrategy switch
        {
            DestinationStrategy.Pathfind => NavigationDecision.Build(_navCtx, World, Hints, Player, speed, forbiddenZoneCushion: cushionSize),
            DestinationStrategy.Explicit => new() { Destination = ResolveTargetLocation(destinationOpt.Value), TimeToGoal = destinationOpt.Value.ExpireIn },
            _ => default
        };
        if (navi.Destination == null)
            return; // nothing to do

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
                            // 🔴 uptimePosition 是「目標身邊那個圈」上的一點，完全可能落在尋路視窗（約 60y 見方）之外——
                            //    例如深牢 AutoClear 把遠處房間的怪設成 ForcedTarget，或 AIHints.Clear() 把
                            //    PathfindMapCenter 歸零而玩家離原點很遠（同一個成因已經寫在 NavigationDecision.cs 的
                            //    Build 裡，那裡是玩家自己的格子，這裡是 uptime 點——**同一個 bug 的另一半**）。
                            //    Map.WorldToGrid 不夾限，GridToIndex 又只是 `y * Width + x`：
                            //    ⚠️ 失敗有兩種形式，第二種更陰——
                            //      ① 索引掉出 PixelMaxG → IndexOutOfRangeException。這支是每幀跑的，Execute 會在
                            //         這一行中途死掉 ⇒ **Hints.ForcedMovement 永遠沒被設**，症狀是「自動移動不走，
                            //         但世界上的標線照畫」（標線是這一行之前就填好的 _pendingVisualization）。
                            //      ② PixelMaxG 是重複使用、只增不減的緩衝區（長度可以大於 Width*Height），
                            //         而且 x 出界、y 沒出界時 `y*Width+x` 會落到**別的列**上 ——
                            //         索引仍然合法 ⇒ 靜默拿到另一格的危險度，然後照它決定要不要貪輸出。
                            //    ⇒ 一律用 InBounds 驗兩個軸（光驗 index >= 0 擋不掉 ② ）。
                            //    出界代表「這一點安不安全我們不知道」，而不知道的正確處理是**完全不調整**：
                            //    navi.Destination 維持尋路算出來的目的地，也就是照原目的地走。
                            //    🔴 刻意**不**夾進視窗再比——那是拿另一格的危險度冒充這一格的答案，
                            //    會在「uptime 點其實站不得」時把角色送過去，比不貪輸出糟得多。
                            //
                            // 📌 實機三個樣本（2026-08-10 22:32:04、22:47:42，堆疊逐字相同）與觸發條件：
                            //    ① **只有近戰／坦克會爆**（使用者換成遠程職業＝零例外）。機理就在上面那行
                            //       effectiveRange：坦克/近戰是 MeleeRange 2.6y、其餘是 CasterRange 25y。
                            //       uptimePosition 是「距目標 maxRange」的那一點，所以目標一遠，
                            //       近戰的 uptime 點會停在目標身邊（離玩家＝離地圖中心很遠）＝出視窗；
                            //       遠程的則往玩家方向退了 25y，通常還在視窗內。**這不是機率問題，是職業問題。**
                            //    ② 要有目標才會走到這裡（上面 `rangeReference != null`），所以「進場正常、
                            //       開了 WrathCombo 介面才開始拋」是**取得目標的時點**，不是 UI 的因果——
                            //       自動輪替本來就每幀從 Plugin.DrawUI 跑，跟開哪個視窗無關。
                            var uptimeGrid = _navCtx.Map.WorldToGrid(uptimePosition);
                            var uptimeInWindow = _navCtx.Map.InBounds(uptimeGrid.x, uptimeGrid.y);
                            LogGreedWindow(uptimeInWindow);
                            // curCell 由 ThetaStar.Start 的 ClampToGrid 產生，本身在界內；這裡的長度檢查擋的是
                            // 「這一幀沒跑過尋路」（Destination=Explicit 時 Map 是空的、StartNodeIndex 是上次的殘值）。
                            var curCell = _navCtx.ThetaStar.StartNodeIndex;
                            if (navi.LeewaySeconds > 0 && uptimeInWindow && (uint)curCell < (uint)_navCtx.Map.PixelMaxG.Length)
                            {
                                var uptimeCell = _navCtx.Map.GridToIndex(uptimeGrid.x, uptimeGrid.y);
                                if (_navCtx.Map.PixelMaxG[uptimeCell] >= _navCtx.Map.PixelMaxG[curCell])
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
            _pendingVisualization = new(navi.Destination.Value, navi.Destination == preRangeDestination ? navi.NextWaypoint : null, urgent);
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
                allowMovement = CalculateUnobstructedPathLength(World.Client.ForcedMovementDirection) >= Math.Min(4, distSq);
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
            var effectiveCastRemaining = Math.Max(0, castInfo.RemainingTime - 0.5f);
            if (Hints.MaxCastTime < effectiveCastRemaining)
            {
                Hints.ForceCancelCast = true;
                // no leeway, cast might have been initiated by user, keep moving
                Hints.ForcedMovement = dir.ToVec3(Player.PosRot.Y);
            }
        }
    }

    /// <summary>
    /// GreedAutomatic 的 uptime 點上一次是不是落在尋路視窗內；用來只在<b>狀態翻轉</b>時記一行 log。
    /// </summary>
    private bool _greedUptimeInWindow = true;

    /// <summary>
    /// 把「貪輸出的目標點跑出尋路視窗、因此這一段不做距離調整」講出來。
    /// </summary>
    /// <remarks>
    /// 📌 走 <c>Information</c>：使用者的 LogLevel 是 2，Debug/Verbose 收不到，而這一行正是
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

    private float CalculateUnobstructedPathLength(Angle dir)
    {
        var start = _navCtx.Map.WorldToGrid(Player.Position);
        if (!_navCtx.Map.InBounds(start.x, start.y))
            return 0;

        var end = _navCtx.Map.WorldToGrid(Player.Position + 100f * dir.ToDirection());
        var startG = _navCtx.Map.PixelMaxG[_navCtx.Map.GridToIndex(start.x, start.y)];
        foreach (var p in _navCtx.Map.EnumeratePixelsInLine(start.x, start.y, end.x, end.y))
        {
            if (!_navCtx.Map.InBounds(p.x, p.y) || _navCtx.Map.PixelMaxG[_navCtx.Map.GridToIndex(p.x, p.y)] < startG)
            {
                var dest = _navCtx.Map.GridToWorld(p.x, p.y, 0.5f, 0.5f);
                return (dest - Player.Position).LengthSq();
            }
        }
        return float.MaxValue;
    }
}
