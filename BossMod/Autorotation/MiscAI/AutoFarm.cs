namespace BossMod.Autorotation.MiscAI;

public sealed class AutoFarm(RotationModuleManager manager, Actor player) : RotationModule(manager, player)
{
    // Track 成員名是預設集的序列化鍵、索引要對得上 Configs 順序：新軌只能往後加。
    public enum Track { General, Specific, QuestBattle, DeepDungeon, EpicEcho, Hunt, Retarget, SwitchTolerance }
    public enum GeneralStrategy { FightBack, AllowPull, Aggressive, Passive }
    public enum TargetingStrategy { None, Fate, All }
    public enum Flag { Disabled, Enabled }
    // 第 0 個選項＝沒設過這條軌的預設集拿到的值，必須是改動前的行為。
    public enum RetargetStrategy { Always, NoTarget, Hostiles, Never }
    public enum SwitchToleranceStrategy { Nearest, KeepCurrent }

    public static RotationModuleDefinition Definition()
    {
        RotationModuleDefinition res = new("Automatic targeting", "Collection of utilities to automatically target and pull mobs based on different criteria.", "AI", "veyn", RotationModuleQuality.Basic, new(~0ul), 1000, 1, RotationModuleOrder.HighLevel, CanUseWhileRoleplaying: true);

        res.Define(Track.General).As<GeneralStrategy>("General")
            .AddOption(GeneralStrategy.FightBack, "Automatically engage eligible mobs that are in combat with player, but don't pull new mobs", supportedTargets: ActionTargets.Hostile)
            .AddOption(GeneralStrategy.AllowPull, "Automatically engage eligible mobs that are in combat with player; if player is not in combat, pull new mobs", supportedTargets: ActionTargets.Hostile)
            .AddOption(GeneralStrategy.Aggressive, "Aggressively pull eligible mobs that are not yet in combat", supportedTargets: ActionTargets.Hostile)
            .AddOption(GeneralStrategy.Passive, "Do nothing");

        res.Define(Track.Specific).As<TargetingStrategy>("Target")
            .AddOption(TargetingStrategy.None, "Don't prioritize any mobs")
            .AddOption(TargetingStrategy.Fate, "Prioritize mobs in active fate")
            .AddOption(TargetingStrategy.All, "Prioritize ALL targetable mobs");

        res.Define(Track.QuestBattle).As<Flag>("QuestBattle", "Automatically attack solo duty bosses")
            .AddOption(Flag.Disabled, "Disabled")
            .AddOption(Flag.Enabled, "Enabled");

        res.Define(Track.DeepDungeon).As<Flag>("DD", "Automatically attack deep dungeon bosses if solo")
            .AddOption(Flag.Disabled, "Disabled")
            .AddOption(Flag.Enabled, "Enabled");

        res.Define(Track.EpicEcho).As<Flag>("EE", "Automatically attack all targets in unsynced duties")
            .AddOption(Flag.Disabled, "Disabled")
            .AddOption(Flag.Enabled, "Enabled");

        res.Define(Track.Hunt).As<Flag>("Hunt", "Automatically attack hunt marks once they have been pulled")
            .AddOption(Flag.Disabled, "Disabled")
            .AddOption(Flag.Enabled, "Enabled");

        // 內部名與上游 AutoTarget 的 Retarget 軌相同，從上游匯入的預設集照名字對得上。
        res.Define(Track.Retarget).As<RetargetStrategy>("Retarget", "When to switch target")
            .AddOption(RetargetStrategy.Always, "Switch whenever a higher-priority or closer eligible mob exists (current behavior)")
            .AddOption(RetargetStrategy.NoTarget, "Only switch target if player has no target (or the target is a dead or untargetable enemy); adds the boss module marks as must-kill-in-time are switched to regardless")
            .AddOption(RetargetStrategy.Hostiles, "Only switch target if player is not targeting an ally")
            .AddOption(RetargetStrategy.Never, "Never switch target; only apply priority changes to enemies. Exception: if you have no usable enemy target (nothing targeted, a coffer or other non-enemy, or a dead enemy) while a mob is attacking you, switch to that mob. Also switches to adds the boss module marks as must-kill-in-time (a lethal mechanic fires if they live)");

        res.Define(Track.SwitchTolerance).As<SwitchToleranceStrategy>("SwitchTolerance", "Target switching tolerance")
            .AddOption(SwitchToleranceStrategy.Nearest, "When picking a mob to pull, always choose the closest eligible one (current behavior)")
            .AddOption(SwitchToleranceStrategy.KeepCurrent, "When picking a mob to pull, keep the current target unless another mob of the same priority is clearly closer (current target gets a 3y head start; mobs within 3y count as equally close)");

        return res;
    }

