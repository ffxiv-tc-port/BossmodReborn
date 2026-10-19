using BossMod.Autorotation;
using BossMod.Pathfinding;
using System.Runtime.ExceptionServices;
using System.Threading;

namespace BossMod.AI;

public record struct Targeting(AIHints.Enemy Target, float PreferredRange = 2.6f, Positional PreferredPosition = Positional.Any, bool PreferTanking = false);

// constantly follow master
sealed class AIBehaviour(AIController ctrl, RotationModuleManager autorot, Preset? aiPreset) : IDisposable
{
    public WorldState WorldState => autorot.Bossmods.WorldState;
    public float ForceMovementIn = float.MaxValue; // TODO: reconsider
    public Preset? AIPreset = aiPreset;
    private static readonly AIConfig _config = Service.Config.Get<AIConfig>();
    private readonly NavigationDecision.Context _naviCtx = new() { LogTag = "AI" };
    private NavigationDecision _naviDecision;
    // 背景尋路的全部輸入：主執行緒在丟到背景（_naviJob）之前取好，背景只讀這幾個欄位、不碰 hints／WorldState／Actor（主執行緒下一幀會 Clear hints、改 Actor）。
    // 同一時間只有一個 Execute 在跑（_semaphore），背景那次讀完之前不會被換掉；兩個區域陣列長度＝筆數，長度沒變就就地覆寫
    private (Func<WPos, float> shapeDistance, DateTime activation)[] _naviFutureZones = [];
    private (Func<WPos, float>, DateTime, ulong)[] _naviForbidden = [];
    private Func<WPos, float>[] _naviGoals = [];
    private AIHints.PathfindMapInputs _naviMap;
    private DateTime _naviNow;
    private WPos _naviPosition;
    private bool _naviCasting, _naviAvoidFutureAOEs;
    private float _naviCushion, _naviSpeed, _naviActivationCushion;
    private NavigationJob? _naviJob;
    private readonly GoalMemo _fallbackGoal = new(); // 只在主執行緒（Execute 前半段）用
    // Execute 後半段要用的前半段區域變數與遊戲狀態：丟到背景之前寫好、後半段讀，規則同上
    private Actor _exPlayer = null!, _exMaster = null!;
    private Targeting _exTarget;
    private bool _exGazeImminent, _exPyreticImminent, _exMisdirectionMode, _exForbidTargeting, _exHadNavi, _exLaunched;
    private TailState _ex;

    /// <summary>後半段讀的遊戲狀態，前半段在主執行緒一次抄好（後半段通常在執行緒池上跑）。</summary>
    private struct TailState
    {
        public DateTime Now;
        public WPos PlayerPosition, MasterPosition, FollowPosition;
        public float MasterY;
        public float? PlayerCastRemaining;
        public bool PlayerInCombat;
        public Angle ForcedMovementDirection, MisdirectionThreshold;
        public DeepDungeonState.DungeonType DungeonId;
        public ArenaBounds Bounds;
        public WPos Center;
    }

    /// <summary>尋路用的 Context（背景執行緒寫；外部只准讀它的 <c>TryGetLookaheadDeadline</c>／<c>TryGetPathDeadline</c>）。</summary>
    public NavigationDecision.Context NavigationContext => _naviCtx;
    private bool _afkMode;
    private bool _followMaster; // if true, our navigation target is master rather than primary target - this happens e.g. in outdoor or in dungeons during gathering trash
    private WPos _masterPrevPos;
    private WPos _masterMovementStart;
    private DateTime _masterLastMoved;
    private DateTime _navStartTime; // if current time is < this, navigation won't start
    private WPos? _preDodgeAnchor; // position to try returning to once a forced dodge is over, if ReturnToPreDodgePosition is enabled
    private bool _wasForcedDodging;
    private DateTime _preDodgeAnchorExpiry;
    private static readonly SemaphoreSlim _semaphore = new(1, 1);
    private static readonly Random random = new();
    private bool cancel; // used to cancel autorotation AI preset during async

    #region 深牢專用 preset 的執行期覆蓋

    private static readonly Global.DeepDungeon.AutoDDConfig _ddConfig = Service.Config.Get<Global.DeepDungeon.AutoDDConfig>();

    private Preset? _ddPreset;
    private string? _ddPresetResolvedFor;
    private static bool _ddPresetMissingLogged; // static：換角會重建 AIBehaviour，實例旗標擋不住同一句重播

    /// <summary>
    /// 深牢裡要改用的 preset；null＝沒設定，或設定的名字在 preset 庫裡找不到（照常用原本的）。
    /// </summary>
    /// <remarks>
    /// 🔴 這是<b>執行期覆蓋</b>，刻意不去寫 <c>AIConfig.AIAutorotPresetName</c>：
    /// 寫設定會製造設定檔 churn，而且遊戲或外掛在深牢裡崩潰時會把使用者永久卡在切換後的狀態。
    /// 覆蓋只活在記憶體裡，離開深牢或重載外掛就自然消失。
    /// <para>
    /// ⚠️ <b>必須快取</b>：<c>PresetDatabase.AllPresets</c> 是屬性，每次存取都會
    /// <c>[.. DefaultPresets, .. UserPresets]</c> 配一個新的 List，而這裡是每幀都會走到的路徑。
    /// </para>
    /// <para>
    /// ⚠️ 快取只在<b>名字改變</b>時失效。preset 物件是不可變的（資料庫註解明講
    /// "presets in the database are immutable"，編輯會產生新物件），所以「改了 preset 內容但沒改名」
    /// 時這裡仍會拿著舊物件，直到名字變動或重新載入。為了不在每幀配置記憶體，這個取捨是刻意的。
    /// </para>
    /// </remarks>
    private Preset? ResolveDeepDungeonPreset()
    {
        var name = _ddConfig.DeepDungeonPreset;
        if (string.IsNullOrEmpty(name))
            return null;

        if (_ddPresetResolvedFor == name)
            return _ddPreset;

        _ddPresetResolvedFor = name;
        _ddPreset = null;

        var presets = autorot.Database.Presets.AllPresets;
        var count = presets.Count;
        for (var i = 0; i < count; ++i)
        {
            if (string.Equals(presets[i].Name, name, PresetDatabase.NameComparison))
            {
                _ddPreset = presets[i];
                _ddPresetMissingLogged = false;
                Service.Logger.Information($"[DD] 深牢期間改用循環預設「{name}」。");
                return _ddPreset;
            }
        }

        // 找不到＝設定裡的名字被改名或刪掉了。不是錯誤，維持原本的 preset。
        if (!_ddPresetMissingLogged)
        {
            _ddPresetMissingLogged = true;
            Service.Logger.Information($"[DD] 設定指定的深牢循環預設「{name}」不存在，維持原本的預設、不切換。");
        }
        return null;
    }

    #endregion

