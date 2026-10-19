namespace BossMod.Autorotation.xan;

public sealed class DeepDungeonAI : AIBase
{
    public enum Track { Potion, Kite, KiteInstantOnly, KiteSprint }

    /// <summary>
    /// 預設<b>關閉</b>的開關用的選項列舉。
    /// </summary>
    /// <remarks>
    /// ⚠️ 不能重用 <c>AbilityUse</c>：<c>AddOption</c> 要求選項的加入順序等於列舉值
    /// （對不上會直接擲例外），而預設值一律是索引 0 —— <c>AbilityUse</c> 的索引 0 是
    /// <c>Enabled</c>，拿它做「預設關」的開關會變成預設開。這裡把 Disabled 放在索引 0。
    /// </remarks>
    public enum OptIn { Disabled, Enabled }

    // 風箏的數值參數放在深牢設定頁（Global.DeepDungeon.AutoDDConfig）：
    // track 選項只吃列舉、給不了滑桿，而使用者要調的正是數值。
    private static readonly Global.DeepDungeon.AutoDDConfig Config = Service.Config.Get<Global.DeepDungeon.AutoDDConfig>();

    private readonly EventSubscriptions _subscriptions;

    public DeepDungeonAI(RotationModuleManager manager, Actor player) : base(manager, player)
    {
        _subscriptions = new(World.Actors.IncomingEffectAdd.Subscribe(OnIncomingEffect));
    }

    public override void Dispose()
    {
        _subscriptions.Dispose();
        base.Dispose();
    }

    public static RotationModuleDefinition Definition()
    {
        var def = new RotationModuleDefinition("Deep Dungeon AI", "Utilities for deep dungeon - potion/pomander user", "AI (xan)", "xan", RotationModuleQuality.Basic, new BitMask(~0ul), 100, CanUseWhileRoleplaying: true);

        def.AbilityTrack(Track.Potion, "Potion");
        def.AbilityTrack(Track.Kite, "Kite enemies");
        def.Define(Track.KiteInstantOnly).As<OptIn>("KiteInstantOnly", "Kite: instant casts only")
            .AddOption(OptIn.Disabled, "Disabled")
            .AddOption(OptIn.Enabled, "Enabled");
        def.Define(Track.KiteSprint).As<OptIn>("KiteSprint", "Kite: allow Sprint")
            .AddOption(OptIn.Disabled, "Disabled")
            .AddOption(OptIn.Enabled, "Enabled");

        return def;
    }

    private static bool OptedIn(StrategyValues strategy, Track track) => strategy.Option(track).As<OptIn>() == OptIn.Enabled;

    enum OID : uint
    {
        Unei = 0x3E1A,
    }

    enum Transformation : uint
    {
        None,
        Manticore,
        Succubus,
        Kuribu,
        Dreadnaught
    }

    enum SID : uint
    {
        Transfiguration = 565,
        ItemPenalty = 1094,
    }

    public override void Execute(StrategyValues strategy, Actor? primaryTarget, float estimatedAnimLockDelay, bool isMoving)
    {
        if (World.DeepDungeon.DungeonId == 0)
            return;

        var transformation = Transformation.None;
        if (Player.FindStatus(SID.Transfiguration) is { } status)
        {
            transformation = (status.Extra & 0xFF) switch
            {
                42 => Transformation.Manticore,
                43 => Transformation.Succubus,
                49 => Transformation.Kuribu,
                244 => Transformation.Dreadnaught,
                _ => Transformation.None
            };
        }

        if (transformation != Transformation.None)
        {
            DoTransformActions(strategy, primaryTarget, transformation);
            return;
        }

        if (IsRanged && !Player.InCombat && primaryTarget is Actor target && !target.InCombat && !target.IsAlly)
            // bandaid fix to help deal with constant LOS issues
            Hints.GoalZones.Add(_losGoal.SingleTarget(Hints, target, 3, 0.1f));

        SetupKiteZone(strategy, primaryTarget);

        if (Player.FindStatus(SID.ItemPenalty) != null)
            return;

        var (regenAction, potAction) = World.DeepDungeon.DungeonId switch
        {
            DeepDungeonState.DungeonType.POTD => (ActionDefinitions.IDPotionSustaining, ActionDefinitions.IDPotionMax),
            DeepDungeonState.DungeonType.HOH => (ActionDefinitions.IDPotionEmpyrean, ActionDefinitions.IDPotionSuper),
            DeepDungeonState.DungeonType.EO => (ActionDefinitions.IDPotionOrthos, ActionDefinitions.IDPotionHyper),
            _ => (default, default)
        };

        // 🔴 regenAction 是**深牢專屬**的秘藥（生者秘藥／天之秘藥／正統治療劑）——
        //    特殊商店購入、不可出售、對使用者而言是昂貴且刻意保留的資源。
        //    上游預設會在 HP 低於 40%／60% 時自動喝掉它；這裡預設不用，要用得自己去開。
        //    ⚠️ 這與下面的保命藥水是兩回事：保命用的是一般治療劑（頂級／聖級／上級），
        //    那些是消耗品、買得到，自動使用沒有爭議。
        if (regenAction != default && Config.AutoUseDeepDungeonPotion && ShouldPotion(strategy))
            Hints.ActionsToExecute.Push(regenAction, Player, ActionQueue.Priority.Medium);

        // 🔴 保命藥水**不在這裡**推了，改由 AutoClear（區域模組）推。
        //    原因：這整個模組只有在 AIBehaviour 掛上 preset 時才會跑，而沒有目標時 preset 會被卸下；
        //    踩到陷阱多半正是趕路、沒有目標的時候，於是保命藥水在最需要的時候必定不會觸發。
        //    區域模組的 hints.ActionsToExecute 走的是每幀無條件消費的那條路。
        _ = potAction; // 仍保留上面的查表，讓「哪一座用哪瓶」的資料只有一份
    }

