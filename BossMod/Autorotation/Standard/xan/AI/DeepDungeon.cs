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
            Hints.GoalZones.Add(Hints.GoalSingleTarget(target, 3, 0.1f));

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

        if (regenAction != default && ShouldPotion(strategy))
            Hints.ActionsToExecute.Push(regenAction, Player, ActionQueue.Priority.Medium);

        if (potAction != default && Player.HPRatio <= 0.3f)
            Hints.ActionsToExecute.Push(potAction, Player, ActionQueue.Priority.VeryHigh);
    }

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

        if (Player.DistanceToHitbox(source) <= RangedAttackDistance)
            return;

        var oid = source.OID;
        if (RangedAttackerOIDs.Contains(oid))
            return;

        var n = RangedAttackObservations.GetValueOrDefault(oid) + 1;
        RangedAttackObservations[oid] = n;
        if (n < RangedAttackSamples)
            return;

        RangedAttackerOIDs.Add(oid);
        // 使用者跑 LogLevel 2，要他回報得到的等級才有意義；一個 OID 只印一次
        Service.Logger.Information($"[DD kite] OID {oid:X} 在近戰距離外仍以自動攻擊命中玩家 {n} 次，本次探索對它停用風箏。");
    }

    private void ResetObservationsOnZoneChange()
    {
        if (_observationZone == World.CurrentZone)
            return;
        _observationZone = World.CurrentZone;
        RangedAttackerOIDs.Clear();
        RangedAttackObservations.Clear();
    }

    /// <summary>
    /// 這個 action 是不是自動攻擊。
    /// </summary>
    /// <remarks>
    /// 📌 判準是 <c>Action.ActionCategory == 1</c>（台服 ActionCategory 表 row 1 ＝「自動攻擊」，
    /// 對照 7＝攻擊、8＝射擊，離線查 exd 驗過）。用資料表而不是寫死 id，
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

    private void SetupKiteZone(StrategyValues strategy, Actor? primaryTarget)
    {
        if (!IsRanged || primaryTarget == null || !Player.InCombat || !strategy.Enabled(Track.Kite))
            return;

        // wew
        if (NoMeleeAutos.Contains(primaryTarget.OID))
            return;

        // 執行期觀測到這隻怪不必追上你就能打你 ⇒ 拉開距離換不到任何好處，別白跑
        if (RangedAttackerOIDs.Contains(primaryTarget.OID))
            return;

        // assume we don't need to kite if mob is busy casting (TODO: some mob spells can be cast while moving, maybe there's a column in sheets for it)
        if (primaryTarget.CastInfo != null)
            return;

        var maxKite = Config.KiteMinDistance;
        // 防呆：外圈永遠要比內圈大，否則甜甜圈是空的、風箏靜默失效
        var maxRange = Math.Max(Config.KiteMaxDistance, maxKite + 1f);

        var primaryPos = primaryTarget.Position;
        var total = maxRange + Player.HitboxRadius + primaryTarget.HitboxRadius;
        var totalKite = maxKite + Player.HitboxRadius + primaryTarget.HitboxRadius;
        var goalFactor = Config.KiteWeight;
        Hints.GoalZones.Add(pos =>
        {
            var dist = (pos - primaryPos).Length();
            return dist <= total && dist >= totalKite ? goalFactor : default;
        });

        // 告訴 AI「這一幀往後退是刻意的」，免得閃避走位的『別後退』懲罰把上面那個 0.05 碾平
        if (Config.KiteAllowRetreatWhileDodging)
            Hints.WantKiting = true;

        // 目前還在圈內＝正在往外退（而不是已經站好位）
        var retreating = Player.DistanceToHitbox(primaryTarget) < maxKite;
        if (!retreating)
            return;

        // B②：施法職業在退避途中只出瞬發。讀條時 AI 完全不移動
        // （NavigationDecision 跳過目標區、AIController 禁移動），不設這個的話風箏窗口只剩 GCD 間隙。
        if (OptedIn(strategy, Track.KiteInstantOnly))
            Hints.MaxCastTime = 0;

        // B③：退避途中允許衝刺。目的是多拉一點喘息距離、少挨幾下，不是逃脫。
        if (OptedIn(strategy, Track.KiteSprint))
            TrySprint();
    }

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
        if (World.Actors.Any(w => w.OID == (uint)OID.Unei) || !strategy.Enabled(Track.Potion))
            return false;

        var ratio = Player.ClassCategory is ClassCategory.Tank ? 0.4f : 0.6f;
        return Player.PendingHPRatio < ratio && Player.FindStatus(648u) == null && Player.InCombat;
    }
}