    private enum SwitchReason { Pull, LowerPriority, Override }

    public override void Execute(StrategyValues strategy, Actor? primaryTarget, float estimatedAnimLockDelay, bool isMoving)
    {
        SwitchLog.Digest.Tick(World.CurrentTime);
        SwitchLog.ObserveCurrentTarget(Player.TargetID);

        var generalOpt = strategy.Option(Track.General);
        var generalStrategy = generalOpt.As<GeneralStrategy>();
        if (generalStrategy == GeneralStrategy.Passive)
            return;

        var allowPulling = generalStrategy switch
        {
            GeneralStrategy.AllowPull => !Player.InCombat,
            GeneralStrategy.Aggressive => true,
            _ => false
        };

        var currentTargetId = Player.TargetID;
        var pick = new PullPick
        {
            Sticky = strategy.Option(Track.SwitchTolerance).As<SwitchToleranceStrategy>() == SwitchToleranceStrategy.KeepCurrent,
            CurrentTargetId = currentTargetId,
            SwitchTargetKey = AutoFarmTargeting.InitialKey,
        };

        var (allowFate, allowAll) = strategy.Option(Track.Specific).As<TargetingStrategy>() switch
        {
            TargetingStrategy.All => (true, true),
            TargetingStrategy.Fate => (true, false),
            _ => (false, false)
        };

        if (strategy.Option(Track.QuestBattle).As<Flag>() == Flag.Enabled)
            allowAll |= Bossmods.ActiveModule?.Info?.Category == BossModuleInfo.Category.Quest;

        if (strategy.Option(Track.DeepDungeon).As<Flag>() == Flag.Enabled)
            allowAll |= Bossmods.ActiveModule?.Info?.Category == BossModuleInfo.Category.DeepDungeon && World.Party.WithoutSlot().Count() == 1;

        if (strategy.Option(Track.EpicEcho).As<Flag>() == Flag.Enabled)
            allowAll |= Player.Statuses.Any(s => s.ID == 2734);

        // first deal with pulling new enemies
        if (allowPulling)
        {
            if (allowFate && Utils.IsPlayerSyncedToFate(World))
                foreach (var e in Hints.PotentialTargets.Where(t => t.Actor.FateID == World.Client.ActiveFate.ID))
                {
                    var isForlorn = e.Actor.NameID is 6737u or 6738u;
                    Prioritize(ref pick, e, isForlorn ? 2 : 1);
                }

            if (strategy.Option(Track.Hunt).As<Flag>() == Flag.Enabled && Bossmods.ActiveModule?.Info?.Category == BossModuleInfo.Category.Hunt && Bossmods.ActiveModule?.PrimaryActor is Actor p && p.InCombat && p.HPRatio < 0.95f && Hints.FindEnemy(p) is { } huntEnemy)
                Prioritize(ref pick, huntEnemy, 1);

            if (allowAll)
                foreach (var h in Hints.PotentialTargets)
                    if (!h.Actor.IsStrikingDummy)
                        Prioritize(ref pick, h, 1);
        }
        var switchTarget = pick.SwitchTarget; // non-null if we bump any priorities

        // we are done with priority changes
        // if we've updated any priorities, we need to re-sort target array
        var reason = SwitchReason.Pull;
        if (switchTarget != null)
        {
            Hints.PotentialTargets.Sort((b, a) => a.Priority.CompareTo(b.Priority));
            Hints.HighestPotentialTargetPriority = Math.Max(0, Hints.PotentialTargets[0].Priority);
        }

        // if we did not select an enemy to pull, see if we can target something higher-priority than what we have now
        if (switchTarget == null && Player.InCombat)
        {
            var curTargetPrio = Hints.FindEnemy(primaryTarget)?.Priority ?? int.MinValue;
            switchTarget = ResolveTargetOverride(generalOpt.Value);
            reason = SwitchReason.Override;
            if (switchTarget == null && curTargetPrio < Hints.HighestPotentialTargetPriority)
            {
                switchTarget = Hints.PriorityTargets.MinBy(e => (e.Actor.Position - Player.Position).LengthSq())?.Actor;
                reason = SwitchReason.LowerPriority;
            }
        }

        // if we have target to switch to, do that
        if (switchTarget != null)
        {
            var current = World.Actors.Find(currentTargetId);
            var retarget = strategy.Option(Track.Retarget).As<RetargetStrategy>();
            var allowed = AutoFarmTargeting.AllowRetarget(retarget, current != null, current?.IsAlly ?? false, current != null && !current.IsDead && current.IsTargetable);
            var isSwitch = switchTarget.InstanceID != currentTargetId;
            if (!allowed && isSwitch && retarget == RetargetStrategy.Never
                && AutoFarmTargeting.NeverAllowsFightBack(current != null, current?.IsAlly ?? false, current?.Type == ActorType.Enemy, current != null && !current.IsDead && current.IsTargetable,
                    switchTarget.AggroPlayer || switchTarget.TargetID == Player.InstanceID))
            {
                allowed = true;
                SwitchLog.NeverFightBack(World.CurrentTime, current, switchTarget);
            }
            if (!allowed && isSwitch && retarget is RetargetStrategy.Never or RetargetStrategy.NoTarget
                && AutoFarmTargeting.AllowsUrgentKill(current?.IsAlly ?? false, current != null && !current.IsDead && current.IsTargetable && IsUrgentKill(current), IsUrgentKill(switchTarget)))
            {
                allowed = true;
                SwitchLog.UrgentKill(World.CurrentTime, current, switchTarget, retarget);
            }
            if (allowed)
            {
                if (isSwitch)
                    SwitchLog.Switched(World.CurrentTime, current, switchTarget, Hints, Player, reason);
                primaryTarget = Hints.ForcedTarget = switchTarget;
            }
            else if (isSwitch)
            {
                SwitchLog.Suppressed(World.CurrentTime, current, switchTarget, retarget);
            }
        }
    }