    private readonly GoalMemo _losGoal = new();

    private bool IsRanged => Player.Class.GetRole() is Role.Ranged or Role.Healer;

    private static readonly HashSet<uint> NoMeleeAutos = [
        // hoh
        0x22C3, // heavenly onibi
        0x22C5, // heavenly dhruva
        0x22C6, // heavenly sai taisui
        0x22DC, // heavenly dogu
        0x22DE, // heavenly ganseki
        0x22ED, // heavenly kongorei
        0x22EF, // heavenly maruishi
        0x22F3, // heavenly rachimonai
        0x22FC, // heavenly doguzeri
        0x2320, // heavenly nuppeppo (WHM) (uses stone)

        // orthos
        0x3DCC, // orthos imp
        0x3DCE, // orthos fachan
        0x3DD2, // orthos water sprite
        0x3DD4, // orthos microsystem
        0x3DD5, // orthosystem β
        0x3DE0, // orthodemolisher
        0x3DE2, // orthodroid
        0x3DFD, // orthos apa
        0x3E10, // orthos ice sprite
        0x3E5C, // orthos ahriman
        0x3E62, // orthos abyss
        0x3E63, // orthodrone
        0x3E64, // orthosystem γ
        0x3E66, // orthosystem α
    ];

    #region 遠距攻擊者的執行期觀測

    /// <summary>
    /// 本次探索觀測到「不必貼身也能打人」的怪物 OID。
    /// </summary>
    /// <remarks>
    /// 🔑 <b>風箏的成敗判準不是「間距有沒有拉開」。</b>怪物追得上是常態，拉不開距離是預期結果；
    /// 風箏真正的收益是<b>逼怪物走路而不是攻擊，讓吃到的攻擊次數變少</b>。
    /// 所以「退了三秒還沒拉開距離就停用」會在功能正常運作時誤判成失效。
    /// <para>
    /// 真正該停用風箏的情況只有一種：<b>這隻怪根本不需要追上你</b>——它在近戰距離外照樣打得到。
    /// 對這種怪拉開距離換不到任何攻擊次數的減少，只是白跑。這個集合就是執行期觀測到的那些怪。
    /// </para>
    /// <para>
    /// 這也是寫死的 <see cref="NoMeleeAutos"/> 清單的一般化版本：那份清單只涵蓋天之逆焰與
    /// 厄運迷宮的部分怪物，死者宮殿完全沒有，而且台服不保證與國際服相同。
    /// </para>
    /// <para>⚠️ static：自動循環模組會隨預設切換而重建，觀測結果不該跟著歸零。換樓層／換區時清空。</para>
    /// </remarks>
    private static readonly HashSet<uint> RangedAttackerOIDs = [];

    /// <summary>每個 OID 觀測到幾次「在近戰距離外挨打」。</summary>
    private static readonly Dictionary<uint, int> RangedAttackObservations = [];

    private static uint _observationZone;

    /// <summary>
    /// 判定「這一擊來自近戰距離外」的門檻（hitbox 到 hitbox）。
    /// </summary>
    /// <remarks>
    /// ⚠️ 刻意取得比近戰攻擊距離（約 3y）寬很多。傷害事件是伺服器回傳的，
    /// 客戶端收到時已經過了一個來回；期間玩家可能已經跑開好幾碼，
    /// 於是「近戰怪打完我、我跑開、事件才到」看起來就像遠距攻擊。
    /// 取 8y ＋ 下面的多次採樣，是為了讓誤判方向落在安全側：
    /// <b>寧可漏判一隻遠距怪（風箏白跑，無害），也不要誤判近戰怪（把有效的風箏關掉）。</b>
    /// </remarks>
    private const float RangedAttackDistance = 8f;