    public void Dispose()
    {
        cancel = true;
    }

    /// <summary>每幀由 <c>AIManager.Update</c> 在主執行緒呼叫，呼叫端不等回傳的 Task。</summary>
    /// <remarks>
    /// 尋路丟到背景時，後半段在背景做完尋路的那條執行緒上接著跑、跑完才放號誌（背景先做完就在主執行緒當場接著跑），
    /// 與 <c>await Task.Run(...)</c> 的接法相同，只是不配置 Task 與狀態機。任何一段擲的例外都照舊變成沒人觀察的已失敗 Task。
    /// </remarks>
    public Task Execute(Actor player, Actor master)
    {
        try
        {
            // 自動移動本幀記下的尋路請求在這裡發到背景、下一幀沿用；放在等號誌之前：上一次 AI 還沒跑完也照樣發
            Autorotation.MiscAI.NormalMovement.Instance?.LaunchBackgroundNavigation();
            if (!_semaphore.Wait(0))
                return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            return Task.FromException(ex);
        }
        try
        {
            var step = ExecuteHead(player, master, out var naviDecision);
            if (step == HeadStep.Background)
                return Task.CompletedTask; // 後半段與放號誌交給背景那一步
            if (step != HeadStep.Done)
                ExecuteTail(step == HeadStep.Navigated, naviDecision);
        }
        catch (Exception ex)
        {
            _semaphore.Release();
            return Task.FromException(ex);
        }
        _semaphore.Release();
        return Task.CompletedTask;
    }

    private enum HeadStep { Done, NoNavigation, Navigated, Background }

    // 主執行緒那一段（尋路之前）
    private HeadStep ExecuteHead(Actor player, Actor master, out NavigationDecision naviDecision)
    {
        naviDecision = default;
        ForceMovementIn = float.MaxValue;
        if (player.IsDead)
            return HeadStep.Done;

        // keep master in focus
        if (_config.FocusTargetMaster)
            FocusMaster(master);

        _afkMode = _config.AutoAFK && !master.InCombat && (WorldState.CurrentTime - _masterLastMoved).TotalSeconds > _config.AFKModeTimer;
        var gazeImminent = autorot.Hints.ForbiddenDirections.Count != 0 && autorot.Hints.ForbiddenDirections[0].activation <= WorldState.FutureTime(0.5d);
        var pyreticImminent = autorot.Hints.ImminentSpecialMode.mode == AIHints.SpecialMode.Pyretic && autorot.Hints.ImminentSpecialMode.activation <= WorldState.FutureTime(1d);
        var misdirectionMode = autorot.Hints.ImminentSpecialMode.mode == AIHints.SpecialMode.Misdirection && autorot.Hints.ImminentSpecialMode.activation <= WorldState.CurrentTime;
        var forbidTargeting = _config.ForbidActions || _afkMode || gazeImminent || pyreticImminent;
        var hadNavi = _naviDecision.Destination != null;

        // 寫 hints 只能在這段主執行緒上（後半段可能在背景才跑完）：用上一次尋路的目的地，最多晚一幀。
        if (_naviDecision.Destination is { } spinDest && autorot.Hints.SpinDirection == null && AIHints.IsSpinning(WorldState, player))
            autorot.Hints.SpinDirection = player.DirectionTo(spinDest).ToAngle();

        Targeting target = default;
        if (!forbidTargeting && AIPreset != null && (!_config.ForbidAIMovementMounted || _config.ForbidAIMovementMounted && player.MountId == 0))
        {
            target = SelectPrimaryTarget(player, master);
            if (_config.ManualTarget)
            {
                var t = autorot.WorldState.Actors.Find(player.TargetID);
                if (t != null)
                    target.Target = new AIHints.Enemy(t, 100, false);
                else
                    target = default;
            }
            if (target.Target != null || TargetIsForbidden(player.TargetID))
                autorot.Hints.ForcedTarget ??= target.Target?.Actor;
            AdjustTargetPositional(player, ref target);
        }

        var followTarget = _config.FollowTarget;
        _followMaster = master != player;

        _exPlayer = player;
        _exMaster = master;
        _exTarget = target;
        _exGazeImminent = gazeImminent;
        _exPyreticImminent = pyreticImminent;
        _exMisdirectionMode = misdirectionMode;
        _exForbidTargeting = forbidTargeting;
        _exHadNavi = hadNavi;
        _exLaunched = false;
        var followActor = target.Target?.Actor ?? master;
        _ex = new()
        {
            Now = WorldState.CurrentTime,
            PlayerPosition = player.Position,
            MasterPosition = master.Position,
            FollowPosition = followActor.Position,
            MasterY = master.PosRot.Y,
            PlayerCastRemaining = player.CastInfo?.RemainingTime,
            PlayerInCombat = player.InCombat,
            ForcedMovementDirection = WorldState.Client.ForcedMovementDirection,
            MisdirectionThreshold = autorot.Hints.MisdirectionThreshold,
            DungeonId = autorot.WorldState.DeepDungeon.DungeonId,
            Bounds = autorot.Hints.PathfindMapBounds,
            Center = autorot.Hints.PathfindMapCenter,
        };

        // note: if there are pending knockbacks, don't update navigation decision to avoid fucking up positioning
        if (player.PendingKnockbacks.Count != 0)
            return HeadStep.NoNavigation;
        var actorTarget = autorot.WorldState.Actors.Find(player.TargetID);
        var decided = followTarget && actorTarget != null
            ? BuildNavigationDecision(player, actorTarget, target, out naviDecision)
            : BuildNavigationDecision(player, master, target, out naviDecision);
        if (decided)
            return HeadStep.Navigated;
        return _naviJob!.HandOff(out naviDecision) ? HeadStep.Background : HeadStep.Navigated;
    }