    private bool IsUrgentKill(Actor? a) => Hints.FindEnemy(a) is { UrgentKill: true } e && e.Actor == a;

    // 拉怪挑選的狀態；用 ref 傳而不是讓區域函式捕獲，Execute 才不會每幀一進來就配閉包物件
    private struct PullPick
    {
        public bool Sticky;
        public ulong CurrentTargetId;
        public Actor? SwitchTarget;
        public AutoFarmTargeting.CandidateKey SwitchTargetKey;
    }

    private void Prioritize(ref PullPick pick, AIHints.Enemy e, int prio)
    {
        e.Priority = prio;

        var key = pick.Sticky
            ? AutoFarmTargeting.StickyKey(prio, Player.DistanceToHitbox(e.Actor), e.Actor.InstanceID == pick.CurrentTargetId)
            : AutoFarmTargeting.NearestKey(prio, (e.Actor.Position - Player.Position).LengthSq());
        if (key.CompareTo(pick.SwitchTargetKey) > 0)
        {
            pick.SwitchTarget = e.Actor;
            pick.SwitchTargetKey = key;
        }
    }

    // 換目標的診斷：每次換寫 Debug、每分鐘彙總一行 Information。
    // 契約：只從 Execute（主執行緒）碰；static 是因為預設集每次重建都會換一個模組實例。
    private static class SwitchLog
    {
        public static readonly MinuteLogDigest Digest = new("[AutoFarm][換目標][每分鐘彙總]",
            ["換目標", "被改回後又換一次", "切換沒生效（每秒最多記一次）", "因「何時切換目標」設定沒換"],
            [0, -1, -1, -1], ["換目標最短間隔"]);