    /// <summary>要幾次觀測才認定。單次可能是延遲造成的假象。</summary>
    private const int RangedAttackSamples = 3;

    /// <summary>
    /// 寫死的 <see cref="NoMeleeAutos"/> 清單說「不用近戰平砍」，但實測會近身平砍的 OID。
    /// </summary>
    /// <remarks>
    /// 🔑 <b>清單的自校正是雙向的。</b><see cref="RangedAttackerOIDs"/> 修的是「清單沒列到、
    /// 但其實是遠程」；這一份修的是相反方向——<b>清單列了、但在台服其實是近戰</b>。
    /// 那份清單是上游從國際服整理的，台服的怪不保證一樣，而且錯誤兩個方向都會靜默生效：
    /// 錯列成遠程 ⇒ 對一隻該風箏的怪整場不風箏（使用者體感就是「風箏沒作用」）。
    /// <para>
    /// 判別軸與 <see cref="RangedAttackerOIDs"/> 完全相同（<c>ActionCategory == 1</c> 的自動攻擊
    /// ＋造成傷害＋多次採樣），只是距離條件相反。
    /// </para>
    /// </remarks>
    private static readonly HashSet<uint> MeleeAttackerOIDs = [];

    /// <summary>每個 OID 觀測到幾次「在近戰距離內挨打」。</summary>
    private static readonly Dictionary<uint, int> MeleeAttackObservations = [];

    /// <summary>
    /// 判定「這一擊來自近戰距離內」的門檻（hitbox 到 hitbox）。
    /// </summary>
    /// <remarks>
    /// ⚠️ 這裡刻意取得比 <see cref="RangedAttackDistance"/> <b>嚴格</b>：要推翻寫死的清單，
    /// 證據就該硬一點。5y 內幾乎不可能是遠程平砍誤判成近戰的結果
    /// （伺服器來回的位移只會讓距離看起來變大，不會變小）。
    /// </remarks>
    private const float MeleeAttackDistance = 5f;

    private void OnIncomingEffect(Actor actor, int index)
    {
        if (actor.InstanceID != Player.InstanceID || World.DeepDungeon.DungeonId == 0)
            return;

        ResetObservationsOnZoneChange();

        ref readonly var eff = ref actor.IncomingEffects[index];
        var source = World.Actors.Find(eff.SourceInstanceId);
        if (source == null || source.IsAlly || source.InstanceID == Player.InstanceID)
            return;

        // 只採計自動攻擊。技能有射程是正常的，不代表這隻怪「不必追」——
        // 持續傷害來自平砍，那才是風箏能減少的東西。
        if (!IsAutoAttack(eff.Action))
            return;

        if (!DealtDamage(eff.Effects))
            return;

        var oid = source.OID;
        var dist = Player.DistanceToHitbox(source);

        if (dist <= MeleeAttackDistance)
        {
            ObserveMeleeAttack(oid);
            return;
        }

        if (dist <= RangedAttackDistance)
            return; // 中間地帶：兩邊都不算，避免把延遲造成的位移當成證據

        if (RangedAttackerOIDs.Contains(oid))
            return;

        var n = RangedAttackObservations.GetValueOrDefault(oid) + 1;
        RangedAttackObservations[oid] = n;
        if (n < RangedAttackSamples)
            return;

        RangedAttackerOIDs.Add(oid);
        // 要使用者回報的診斷；一個 OID 只印一次
        Service.Logger.Information($"[DD kite] OID {oid:X} 在近戰距離外仍以自動攻擊命中玩家 {n} 次，本次探索對它停用風箏。");
    }

    /// <summary>
    /// 記一次「這隻怪在近戰距離內平砍了我」。只有寫死清單裡的 OID 才有意義——
    /// 不在清單裡的怪本來就會風箏，不需要證據。
    /// </summary>
    private static void ObserveMeleeAttack(uint oid)
    {
        if (!NoMeleeAutos.Contains(oid) || MeleeAttackerOIDs.Contains(oid))
            return;

        var n = MeleeAttackObservations.GetValueOrDefault(oid) + 1;
        MeleeAttackObservations[oid] = n;
        if (n < RangedAttackSamples)
            return;

        MeleeAttackerOIDs.Add(oid);
        Service.Logger.Information(
            $"[DD kite] OID {oid:X} 被寫死清單標成遠程平砍，但實測它在 {MeleeAttackDistance:f0}y 內近身平砍了 {n} 次" +
            $"（台服資料與上游清單有差異），本次探索改為照樣風箏。");
    }