    // 尋路之後那一段：在主執行緒（沒丟背景、或背景先做完）或背景做完尋路的那條執行緒上跑
    private void ExecuteTail(bool navigated, NavigationDecision naviDecision)
    {
        var player = _exPlayer;
        var master = _exMaster;
        var target = _exTarget;
        var gazeImminent = _exGazeImminent;
        var pyreticImminent = _exPyreticImminent;
        var forbidTargeting = _exForbidTargeting;
        if (navigated)
        {
            _naviDecision = naviDecision;

            // there is a difference between having a small positive leeway and having a negative one for pathfinding, prefer to keep positive
            _naviDecision.LeewaySeconds = Math.Max(0, _naviDecision.LeewaySeconds - 0.1f);
        }

        var masterIsMoving = TrackMasterMovement();
        var moveWithMaster = masterIsMoving && _followMaster && master != player;
        ForceMovementIn = moveWithMaster || gazeImminent || pyreticImminent ? 0f : _naviDecision.LeewaySeconds;

        TrackPreDodgeAnchor(moveWithMaster || gazeImminent || pyreticImminent);

        if (_config.MoveDelay != 0d && !_exHadNavi && _naviDecision.Destination != null)
            _navStartTime = _ex.Now.AddSeconds(_config.MoveDelay);

        // 📌 這整段是否執行本身就是一個診斷缺口：它不執行時 autorot.Preset 完全不被碰，
        //    使用者手動掛上的預設會原封不動留著，於是「有沒有目標」那個判斷根本不在路徑上。
        //    LogPresetGate 沒印時分不出是被誰擋的，所以這裡把擋住它的條件記下來。
        var presetGateRuns = !forbidTargeting && !cancel;
        LogPresetGateReachable(presetGateRuns, forbidTargeting, cancel);
        if (presetGateRuns)
        {
            // 🔑 只掛預設，不影響走位/閃避/選目標——那些在這個判斷之外。
            //    深牢判定用 DeepDungeon.DungeonId：它直接來自遊戲的深牢 instance content director
            //    （不在深牢時是 None），不需要另外維護一份 territory 清單。
            var inDeepDungeon = _ex.DungeonId != DeepDungeonState.DungeonType.None;
            var autorotAllowed = !_config.AutorotOnlyInDeepDungeon || inDeepDungeon;
            // 深牢裡改用指定的 preset；沒設定或找不到就退回原本的（不是錯誤）。
            // 📌 與「只在深牢出招」的互動：深牢外 autorotAllowed 為 false ⇒ Preset 直接是 null，
            //    這裡選了哪個 preset 都不影響，兩個開關同時開啟時語意是一致的。
            var preset = inDeepDungeon ? ResolveDeepDungeonPreset() ?? AIPreset : AIPreset;
            var hasTarget = target.Target != null;
            LogPresetGate(hasTarget && autorotAllowed, hasTarget, autorotAllowed);
            autorot.Preset = hasTarget && autorotAllowed ? preset : null;
        }
        UpdateMovement(player, master, target, gazeImminent || pyreticImminent, _exMisdirectionMode ? _ex.MisdirectionThreshold : default, !forbidTargeting ? autorot.Hints.ActionsToExecute : null);
    }

    // 背景做完尋路後由 NavigationJob 在同一條執行緒上呼叫；error＝尋路擲的例外（後半段就不跑）
    private void ResumeOnPool(NavigationDecision naviDecision, Exception? error)
    {
        try
        {
            if (error == null)
                ExecuteTail(true, naviDecision);
        }
        catch (Exception ex)
        {
            error = ex;
        }
        _semaphore.Release();
        if (error != null)
            _ = Task.FromException(error);
    }

    /// <summary>背景尋路＋其後的 Execute 後半段。每個 AIBehaviour 一個、重用；號誌保證同一時間最多一個在跑。</summary>
    /// <remarks>
    /// <see cref="Start"/> 之後主執行緒馬上 <see cref="HandOff"/>：搶到就由背景做完尋路接著跑後半段；
    /// 搶不到＝背景已經做完，主執行緒當場拿結果接著跑。兩種接法都與 <c>await</c> 一個 <c>Task.Run</c> 相同。
    /// </remarks>
    private sealed class NavigationJob(AIBehaviour owner) : IThreadPoolWorkItem
    {
        private const int Running = 0, HandedOff = 1, Completed = 2;
        private int _state;
        private NavigationDecision _result;
        private Exception? _error;

        public void Start()
        {
            _result = default;
            _error = null;
            Volatile.Write(ref _state, Running);
            ThreadPool.UnsafeQueueUserWorkItem(this, preferLocal: false);
        }

        /// <summary>主執行緒：true＝後半段交給背景；false＝背景已做完，結果在 <paramref name="result"/>（尋路擲的例外原樣重擲）。</summary>
        public bool HandOff(out NavigationDecision result)
        {
            result = default;
            if (Interlocked.CompareExchange(ref _state, HandedOff, Running) == Running)
                return true;
            if (_error != null)
                ExceptionDispatchInfo.Throw(_error);
            result = _result;
            return false;
        }

        public void Execute()
        {
            try
            {
                _result = owner.BuildNavigationOnPool();
            }
            catch (Exception ex)
            {
                _error = ex;
            }
            if (Interlocked.CompareExchange(ref _state, Completed, Running) == Running)
                return;
            owner.ResumeOnPool(_result, _error);
        }
    }

    // returns null if we're to be idle, otherwise target to attack
    private Targeting SelectPrimaryTarget(Actor player, Actor master)
    {
        // we prefer not to switch targets unnecessarily, so start with current target - it could've been selected manually or by AI on previous frames
        // if current target is not among valid targets, clear it - this opens way for future target selection heuristics
        var targetId = autorot.Hints.ForcedTarget?.InstanceID ?? player.TargetID;
        // 候選與 AIHints.PriorityTargets 是同一批（PotentialTargets 開頭連續、優先度等於最高值的那一段）；每幀都走，直接掃、不另建清單與委派
        var candidates = autorot.Hints.PotentialTargets;
        var numCandidates = 0;
        while (numCandidates < candidates.Count && candidates[numCandidates].Priority == autorot.Hints.HighestPotentialTargetPriority)
            ++numCandidates;

        AIHints.Enemy? target = null;
        for (var i = 0; i < numCandidates; ++i)
        {
            if (candidates[i].Actor.InstanceID == targetId)
            {
                target = candidates[i];
                break;
            }
        }

        // if we don't have a valid target yet, use some heuristics to select some 'ok' target to attack
        // try assisting master, otherwise (if player is own master, or if master has no valid target) just select closest valid target
        if (target == null && master != player)
        {
            var masterTargetId = master.TargetID;
            for (var i = 0; i < numCandidates; ++i)
            {
                if (masterTargetId == candidates[i].Actor.InstanceID)
                {
                    target = candidates[i];
                    break;
                }
            }
        }
        if (target == null && numCandidates > 0)
        {
            // 最近的一個；距離相同時取排在前面的（比較方式與 MinBy 相同）
            target = candidates[0];
            var bestDistSq = (target.Actor.Position - player.Position).LengthSq();
            for (var i = 1; i < numCandidates; ++i)
            {
                var distSq = (candidates[i].Actor.Position - player.Position).LengthSq();
                if (distSq.CompareTo(bestDistSq) < 0)
                {
                    bestDistSq = distSq;
                    target = candidates[i];
                }
            }
        }

        // if the previous line returned no target, there aren't any priority targets at all - give up
        if (target == null)
            return default;

        // TODO: rethink all this... ai module should set forced target if it wants to switch... figure out positioning and stuff
        // now give class module a chance to improve targeting
        // typically it would switch targets for multidotting, or to hit more targets with AOE
        // in case of ties, it should prefer to return original target - this would prevent useless switches
        var targeting = new Targeting(target!, player.Role is Role.Melee or Role.Tank ? 2.6f : 24.5f);

        var pos = autorot.Hints.RecommendedPositional;
        if (pos.Target != null && targeting.Target.Actor == pos.Target)
            targeting.PreferredPosition = pos.Pos;

        return /*autorot.SelectTargetForAI(targeting) ??*/ targeting;
    }