        private const int MaxDebugLinesPerWindow = 120;
        private static ulong _lastFrom, _lastTo, _suppressedFrom, _suppressedTo;
        private static bool _lastToTookEffect;
        private static DateTime _lastNoEffectAt, _debugWindowStart;
        private static int _debugLines;

        public static void ObserveCurrentTarget(ulong targetId)
        {
            if (_lastTo != 0 && targetId == _lastTo)
                _lastToTookEffect = true;
        }

        public static void Switched(DateTime now, Actor? from, Actor to, AIHints hints, Actor player, SwitchReason reason)
        {
            var fromId = from?.InstanceID ?? 0;
            var samePair = fromId == _lastFrom && to.InstanceID == _lastTo;
            if (samePair && !_lastToTookEffect)
            {
                // 上一筆一直沒生效（不可選取、或同一幀後面有人改寫 ForcedTarget）：每幀都會走到，只計數。
                if ((now - _lastNoEffectAt).TotalSeconds >= 1d)
                {
                    _lastNoEffectAt = now;
                    Digest.Record(2, now);
                }
                return;
            }
            var text = Describe(from, to, hints, player, reason);
            Digest.Record(0, now, "最近一次：" + text);
            if (samePair)
                Digest.Record(1, now);
            _lastFrom = fromId;
            _lastTo = to.InstanceID;
            _lastToTookEffect = false;
            _suppressedFrom = _suppressedTo = 0;

            if ((now - _debugWindowStart).TotalSeconds >= MinuteLogDigest.WindowSeconds)
            {
                _debugWindowStart = now;
                _debugLines = 0;
            }
            if (++_debugLines <= MaxDebugLinesPerWindow)
                Service.Logger.Debug($"[AutoFarm][換目標] {text}{(samePair ? "（被改回後又換一次）" : "")}");
        }

        public static void Suppressed(DateTime now, Actor? from, Actor to, RetargetStrategy retarget)
        {
            var fromId = from?.InstanceID ?? 0;
            if (fromId == _suppressedFrom && to.InstanceID == _suppressedTo)
                return;
            _suppressedFrom = fromId;
            _suppressedTo = to.InstanceID;
            Digest.Record(3, now);
            Service.Logger.Debug($"[AutoFarm][換目標] 想換成 {Name(to)}，但「何時切換目標」設定是 {retarget}，維持 {Name(from)}");
        }

        private static ulong _fightBackLastTo;
        private static DateTime _fightBackLastAt;

        // Never 的例外放行：同一隻 10 秒最多一行。
        public static void NeverFightBack(DateTime now, Actor? from, Actor to)
        {
            if (to.InstanceID == _fightBackLastTo && (now - _fightBackLastAt).TotalSeconds < 10d)
                return;
            _fightBackLastTo = to.InstanceID;
            _fightBackLastAt = now;
            var why = from == null ? "沒有目標" : from.Type == ActorType.Enemy ? $"目標 {Name(from)} 已死亡或無法選取" : $"目標 {Name(from)} 不是敵人";
            Service.Logger.Information($"[AutoFarm][換目標] 「何時切換目標」是 Never，但{why}，而 {Name(to)} 正在打我：放行換過去");
        }

        private static ulong _urgentLastTo;
        private static DateTime _urgentLastAt;

        // 限時必殺的破例放行：同一隻 10 秒最多一行。
        public static void UrgentKill(DateTime now, Actor? from, Actor to, RetargetStrategy retarget)
        {
            if (to.InstanceID == _urgentLastTo && (now - _urgentLastAt).TotalSeconds < 10d)
                return;
            _urgentLastTo = to.InstanceID;
            _urgentLastAt = now;
            Service.Logger.Information($"[AutoFarm][換目標] 「何時切換目標」是 {retarget}，但 {Name(to)} 是首領模組標記的限時必殺目標（時限內沒打掉會致死）：從 {Name(from)} 換過去");
        }