    private void ResetObservationsOnZoneChange()
    {
        if (_observationZone == World.CurrentZone)
            return;
        _observationZone = World.CurrentZone;
        RangedAttackerOIDs.Clear();
        RangedAttackObservations.Clear();
        MeleeAttackerOIDs.Clear();
        MeleeAttackObservations.Clear();
    }

    /// <summary>
    /// 這個 action 是不是自動攻擊。
    /// </summary>
    /// <remarks>
    /// 📌 判準是 <c>Action.ActionCategory == 1</c>（台服 ActionCategory 表 row 1 ＝「自動攻擊」）。
    /// 用資料表而不是寫死 id，
    /// 因為有些怪有自己專屬的平砍 action。
    /// </remarks>
    private static bool IsAutoAttack(ActionID action)
        => action.Type == ActionType.Spell && Service.LuminaRow<Lumina.Excel.Sheets.Action>(action.ID)?.ActionCategory.RowId == 1u;

    private static bool DealtDamage(ActionEffects effects)
    {
        foreach (var e in effects)
            if (e.Type is ActionEffectType.Damage or ActionEffectType.BlockedDamage or ActionEffectType.ParriedDamage)
                return true;
        return false;
    }

    #endregion

    // ── 風箏診斷 ──────────────────────────────────────────────────────
    // 🔑 使用者回報「有打但不走位」＝出招層正常、斷點在移動層。風箏要成立得連過六七關
    //    （職業角色／track／怪的類型／目標讀條／權重場有沒有被碾／誰在寫 ForcedMovement），
    //    而失敗全是靜默的，光看結果分不出是哪一關。所以每一關都把理由寫進 log。
    // 節流：只在戰鬥中；每種狀態各自計時，細節（OID／職業角色）變了立刻印，沒變就每分鐘最多一次；
    //       本區第一次與細節變了記 Information，同區同細節的每分鐘重印記 Debug。
    // 呼叫端先問 ShouldKiteDiag 再組字串：不印的幀不配置。
    private enum KiteDiagState { TrackOff, NotRanged, NoTarget, ObservedRanged, HardcodedList, TargetCasting, Active, Count }
    private const double KiteDiagRepeatSeconds = 60d;
    private static readonly DateTime[] _kiteDiagLastAt = new DateTime[(int)KiteDiagState.Count];
    private static readonly ulong[] _kiteDiagLastDetail = new ulong[(int)KiteDiagState.Count];
    private static readonly ushort[] _kiteDiagLastZone = new ushort[(int)KiteDiagState.Count];

    /// <summary>風箏對<b>當前目標</b>被停用的原因；供深牢狀態列顯示。</summary>
    public enum KiteSuppression { None, HardcodedList, ObservedRanged }

    /// <summary>最近一幀的停用原因，以及它是什麼時候寫的（過期就不要再顯示，避免說謊）。</summary>
    public static KiteSuppression Suppression { get; private set; }
    public static DateTime SuppressionAt { get; private set; }

    private void SetSuppression(KiteSuppression reason)
    {
        Suppression = reason;
        SuppressionAt = World.CurrentTime;
    }

    /// <summary>這一幀要不要印 <paramref name="state"/> 那一行；回 true 時已記下時間與細節。</summary>
    /// <param name="repeat">true＝同一區、同一細節只是隔了一分鐘再印（記 Debug）；false＝本區第一次或細節變了（記 Information）。</param>
    /// <param name="detail">變化偵測用的粗鍵（OID、職業角色）；不可放距離之類每幀都在變的數值。</param>
    private bool ShouldKiteDiag(KiteDiagState state, out bool repeat, ulong detail = 0)
    {
        var i = (int)state;
        var now = World.CurrentTime;
        var zone = World.CurrentZone;
        var since = (now - _kiteDiagLastAt[i]).TotalSeconds;
        repeat = _kiteDiagLastAt[i] != default && detail == _kiteDiagLastDetail[i] && zone == _kiteDiagLastZone[i];
        if (repeat && since is >= 0d and < KiteDiagRepeatSeconds)
            return false;
        _kiteDiagLastAt[i] = now;
        _kiteDiagLastDetail[i] = detail;
        _kiteDiagLastZone[i] = zone;
        return true;
    }

    private static void KiteDiagLog(bool repeat, string message)
    {
        if (repeat)
            Service.Logger.Debug(message);
        else
            Service.Logger.Information(message);
    }