    private void AdjustTargetPositional(Actor player, ref Targeting targeting)
    {
        if (targeting.Target == null || targeting.PreferredPosition == Positional.Any)
            return; // nothing to adjust

        if (targeting.PreferredPosition == Positional.Front)
        {
            // 'front' is tank-specific positional; no point doing anything if we're not tanking target
            if (targeting.Target.Actor.TargetID != player.InstanceID)
                targeting.PreferredPosition = Positional.Any;
            return;
        }

        // if target-of-target is player, don't try flanking, it's probably impossible... - unless target is currently casting (TODO: reconsider?)
        // skip if targeting a dummy, they don't rotate
        if (targeting.Target.Actor.TargetID == player.InstanceID && targeting.Target.Actor.CastInfo == null && targeting.Target.Actor.NameID != 541)
            targeting.PreferredPosition = Positional.Any;
    }

    // remembers the position player was standing at right before a forced dodge, so we can try to walk back to it once the dodge is over
    private void TrackPreDodgeAnchor(bool forcedByExternalFactor)
    {
        if (!_config.ReturnToPreDodgePosition)
        {
            _preDodgeAnchor = null;
            _wasForcedDodging = false;
            return;
        }

        var playerPos = _ex.PlayerPosition;
        var forcedDodging = !forcedByExternalFactor && ForceMovementIn <= 0f && _naviDecision.Destination != null;
        if (forcedDodging && !_wasForcedDodging)
            _preDodgeAnchor = playerPos; // just started dodging - remember where we came from
        _wasForcedDodging = forcedDodging;

        if (_preDodgeAnchor == null)
            return;

        if (!forcedDodging && (playerPos - _preDodgeAnchor.Value).LengthSq() < 1f)
            _preDodgeAnchor = null; // arrived back, done
        else if (!forcedDodging && !IsAnchorSafe(_preDodgeAnchor.Value))
            _preDodgeAnchor = null; // spot became dangerous or is out of bounds - give up
        else if (_preDodgeAnchor != null && _ex.Now > _preDodgeAnchorExpiry)
            _preDodgeAnchor = null; // took too long, give up and let normal positioning take over
        if (forcedDodging)
            _preDodgeAnchorExpiry = _ex.Now.AddSeconds(_config.ReturnToPreDodgePositionTimeout);
    }

    // true for Gold Saucer minigames, where standing further into a safe zone than strictly necessary costs nothing meaningful (as opposed to dungeons/trials/raids, where positioning still needs to stay precise)
    private bool IsCasualContent() => autorot.Bossmods.ActiveModule?.Info?.GroupType == BossModuleInfo.GroupType.GoldSaucer;

    // true if pos is inside the arena bounds and not inside any (even future) forbidden zone
    // 後半段可能在執行緒池上：這一次有丟背景尋路時讀交給它的那份危險區快照（同一幀、內容相同），沒丟時後半段一定在主執行緒、讀 hints 本身
    private bool IsAnchorSafe(WPos pos)
    {
        if (!_ex.Bounds.Contains(pos - _ex.Center))
            return false;
        if (_exLaunched)
        {
            foreach (var z in _naviForbidden)
                if (z.Item1(pos) < 0f)
                    return false;
            return true;
        }
        foreach (var z in autorot.Hints.ForbiddenZones)
            if (z.shapeDistance(pos) < 0f)
                return false;
        return true;
    }