        private static string Describe(Actor? from, Actor to, AIHints hints, Actor player, SwitchReason reason)
        {
            var why = reason switch
            {
                SwitchReason.Pull => "拉怪選目標",
                SwitchReason.LowerPriority => "目前目標優先度較低",
                _ => "軌道指定的目標"
            };
            return $"{Name(from)}（優先度 {Prio(hints, from)}）→ {Name(to)}（優先度 {Prio(hints, to)}），距離 {player.DistanceToHitbox(to):f1} 米，原因：{why}";
        }

        private static string Name(Actor? a) => a == null ? "（無目標）" : $"{a.Name} [{a.InstanceID:X}]";

        // 不是敵人（友方、沒有目標、不在敵人表裡）時優先度是「不知道」，寫 ? 不寫數字。
        private static string Prio(AIHints hints, Actor? a) => hints.FindEnemy(a) is { } e && e.Actor == a ? e.Priority.ToString() : "?";
    }
}

// 換目標判斷的純函式：不碰遊戲狀態，離線測試直接呼叫。
public static class AutoFarmTargeting
{
    // 同優先度時，目前目標先讓這麼多米；距離在這之內的都算一樣近。
    public const float SwitchTolerance = 3f;

    public readonly record struct CandidateKey(int Priority, float Score, bool IsCurrent) : IComparable<CandidateKey>
    {
        public int CompareTo(CandidateKey other)
        {
            if (Priority.CompareTo(other.Priority) is var p && p != 0)
                return p;
            if (Score.CompareTo(other.Score) is var s && s != 0)
                return s;
            return IsCurrent.CompareTo(other.IsCurrent);
        }
    }

    public static readonly CandidateKey InitialKey = new(0, float.MinValue, false);

    // 改動前的排序鍵（優先度、負的中心距離平方），IsCurrent 恆為 false ⇒ 比較結果與原本的 tuple 逐位元相同。
    public static CandidateKey NearestKey(int priority, float distanceSq) => new(priority, -distanceSq, false);

    public static CandidateKey StickyKey(int priority, float hitboxDistance, bool isCurrent)
        => new(priority, -Math.Max(SwitchTolerance, isCurrent ? hitboxDistance - SwitchTolerance : hitboxDistance), isCurrent);

    // 已死亡／不可選取的敵人算「沒有目標」；友方不論死活都算有目標（補師選著倒地的隊友要復活，不能搶走）。
    public static bool AllowRetarget(AutoFarm.RetargetStrategy strategy, bool hasTarget, bool targetIsAlly, bool targetUsable) => strategy switch
    {
        AutoFarm.RetargetStrategy.NoTarget => !hasTarget || (!targetIsAlly && !targetUsable),
        AutoFarm.RetargetStrategy.Hostiles => !hasTarget || !targetIsAlly,
        AutoFarm.RetargetStrategy.Never => false,
        _ => true
    };

    // Never 的唯一例外：手上沒有可打的敵人（沒有目標、選著寶箱之類的非敵人、或敵人已死／不可選取）而候選正在打我。
    // 友方目標照舊不搶；換去的對象必須是正在打我的那隻，不會因此去拉新的怪。
    public static bool NeverAllowsFightBack(bool hasTarget, bool targetIsAlly, bool targetIsEnemy, bool targetUsable, bool candidateAttacksPlayer)
        => candidateAttacksPlayer && (!hasTarget || !targetIsAlly && (!targetIsEnemy || !targetUsable));

    // NoTarget／Never 的另一個例外：候選是模組標記的限時必殺目標（AIHints.Enemy.UrgentKill）。
    // 友方目標照舊不搶；手上已經是一隻還能打的限時必殺目標時不換，多隻之間不來回。
    public static bool AllowsUrgentKill(bool targetIsAlly, bool targetIsUsableUrgent, bool candidateIsUrgent)
        => candidateIsUrgent && !targetIsAlly && !targetIsUsableUrgent;
}