    private void SetupKiteZone(StrategyValues strategy, Actor? primaryTarget)
    {
        FlushKiteAggroDigest();
        if (!Player.InCombat)
            return;

        if (!strategy.Enabled(Track.Kite))
        {
            if (ShouldKiteDiag(KiteDiagState.TrackOff, out var repeat))
                KiteDiagLog(repeat, "[DD kite] 停用：preset 的「Kite enemies」不是 Enabled");
            return;
        }
        if (!IsRanged)
        {
            var role = Player.Class.GetRole();
            if (ShouldKiteDiag(KiteDiagState.NotRanged, out var repeat, (ulong)role))
                KiteDiagLog(repeat, $"[DD kite] 停用：本職業角色是 {role}，風箏只對遠程與治療生效");
            return;
        }
        if (primaryTarget == null)
        {
            if (ShouldKiteDiag(KiteDiagState.NoTarget, out var repeat))
                KiteDiagLog(repeat, "[DD kite] 停用：目前沒有主要目標");
            return;
        }

        // 執行期觀測到這隻怪不必追上你就能打你 ⇒ 拉開距離換不到任何好處，別白跑。
        // 📌 排在寫死清單之前：兩者都是「遠程」判定，但這一份是實測證據，優先權較高。
        if (RangedAttackerOIDs.Contains(primaryTarget.OID))
        {
            SetSuppression(KiteSuppression.ObservedRanged);
            if (ShouldKiteDiag(KiteDiagState.ObservedRanged, out var repeat, primaryTarget.OID))
                KiteDiagLog(repeat, $"[DD kite] 停用：目標 OID {primaryTarget.OID:X} 已被觀測為遠距攻擊者");
            return;
        }

        // wew（上游寫死的清單）
        // 🔑 但清單可以被實測推翻：同一隻怪若被觀測到近身平砍，就照樣風箏。
        //    清單是上游從國際服整理的，台服不保證一樣，而錯誤是靜默的。
        if (NoMeleeAutos.Contains(primaryTarget.OID) && !MeleeAttackerOIDs.Contains(primaryTarget.OID))
        {
            SetSuppression(KiteSuppression.HardcodedList);
            if (ShouldKiteDiag(KiteDiagState.HardcodedList, out var repeat, primaryTarget.OID))
                KiteDiagLog(repeat, $"[DD kite] 停用：目標 OID {primaryTarget.OID:X} 在寫死的「不用近戰平砍」清單裡（尚未觀測到它近身平砍）");
            return;
        }

        SetSuppression(KiteSuppression.None);

        // assume we don't need to kite if mob is busy casting (TODO: some mob spells can be cast while moving, maybe there's a column in sheets for it)
        if (primaryTarget.CastInfo != null)
        {
            if (ShouldKiteDiag(KiteDiagState.TargetCasting, out var repeat))
                KiteDiagLog(repeat, "[DD kite] 暫停：目標正在讀條");
            return;
        }

        var maxKite = Config.KiteMinDistance;
        // 防呆：外圈永遠要比內圈大，否則甜甜圈是空的、風箏靜默失效
        var maxRange = Math.Max(Config.KiteMaxDistance, maxKite + 1f);

        var primaryPos = primaryTarget.Position;
        var total = maxRange + Player.HitboxRadius + primaryTarget.HitboxRadius;
        var totalKite = maxKite + Player.HitboxRadius + primaryTarget.HitboxRadius;
        var goalFactor = Config.KiteWeight;
        Hints.GoalZones.Add(KiteRingGoal(primaryPos, total, totalKite, goalFactor));

        // 告訴 AI「這一幀往後退是刻意的」，免得閃避走位的『別後退』懲罰把上面那個 0.05 碾平
        if (Config.KiteAllowRetreatWhileDodging)
            Hints.WantKiting = true;

        // 目前還在圈內＝正在往外退（而不是已經站好位）
        var dist = Player.DistanceToHitbox(primaryTarget);
        var retreating = dist < maxKite;

        // 🔑 這一行才是「有打但不走位」的判讀依據：風箏區已經放下去了，
        //    所以接下來要看的是「誰在決定移動、以及有沒有別的權重把它壓掉」。
        //    ForcedMovement 非 null＝已經有別的模組（多半是 Automatic movement）在寫移動輸出，
        //    此時 AIController 不會再補；GoalZones 的數量與 ForbiddenZones 的數量則說明
        //    這一幀的權重場有多擁擠（閃避方向場的量級是 0.5，風箏只有 0.05）。
        if (retreating && ShouldKiteDiag(KiteDiagState.Active, out var activeRepeat))
            KiteDiagLog(activeRepeat, $"[DD kite] 生效：距離 {dist:f1}y < 內圈 {maxKite:f1}y、權重 {goalFactor:f2}"
                + $"；抑制後退懲罰={Config.KiteAllowRetreatWhileDodging}"
                + $"；本幀 GoalZones={Hints.GoalZones.Count}、ForbiddenZones={Hints.ForbiddenZones.Count}"
                + $"；ForcedMovement={(Hints.ForcedMovement == null ? "null（AI 自行決定）" : "已被其他模組寫入")}"
                + $"；Automatic movement 模組={(MiscAI.NormalMovement.Instance != null ? "在線" : "不在")}");

        if (!retreating)
            return;

        AddKiteAggroAvoidance(primaryTarget, (totalKite + total) * 0.5f);

        // 施法職業在退避途中只出瞬發。讀條時 AI 完全不移動
        // （NavigationDecision 跳過目標區、AIController 禁移動），不設這個的話風箏窗口只剩 GCD 間隙。
        if (OptedIn(strategy, Track.KiteInstantOnly))
            Hints.MaxCastTime = 0;

        // 退避途中允許衝刺。目的是多拉一點喘息距離、少挨幾下，不是逃脫。
        if (OptedIn(strategy, Track.KiteSprint))
            TrySprint();
    }