    // true＝當場決定、不必尋路（decision 就是結果）；false＝尋路已丟到背景（_naviJob），由呼叫端接著 HandOff
    private bool BuildNavigationDecision(Actor player, Actor master, Targeting targeting, out NavigationDecision decision)
    {
        decision = default;
        if (_config.ForbidMovement || _config.ForbidAIMovementMounted && player.MountId != 0
            || autorot.Hints.ImminentSpecialMode.mode == AIHints.SpecialMode.NoMovement && autorot.Hints.ImminentSpecialMode.activation <= WorldState.FutureTime(1d))
        {
            decision = new() { LeewaySeconds = float.MaxValue };
            return true;
        }

        // 下面那行的「玩家座標 × 亂數」看起來像筆誤，與上游同形，主體刻意不改：
        // 這個欄位的語意是**方向**不是座標（AIHints.ForcedMovement 的註解：「character will move in specified direction」），
        // MovementOverride.DirectionToDestination 只取 Angle.FromDirection(X, Z)、長度整個丟掉 ⇒ 實際效果是「每幀換一個亂數方向」，
        // 冰結需要的「持續移動」有被滿足。唯一真正會壞的退化情形（玩家站在世界原點 ⇒ 零向量）在下方單獨擋下。
        if (autorot.Hints.ImminentSpecialMode.mode == AIHints.SpecialMode.Freezing && autorot.Hints.ImminentSpecialMode.activation <= WorldState.FutureTime(2.1d))
        {
            var randomO1 = random.NextSingle() * 2f - 1f;
            var randomO2 = random.NextSingle() * 2f - 1f;
            var dir = new WPos(player.Position.X * randomO1, player.Position.Z * randomO2).ToVec3();
            // 退化防護：玩家剛好站在世界原點（X==0 且 Z==0）時上面那個乘法會得到零向量，
            // 而 MovementOverride.DirectionToDestination 對 `DesiredDirection.Value == default` 是 `return null`＝AI 完全不動；
            // AIController.Update 又只在 `ForcedMovement == null` 時才接手，所以寫進去的零向量會把平常的自動移動也一起壓掉。
            // 退回純亂數方向與原意（每幀換一個亂數方向）一致。刻意只擋「剛好等於 default」這一個情形，其餘與上游逐位元相同。
            if (dir == default)
                dir = new WDir(randomO1, randomO2) is var d && d != default ? d.ToVec3() : new WDir(1f, default).ToVec3();
            autorot.Hints.ForcedMovement = dir;
            decision = new() { LeewaySeconds = float.MaxValue };
            return true;
        }

        Actor? forceDestination = null;
        var interactTarget = autorot.Hints.InteractWithTarget;
        if (interactTarget != null)
            forceDestination = interactTarget;
        else if (_followMaster)
        {
            forceDestination = master;
        }

        // 🔴 `master != player` 這個條件不能漏（初次判定 `_followMaster = master != player` 也有它）：少了它，solo 時（master 就是自己）
        //    只要在戰鬥中移動超過 10y 就會把 _followMaster 重新設成 true，下面的分支拿「master」當跟隨目標，
        //    等於在自己腳下種一個權重 1.0 的目標點（GoalSingleTarget(master, ...)），
        //    壓過所有小權重的走位提示（例如風箏的 0.05），表現成「AI 一直想站回原地」而不報錯。
        _followMaster = master != player && interactTarget == null && (_config.FollowDuringCombat || !master.InCombat || (_masterPrevPos - _masterMovementStart).LengthSq() > 100f) && (_config.FollowDuringActiveBossModule || autorot.Bossmods.ActiveModule?.StateMachine.ActiveState == null) && (_config.FollowOutOfCombat || master.InCombat);

        var forbiddenZoneCushion = _config.PreferredDistance + (IsCasualContent() ? _config.CasualSafetyMargin : 0f);

        // while actually dodging something, prefer moving behind the target over its front/flanks, and avoid simply backing away from it;
        // this is only a tie-breaker among otherwise equally-safe cells, so it never overrides actual AOE safety
        // 🔑 有模組正在風箏時不要加「別後退」的懲罰：那一項每遠離 1y 約 −0.5，而風箏的目標區只有 0.05，
        //    兩者放在同一個權重場裡風箏必然被靜默碾平（使用者只看到「開了風箏但角色不退」）。
        //    只拿掉這個偏好項，「躲到目標背後」與所有實際閃避判定都不受影響。
        if (autorot.Hints.ForbiddenZones.Count != 0 && targeting.Target != null)
            autorot.Hints.GoalZones.Add(autorot.Hints.GoalDodgeDirection(targeting.Target.Actor, player.Position, penalizeRetreat: !autorot.Hints.WantKiting));

        if (_followMaster)
        {
            if (forceDestination != null && forceDestination.OID != master.OID && autorot.Hints.PathfindMapBounds.Contains(forceDestination.Position - autorot.Hints.PathfindMapCenter))
            {
                autorot.Hints.GoalZones.Add(autorot.Hints.GoalProximity(forceDestination, 3.5f, 100f));
            }
            var target = autorot.WorldState.Actors.Find(player.TargetID);
            var masterInBounds = !_config.StayWithinArenaBounds || autorot.Hints.PathfindMapBounds.Contains(master.Position - autorot.Hints.PathfindMapCenter);
            if (!_config.FollowTarget || _config.FollowTarget && target == null)
            {
                if (masterInBounds)
                    autorot.Hints.GoalZones.Add(autorot.Hints.GoalSingleTarget(master, Positional.Any, _config.FollowTarget && player.InCombat ? _config.MaxDistanceToTarget : _config.MaxDistanceToSlot));
            }
            else if (_config.FollowTarget && target != null && AIPreset == null)
            {
                var positional = _config.DesiredPositional;
                var mindist = _config.MinDistance;
                var maxdist = _config.MaxDistanceToTarget;
                if (positional is Positional.Rear or Positional.Flank && (target.CastInfo == null && target.NameID != 541u && target.TargetID == player.InstanceID || target.Omnidirectional)) // if player is target, rear/flank is usually impossible unless target is casting
                    positional = Positional.Any;
                if (masterInBounds)
                    autorot.Hints.GoalZones.Add(autorot.Hints.GoalSingleTarget(master, positional, positional != Positional.Any ? 2.6f : maxdist));

                if (mindist != default && target.InstanceID != player.InstanceID && interactTarget == null)
                {
                    var hitboxradius = target.HitboxRadius;
                    var maxAdj = hitboxradius + maxdist;
                    var min = hitboxradius + mindist;
                    var max = maxAdj > min ? maxAdj : min + 1f;
                    autorot.Hints.GoalZones.Add(autorot.Hints.GoalDonut(target.Position, min, max, 2f));
                }
            }
            if (_preDodgeAnchor != null && !_wasForcedDodging)
                autorot.Hints.GoalZones.Add(autorot.Hints.GoalProximity(_preDodgeAnchor.Value, 3f, 1.5f));
            LaunchNavigation(player, forbiddenZoneCushion);
            return false;
        }

        // TODO: remove this once all rotation modules are fixed
        if (autorot.Hints.GoalZones.Count == 0 && targeting.Target != null)
            autorot.Hints.GoalZones.Add(_fallbackGoal.SingleTarget(autorot.Hints, targeting.Target.Actor, targeting.PreferredPosition, targeting.PreferredRange));
        if (_preDodgeAnchor != null && !_wasForcedDodging)
            autorot.Hints.GoalZones.Add(autorot.Hints.GoalProximity(_preDodgeAnchor.Value, 3f, 1.5f));
        LaunchNavigation(player, forbiddenZoneCushion);
        return false;
    }

    // 主執行緒：把 NavigationDecision.Build 會讀的輸入全部抄進 _navi* 欄位，再丟到背景
    private void LaunchNavigation(Actor player, float forbiddenZoneCushion)
    {
        var hints = autorot.Hints;
        // 診斷用：記下丟進尋路的那一刻有幾個目標區（丟到背景之後主執行緒就會把 hints 清掉了）
        _naviGoalZoneCount = hints.GoalZones.Count;
        _naviFutureZones = hints.SnapshotFutureZones();
        var forbidden = hints.ForbiddenZones;
        if (_naviForbidden.Length != forbidden.Count)
            _naviForbidden = forbidden.Count == 0 ? [] : new (Func<WPos, float>, DateTime, ulong)[forbidden.Count];
        forbidden.CopyTo(_naviForbidden);
        var goals = hints.GoalZones;
        if (_naviGoals.Length != goals.Count)
            _naviGoals = goals.Count == 0 ? [] : new Func<WPos, float>[goals.Count];
        goals.CopyTo(_naviGoals);
        _naviMap = hints.CapturePathfindMap();
        _naviNow = WorldState.CurrentTime;
        _naviPosition = player.Position;
        _naviCasting = player.CastInfo != null;
        _naviSpeed = autorot.Bossmods.WorldState.Client.MoveSpeed;
        _naviCushion = forbiddenZoneCushion;
        _naviAvoidFutureAOEs = _config.AvoidFutureAOEs;
        _naviActivationCushion = _config.ActivationTimeCushion;
        _exLaunched = true;
        (_naviJob ??= new(this)).Start();
    }

    // 在執行緒池上跑；只讀 LaunchNavigation 抄好的欄位（與 NavigationDecision.Build 同一套步驟）
    private NavigationDecision BuildNavigationOnPool()
    {
        AIHints.InitPathfindMap(_naviCtx.Map, in _naviMap);
        return NavigationDecision.BuildFromSnapshot(_naviCtx, _naviNow, _naviPosition, _naviCasting, _naviForbidden, _naviGoals, _naviFutureZones, null,
            _naviSpeed, _naviCushion, _naviAvoidFutureAOEs, _naviActivationCushion);
    }