    // 閉包放在獨立方法裡：寫在 SetupKiteZone 本體時，捕獲的區域變數讓每次呼叫（含脫戰提早返回）都先配一個閉包物件
    private static Func<WPos, float> KiteRingGoal(WPos center, float outer, float inner, float weight) => pos =>
    {
        var dist = (pos - center).Length();
        return dist <= outer && dist >= inner ? weight : default;
    };

    #region 風箏時避開其他怪的仇恨範圍

    // 同一幀最多畫幾隻（每隻都是每像素求值的禁區）；與趕路迴避同一個上限。
    private const int MaxKiteAggroShapes = 6;
    private const int KiteRingSamples = 16;
    private const double KiteAggroStuckSeconds = 2.5d;
    private const float KiteAggroStuckDistance = 1.5f;
    private const double KiteAggroSuppressSeconds = 6d;

    private static readonly Func<WPos, float>[] _kiteShapes = new Func<WPos, float>[MaxKiteAggroShapes];
    private static readonly ulong[] _kiteShapeIds = new ulong[MaxKiteAggroShapes];
    private static DateTime _kiteAggroSuppressUntil;
    private static (WPos Pos, DateTime At)? _kiteAggroProgress;

    // 每分鐘彙總一行的計數（幀數）；0＝那一分鐘沒發生
    private static DateTime _kiteAggroDigestStart;
    private static int _dgAvoidFrames, _dgShapes, _dgTrimmedFrames, _dgNoClearFrames, _dgInsideSkips, _dgDodgeFrames, _dgSuppressedFrames, _dgStuckTrips;

    /// <summary>
    /// 往外退的那幾幀，把附近「還沒發現我」的怪的偵測形狀畫成禁區，讓退路不把新的怪拉進來。
    /// </summary>
    /// <remarks>
    /// 目前目標與已在打我的怪不畫 ⇒ 背後被堵時，尋路可以從目標身邊（或身上）繞到另一側的風箏圈。
    /// 不會把人困住：已站在裡面的形狀跳過；腳下有危險區時整幀不加（閃避優先）；風箏圈上取樣找不到乾淨點時
    /// 只保留最近的幾隻；退不動 <see cref="KiteAggroStuckSeconds"/> 秒就關掉 <see cref="KiteAggroSuppressSeconds"/> 秒。
    /// </remarks>
    private void AddKiteAggroAvoidance(Actor target, float ringRadius)
    {
        var radius = Config.AggroAvoidRadius;
        if (!Config.KiteAvoidAggro || radius <= 0f)
            return;

        var now = World.CurrentTime;
        if (now < _kiteAggroSuppressUntil)
        {
            ++_dgSuppressedFrames;
            return;
        }

        var pp = Player.Position;
        var zones = Hints.ForbiddenZones;
        for (var i = 0; i < zones.Count; ++i)
        {
            if (zones[i].shapeDistance(pp) <= 0f)
            {
                ++_dgDodgeFrames;
                _kiteAggroProgress = null;
                return;
            }
        }

        // 依距離由近到遠挑最多 MaxKiteAggroShapes 隻（插入排序，候選很少）
        Span<int> picked = stackalloc int[MaxKiteAggroShapes];
        Span<float> pickedDist = stackalloc float[MaxKiteAggroShapes];
        var pickedCount = 0;
        var targets = Hints.PotentialTargets;
        for (var i = 0; i < targets.Count; ++i)
        {
            var a = targets[i].Actor;
            if (a.AggroPlayer || a.InCombat || a.IsDeadOrDestroyed || a.InstanceID == target.InstanceID || a.InstanceID == Player.TargetID)
                continue;
            if (!Hints.PathfindMapBounds.Contains(a.Position - Hints.PathfindMapCenter))
                continue;
            var d = Player.DistanceToHitbox(a);
            if (pickedCount == MaxKiteAggroShapes && d >= pickedDist[pickedCount - 1])
                continue;
            var j = pickedCount < MaxKiteAggroShapes ? pickedCount++ : pickedCount - 1;
            for (; j > 0 && pickedDist[j - 1] > d; --j)
            {
                picked[j] = picked[j - 1];
                pickedDist[j] = pickedDist[j - 1];
            }
            picked[j] = i;
            pickedDist[j] = d;
        }

        var inPotd = World.DeepDungeon.DungeonId == DeepDungeonState.DungeonType.POTD;
        var count = 0;
        for (var i = 0; i < pickedCount; ++i)
        {
            var a = targets[picked[i]].Actor;
            var shape = Global.DeepDungeon.AutoClear.AggroShape(a, radius, inPotd, out _, out _, out _);
            // 站在裡面的不畫：玩家格落在禁區時尋路整批丟掉目標區、退成純逃離
            if (shape(pp) <= 0f)
            {
                ++_dgInsideSkips;
                continue;
            }
            _kiteShapes[count] = shape;
            _kiteShapeIds[count] = a.InstanceID;
            ++count;
        }

        if (count == 0)
        {
            _kiteAggroProgress = null;
            return;
        }

        // 風箏圈上要至少留一個乾淨點；留不出來就從最遠的開始丟，最壞丟光＝照舊行為
        var keep = count;
        while (keep > 0 && !RingHasClearPoint(target.Position, ringRadius, keep))
            --keep;
        if (keep < count)
        {
            if (keep == 0)
                ++_dgNoClearFrames;
            else
                ++_dgTrimmedFrames;
        }
        if (keep == 0)
        {
            _kiteAggroProgress = null;
            return;
        }

        if (KiteStuck(pp, now, keep))
            return;

        for (var i = 0; i < keep; ++i)
            Hints.AddForbiddenZone(_kiteShapes[i], default, _kiteShapeIds[i]);
        ++_dgAvoidFrames;
        _dgShapes += keep;
    }

    private bool RingHasClearPoint(WPos center, float ringRadius, int shapeCount)
    {
        for (var k = 0; k < KiteRingSamples; ++k)
        {
            var p = center + ringRadius * (k * (360f / KiteRingSamples)).Degrees().ToDirection();
            if (!Hints.PathfindMapBounds.Contains(p - Hints.PathfindMapCenter))
                continue;
            var clear = true;
            for (var i = 0; i < shapeCount && clear; ++i)
                clear = _kiteShapes[i](p) > 0f;
            if (clear)
                return true;
        }
        return false;
    }

    /// <summary>有加形狀卻退不動就讓路：回 true＝這一幀起暫停避讓。讀條期間不累積（站著不動是正常的）。</summary>
    private bool KiteStuck(WPos pp, DateTime now, int shapes)
    {
        if (Player.CastInfo != null || _kiteAggroProgress is not { } s || (pp - s.Pos).LengthSq() >= KiteAggroStuckDistance * KiteAggroStuckDistance)
        {
            _kiteAggroProgress = (pp, now);
            return false;
        }
        if ((now - s.At).TotalSeconds < KiteAggroStuckSeconds)
            return false;
        _kiteAggroProgress = null;
        _kiteAggroSuppressUntil = now.AddSeconds(KiteAggroSuppressSeconds);
        ++_dgStuckTrips;
        Service.Logger.Information($"[DD kite] 仇恨避讓讓路：{KiteAggroStuckSeconds:f1} 秒內只移動了不到 {KiteAggroStuckDistance:f1}y（本幀 {shapes} 個形狀）⇒ 接下來 {KiteAggroSuppressSeconds:f0} 秒不避讓、照舊行為。");
        return true;
    }