    private void FocusMaster(Actor master)
    {
        var masterChanged = Service.TargetManager.FocusTarget?.EntityId != master.InstanceID;
        if (masterChanged)
        {
            ctrl.SetFocusTarget(master);
            _masterPrevPos = _masterMovementStart = master.Position;
            _masterLastMoved = WorldState.CurrentTime.AddSeconds(-1d);
        }
    }

    private bool TrackMasterMovement()
    {
        // keep track of master movement
        // idea is that if master is moving forward (e.g. running in outdoor or pulling trashpacks in dungeon), we want to closely follow and not stop to cast
        var masterIsMoving = true;
        if (_ex.MasterPosition != _masterPrevPos)
        {
            _masterLastMoved = _ex.Now;
            _masterPrevPos = _ex.MasterPosition;
        }
        else if ((_ex.Now - _masterLastMoved).TotalSeconds > 0.5d)
        {
            // master has stopped, consider previous movement finished
            _masterMovementStart = _masterPrevPos;
            masterIsMoving = false;
        }
        // else: don't consider master to have stopped moving unless he's standing still for some small time

        return masterIsMoving;
    }

    #region 移動為什麼沒發生 —— 三個狀態轉換診斷

    // 📌 只在狀態翻轉時記一行（這些判斷每幀都會走到）；預設集掛載兩支走 Information，擁有權換手與「算不出目的地」逐筆走 Debug、每分鐘由 MovementLog 彙總一行 Information。
    //    旗標一律 static：換角會重建 AIBehaviour，實例旗標會讓同一句重播。
    // 🔑 三行合起來把「角色不動」拆成互斥的三種原因：① 預設集根本沒掛上（沒有主要目標）⇒ 自動移動模組整段不執行
    //    ② 預設集掛上了、AI 也讓位了，但自動移動模組沒寫出方向　③ 沒人讓位，AI 自己有目標區卻算不出目的地（尋路回 null）

    private static bool? _loggedPresetGate;

    /// <summary>
    /// 把「自動循環預設被掛上／卸下」講出來。這是<b>趕路時什麼都沒發生</b>的頭號嫌疑。
    /// </summary>
    /// <remarks>
    /// 🔴 <c>Preset</c> 被卸下時 <c>RotationModuleManager</c> 會 Dispose 掉預設集裡的所有模組，
    /// 包含「自動移動」——於是它的 <c>Execute</c> 整段不跑：<b>不寫 ForcedMovement，也不產生
    /// 移動目標視覺化</b>。使用者看到的就是「站著不動、連提示線都沒有」。
    /// ⚠️ 脫戰趕路時場上的被動怪在 <c>AIHintsBuilder.FillEnemies</c> 只拿到
    /// <c>PriorityUndesirable</c>（-3），進不了 <c>AIHints.PriorityTargets</c>
    /// ⇒ <c>SelectPrimaryTarget</c> 選不到任何目標 ⇒ 這個閘門必然關著。
    /// </remarks>
    private void LogPresetGate(bool on, bool hasTarget, bool autorotAllowed)
    {
        if (_loggedPresetGate == on)
            return;
        _loggedPresetGate = on;
        if (on)
        {
            Service.Logger.Information("[AI] 自動循環預設已掛上（有主要目標且允許出招）：預設集裡的模組從現在起每幀執行，「自動移動」模組會開始寫移動方向與標線。");
        }
        else
        {
            var why = !hasTarget
                ? "沒有主要目標（脫戰趕路時場上的被動怪優先度是 -3，進不了 PriorityTargets）"
                : "目前不允許出招（只在深牢出招的開關擋住）";
            Service.Logger.Information($"[AI] 自動循環預設已卸下：{why}。⚠️ 這代表預設集裡的「自動移動」模組**整段不執行** —— 不寫移動方向、也不畫移動目標標線，趕路完全交給 AI 自動走位處理。");
        }
    }

    private static bool? _loggedPresetGateReachable;

    /// <summary>
    /// 講出「掛／卸預設的那個判斷這一段到底有沒有被執行」。
    /// </summary>
    /// <remarks>
    /// 🔴 <see cref="LogPresetGate"/> 沒印任何東西時有兩種完全不同的意思：
    /// <list type="number">
    /// <item>閘門有跑、狀態一直沒變 —— 但這不可能靜默，<c>_loggedPresetGate</c> 是 <c>static bool?</c> 起始 <c>null</c>，<b>每個 session 第一次呼叫必定會印</b>。</item>
    /// <item>閘門<b>整段沒被執行</b>（<c>ForbidActions</c>／AFK／凝視／Pyretic／<c>cancel</c> 任一成立）；這支記下是被哪個開關擋的。</item>
    /// </list>
    /// <para>
    /// 契約：這一段不執行時 <c>autorot.Preset</c> 完全不被 AI 碰 —— 手動掛上的預設原封
    /// 不動繼續跑，「沒有主要目標就卸下預設」那條規則在這個狀態下不成立；走位與閃避
    /// 不受影響（<c>UpdateMovement</c> 在這個判斷之外）。
    /// </para>
    /// </remarks>
    private void LogPresetGateReachable(bool runs, bool forbidTargeting, bool cancelled)
    {
        if (_loggedPresetGateReachable == runs)
            return;
        _loggedPresetGateReachable = runs;
        if (runs)
            LogGateReason("[AI] 預設掛載判斷恢復執行：改由「有沒有主要目標」決定掛不掛預設。", ref _saidGateRuns);
        else if (cancelled)
            LogGateReason("[AI] 預設掛載判斷不執行：AI 正在停用／切換預設（cancel）。", ref _saidGateCancelled);
        else
            LogGateReason("[AI] 預設掛載判斷不執行：禁止出招／AFK／凝視或 Pyretic 將至。", ref _saidGateForbidden);
    }

    // MultiMode 每次換角都重建 AIBehaviour，實例旗標跟著歸零 —— 記在 static 才擋得住重播：
    // 同一個理由整場遊戲只用 Information 講一次完整的一行，之後同理由降 Verbose。
    // 契約：只從 Execute 的信號量區段內碰；最壞情況是重複印一行，不必上鎖。
    private static bool _saidGateRuns, _saidGateCancelled, _saidGateForbidden;

    private static void LogGateReason(string msg, ref bool alreadySaid)
    {
        if (alreadySaid)
        {
            Service.Logger.Verbose(msg);
            return;
        }
        alreadySaid = true;
        Service.Logger.Information(msg);
    }

    private static bool _loggedYieldMovement;
    /// <summary>兩個方向的長說明各印過了沒；印過之後只留短行。</summary>
    private static bool _explainedYieldMovement;
    private static bool _explainedTakeBackMovement;