    private void FlushKiteAggroDigest()
    {
        var now = World.CurrentTime;
        if (_kiteAggroDigestStart == default)
            _kiteAggroDigestStart = now;
        if ((now - _kiteAggroDigestStart).TotalSeconds < 60d)
            return;
        if (_dgAvoidFrames + _dgTrimmedFrames + _dgNoClearFrames + _dgInsideSkips + _dgDodgeFrames + _dgSuppressedFrames + _dgStuckTrips > 0)
        {
            Service.Logger.Information($"[DD kite] 仇恨避讓彙總（{(now - _kiteAggroDigestStart).TotalSeconds:f0} 秒）：避開其他怪仇恨範圍 {_dgAvoidFrames} 幀（平均 {(_dgAvoidFrames > 0 ? (float)_dgShapes / _dgAvoidFrames : 0f):f1} 隻）"
                + $"、退路被包住只避最近幾隻 {_dgTrimmedFrames} 幀、找不到乾淨退路（不避讓）{_dgNoClearFrames} 幀"
                + $"、已站在其範圍內而跳過 {_dgInsideSkips} 次、閃避優先暫停 {_dgDodgeFrames} 幀、卡住讓路 {_dgStuckTrips} 次（讓路中 {_dgSuppressedFrames} 幀）。");
        }
        _kiteAggroDigestStart = now;
        _dgAvoidFrames = _dgShapes = _dgTrimmedFrames = _dgNoClearFrames = _dgInsideSkips = _dgDodgeFrames = _dgSuppressedFrames = _dgStuckTrips = 0;
    }

    #endregion

    /// <summary>
    /// 排一個衝刺。<b>解不開就靜默跳過</b>（等級不足、副本禁用等），不擲例外也不留下半套狀態。
    /// </summary>
    private void TrySprint()
    {
        // 🔴 一定要用 IDSprint（Spell/3）不能用 IDGeneralSprint（General/4）：
        //    只有前者經 ClassShared 的 RegisterSpell 註冊成 ActionDefinition，
        //    後者在 ActionDefinitions 裡查不到 ⇒ ActionUnlocked() 會回 false，
        //    整個功能靜默不作用（回 0 而不是報錯）。General/4 只是 ActionManagerEx
        //    在執行期把它翻譯成 Spell/3 用的別名。
        var sprint = ActionDefinitions.IDSprint;
        if (!ActionUnlocked(sprint))
            return;
        // 已經在衝刺就不要重複排
        if (Player.FindStatus((uint)ClassShared.SID.Sprint) != null)
            return;
        // Low＝有空檔才用，不排擠任何輸出技能
        Hints.ActionsToExecute.Push(sprint, Player, ActionQueue.Priority.Low);
    }

    private void DoTransformActions(StrategyValues strategy, Actor? primaryTarget, Transformation t)
    {
        if (primaryTarget == null)
            return;

        Func<WPos, float> goal;
        ActionID attack;
        int numTargets;
        var castTime = 0f;

        switch (t)
        {
            case Transformation.Manticore:
                goal = Hints.GoalSingleTarget(primaryTarget, 3f);
                numTargets = 1;
                attack = ActionID.MakeSpell(Roleplay.AID.Pummel);
                break;
            case Transformation.Succubus:
                goal = Hints.GoalSingleTarget(primaryTarget, 25f);
                numTargets = Hints.NumPriorityTargetsInAOECircle(primaryTarget.Position, 5f);
                attack = ActionID.MakeSpell(Roleplay.AID.VoidFireII);
                castTime = 2.5f;
                break;
            case Transformation.Kuribu:
                // heavenly judge is ground targeted
                goal = Hints.GoalSingleTarget(primaryTarget.Position, 25f);
                numTargets = Hints.NumPriorityTargetsInAOECircle(primaryTarget.Position, 6f);
                attack = ActionID.MakeSpell(Roleplay.AID.HeavenlyJudge);
                castTime = 2.5f;
                break;
            case Transformation.Dreadnaught:
                goal = Hints.GoalSingleTarget(primaryTarget, 3f);
                numTargets = 1;
                attack = ActionID.MakeSpell(Roleplay.AID.Rotosmash);
                break;
            default:
                return;
        }

        if (numTargets == 0)
            return;

        Hints.GoalZones.Add(goal);
        Hints.ActionsToExecute.Push(attack, primaryTarget, ActionQueue.Priority.High, targetPos: primaryTarget.PosRot.XYZ(), castTime: castTime - 0.5f);
    }

    private bool ShouldPotion(StrategyValues strategy)
    {
        // 直接 foreach（結構列舉器）：LINQ Any 走介面會每幀裝箱一個列舉器
        foreach (var w in World.Actors)
            if (w.OID == (uint)OID.Unei)
                return false;
        if (!strategy.Enabled(Track.Potion))
            return false;

        var ratio = Player.ClassCategory is ClassCategory.Tank ? 0.4f : 0.6f;
        return Player.PendingHPRatio < ratio && Player.FindStatus(648u) == null && Player.InCombat;
    }
}