    /// <summary>
    /// 把「AI 把移動讓給預設集的自動移動模組」講出來。
    /// </summary>
    /// <remarks>
    /// 🔴 讓位期間本 AI <b>完全不設</b> <c>NaviTargetPos</c>。若角色同時也不動，代表接手的那一方
    /// 也沒有寫出方向 —— 那就是「兩邊都不動」的殭屍狀態，必須看這一行才分得出來是誰的問題。
    /// <para>⚠️ 換手若只是因為「沒有人要求移動」（<see cref="Autorotation.MiscAI.NormalMovement.IdleNoDestination"/>）就走 <c>Verbose</c>：那是閒置常態，而且對應的 <c>[NormalMovement]</c> 說明行也降了同一級。</para>
    /// </remarks>
    private void LogMovementOwnership(bool yield)
    {
        if (yield == _loggedYieldMovement)
            return;
        _loggedYieldMovement = yield;
        // 長說明只印第一次：翻轉本身是有用的訊號，但每次都重印整段說明純粹是雜訊。
        // 兩個方向各記一個旗標，兩邊的說明都至少出現一次。
        bool explain;
        if (yield)
        {
            explain = !_explainedYieldMovement;
            _explainedYieldMovement = true;
        }
        else
        {
            explain = !_explainedTakeBackMovement;
            _explainedTakeBackMovement = true;
        }
        var msg = yield
            ? (explain ? "[AI] 移動擁有權交給預設集的「自動移動」模組：本 AI 這段期間完全不設導航目標。若角色同時站著不動，代表接手的那一方也沒寫出移動方向。" : "[AI] 移動擁有權交給預設集的「自動移動」模組")
            : (explain ? "[AI] 移動擁有權回到 AI 自動走位：預設集裡的「自動移動」模組沒有舉手（不在預設集裡、Destination 軌設成 None、或它這一段算不出目的地而主動交還——後者上一行會有同級的 [NormalMovement] 說明）。" : "[AI] 移動擁有權回到 AI 自動走位");
        if (Autorotation.MiscAI.NormalMovement.IdleNoDestination)
        {
            Service.Logger.Verbose(msg);
            return;
        }
        Service.Logger.Debug(msg);
        MovementLog.Digest.Record(yield ? MovementLog.YieldToPreset : MovementLog.BackToAI, _ex.Now);
    }

    /// <summary>建導航決策那一刻場上有幾個目標區；給下面那支診斷區分「沒人給方向」與「給了方向但算不出來」。</summary>
    private int _naviGoalZoneCount;
    private static bool _loggedNoDestination;

    /// <summary>
    /// 把「有人給了目標區，尋路卻算不出目的地」講出來 —— 這正是趕路「走幾步就停」的形狀。
    /// </summary>
    /// <remarks>
    /// <c>NavigationDecision.Build</c> 在最佳格就是玩家腳下時回傳 <c>Destination == null</c>，
    /// 而那有兩種完全不同的成因：<b>真的已經站在最好的位置</b>，或<b>目標區根本沒被 rasterize</b>
    /// （玩家格出了尋路視窗、或本地副本與存活 List 的 race）。
    /// 🔴 「有目標區卻回 null」與「權重全 0」都不足以判成後者：玩家真的就站在最高權重格、以及安全優先閘門（沒畫上權重場）都是正常結果。
    /// 判準見 <see cref="IsNaviStuck"/>。
    /// </remarks>
    private void LogNoDestination(bool evaluated, bool stuck)
    {
        // 🔴 讓位期間「連問都沒問」——本 AI 的導航決策這一段根本不會被使用，
        //    把它當成 false 會印出**假的**「導航恢復」（實際上只是移動擁有權換手，尋路什麼都沒改變）。
        //    不知道的時候就什麼都不宣稱，維持上一次的判斷（同款解法見 MovementOverride.LogFollowPathYield）。
        if (!evaluated)
            return;
        if (stuck == _loggedNoDestination)
            return;
        _loggedNoDestination = stuck;
        Service.Logger.Debug(stuck
            ? $"[AI] 導航算不出目的地：丟進尋路時場上有 {_naviGoalZoneCount} 個目標區，尋路卻回報「已經在最佳位置」⇒ 這一段不會移動。{_naviDecision.DiagSummary()}"
            : "[AI] 導航恢復：尋路重新算得出目的地。");
        if (stuck)
            MovementLog.Digest.Record(MovementLog.AINoDestination, _ex.Now);
    }

    /// <summary>
    /// 尋路回 <c>null</c> 是不是<b>真的</b>異常。
    /// </summary>
    /// <remarks>
    /// 三個判準，任一成立就算異常：①玩家格根本不在尋路視窗內 ⇒ 目標區對他完全沒有作用。
    /// ②目標區畫上去了，整張權重場卻還是 0（或更低）⇒ 畫了等於沒畫。
    /// ③場上有權重更高的格子，尋路卻回 null ⇒ 明明有更好的位置卻不去。
    /// 其餘情形（玩家格權重就是場上最高，而且那個最高是正的）都是<b>正確行為</b>，不該報。
    /// 🔴 ②一定要帶上 <c>DiagGoalsRasterized</c>：沒有 rasterize 而權重全 0 是安全優先閘門（見 <c>NavigationDecision.Build</c>），那是刻意行為，不是異常。
    /// 📌 <c>Map.MaxPriority</c> 的初值就是 <c>0f</c>，②用 <c>&lt;= 0f</c> 是對「從來沒有被抬高過」的精確比對；用寬的 epsilon 會把「玩家站在一個小的、真實存在的最高點」誤報成異常。
    /// </remarks>
    private static bool IsNaviStuck(in NavigationDecision navi)
        => !navi.DiagPlayerInWindow
        || (navi.DiagGoalsRasterized && navi.DiagMaxPriority <= 0f)
        || navi.DiagMaxPriority > navi.DiagPlayerPriority + 1e-4f;

    #endregion

    private void UpdateMovement(Actor player, Actor master, Targeting target, bool gazeOrPyreticImminent, Angle misdirectionAngle, ActionQueue? queueForSprint)
    {
        // 🔴 讓路：預設集裡的「自動移動」模組正在負責移動時，這裡完全不做自己的移動決策。
        //    兩邊各自算目的地、逐幀交替接管，使用者看到的就是角色抖動（見 NormalMovement.OwnsMovement）。
        //    ⚠️ 只讓出「移動」這一項：目標選擇、InteractWithTarget、凝視／Pyretic 的中斷詠唱、
        //    衝刺推送全部照舊，所以這裡不是提早 return，而是只把 NaviTargetPos 壓成 null。
        //    讓出後由 NormalMovement 單獨寫 Hints.ForcedMovement ⇒ 移動只有一個擁有者。
        var yieldMovement = Autorotation.MiscAI.NormalMovement.OwnsMovement;
        LogMovementOwnership(yieldMovement);
        MovementLog.Digest.Tick(_ex.Now);
        var playerPos = _ex.PlayerPosition;

        if (gazeOrPyreticImminent)
        {
            // gaze or pyretic imminent, drop any movement - we should have moved to safe zone already...
            ctrl.NaviTargetPos = null;
            ctrl.NaviTargetVertical = null;
            ctrl.ForceCancelCast = true;
        }
        else if (misdirectionAngle != default && _naviDecision.Destination is WPos destination)
        {
            ctrl.AllowInterruptingCastByMovement = true;
            // 🔴 這一支也要指派 ForceCancelCast：ctrl 這個欄位只在 AI 被停用時才走 AIController.Clear()，AI 跑著的時候**沒有任何逐幀重置點**，
            //    上面那一支（凝視／Pyretic）設下的 true 會殘留。迷惑與凝視可以同時存在（ImminentSpecialMode 與 ForbiddenDirections 是兩個獨立來源），
            //    凝視過去了、迷惑還在時就會落到這一支 ⇒ 不指派的話旗標卡在 true，之後每一幀只要在詠唱就被取消。
            //    進得到這一支就代表凝視／Pyretic 不迫在眉睫（上一支優先），所以這裡的正解一定是 false。
            //    ⚠️ 這不會吃掉模組自己提出的取消請求：AIController 是用 |= 疊到 Hints 上，不是覆寫。
            ctrl.ForceCancelCast = false;
            var dir = destination - playerPos;
            var distSq = dir.LengthSq();
            var threshold = 45f.Degrees();
            var forceddir = _ex.ForcedMovementDirection;
            var allowMovement = forceddir.AlmostEqual(Angle.FromDirection(dir), threshold.Rad);
            if (allowMovement)
                allowMovement = CalculateUnobstructedPathLength(forceddir) >= Math.Min(3f, distSq);
            ctrl.NaviTargetPos = !yieldMovement && allowMovement && distSq >= 0.01f ? destination : null;

            float CalculateUnobstructedPathLength(Angle dir)
            {
                var start = _naviCtx.Map.WorldToGrid(playerPos);
                var startx = start.x;
                var starty = start.y;
                if (!_naviCtx.Map.InBounds(startx, starty))
                    return 0f;

                var end = _naviCtx.Map.WorldToGrid(playerPos + 100f * dir.ToDirection());
                var startG = _naviCtx.Map.PixelMaxG[_naviCtx.Map.GridToIndex(startx, starty)];
                var pixels = _naviCtx.Map.EnumeratePixelsInLine(startx, starty, end.x, end.y);
                var len = pixels.Length;
                for (var i = 0; i < len; ++i)
                {
                    ref readonly var p = ref pixels[i];
                    var px = p.x;
                    var py = p.y;
                    if (!_naviCtx.Map.InBounds(px, py) || _naviCtx.Map.PixelMaxG[_naviCtx.Map.GridToIndex(px, py)] < startG)
                    {
                        var dest = _naviCtx.Map.GridToWorld(px, py, 0.5f, 0.5f);
                        return (dest - playerPos).LengthSq();
                    }
                }
                return float.MaxValue;
            }

            // debug
            //void drawLine(WPos from, WPos to, uint color) => Camera.Instance!.DrawWorldLine(new(from.X, player.PosRot.Y, from.Z), new(to.X, player.PosRot.Y, to.Z), color);
            //var toDest = _naviDecision.Destination.Value - player.Position;
            //drawLine(player.Position, _naviDecision.Destination.Value, Colors.Safe);
            //drawLine(_naviDecision.Destination.Value, _naviDecision.Destination.Value + toDest.Normalized().OrthoL(), Colors.Safe);
            //drawLine(player.Position, ctrl.NaviTargetPos.Value, Colors.Danger);
        }
        else
        {
            var toDest = _naviDecision.Destination != null ? _naviDecision.Destination.Value - playerPos : default;
            var distSq = toDest.LengthSq();

            // avoid relocating the instant a marginally "safer" spot appears when there's no real urgency yet (see MovementUrgencyThreshold);
            // still move immediately if that's actually needed to stay in range of whatever we're following
            var mustMoveNow = _config.MovementUrgencyThreshold <= 0f || ForceMovementIn <= _config.MovementUrgencyThreshold;
            if (!mustMoveNow)
            {
                var followActor = target.Target?.Actor ?? master;
                var maxRange = target.Target != null ? _config.MaxDistanceToTarget : _config.MaxDistanceToSlot;
                mustMoveNow = followActor != player && (_ex.FollowPosition - playerPos).LengthSq() > maxRange * maxRange;
            }

            // 只在「我方負責移動」時才有意義：讓位期間目的地本來就不該由這裡產生 ⇒ 那時是「沒問」不是「沒事」。
            // 收緊判準：只有「回 null」加「場上有目標區」還不夠（玩家真的就站在最高權重格、或安全優先閘門都是正常結果），見 IsNaviStuck。
            LogNoDestination(!yieldMovement, _naviDecision.Destination == null && _naviGoalZoneCount != 0 && IsNaviStuck(in _naviDecision));

            ctrl.NaviTargetPos = !yieldMovement && _ex.Now >= _navStartTime && mustMoveNow ? _naviDecision.Destination : null;
            ctrl.NaviTargetVertical = master != player ? _ex.MasterY : null;
            ctrl.AllowInterruptingCastByMovement = _ex.PlayerCastRemaining is float castRemaining && _naviDecision.LeewaySeconds <= castRemaining - 0.5d;
            ctrl.ForceCancelCast = false;

            //var cameraFacing = _ctrl.CameraFacing;
            //var dot = cameraFacing.Dot(_ctrl.TargetRot.Value);
            //if (dot < -0.707107f)
            //    _ctrl.TargetRot = -_ctrl.TargetRot.Value;
            //else if (dot < 0.707107f)
            //    _ctrl.TargetRot = cameraFacing.OrthoL().Dot(_ctrl.TargetRot.Value) > 0 ? _ctrl.TargetRot.Value.OrthoR() : _ctrl.TargetRot.Value.OrthoL();

            // sprint, if not in combat and far enough away from destination
            if (_ex.PlayerInCombat ? _naviDecision.LeewaySeconds <= 0f && distSq > 25f : player != master && distSq > 400f)
            {
                queueForSprint?.Push(ActionDefinitions.IDSprint, player, ActionQueue.Priority.Minimal + 100f);
            }
        }
    }

    // 與 AIHints.ForbiddenTargets 是同一批（PotentialTargets 結尾連續、優先度不高於 PriorityUndesirable 的那一段）；每幀都走，直接掃
    private bool TargetIsForbidden(ulong actorId)
    {
        var all = autorot.Hints.PotentialTargets;
        for (var i = all.Count - 1; i >= 0; --i)
        {
            var e = all[i];
            if (e.Priority > AIHints.Enemy.PriorityUndesirable)
                break;
            if (e.Actor.InstanceID == actorId)
                return true;
        }
        return false;
    }
}
