using BossMod.Autorotation.xan;

namespace BossMod.Autorotation.MiscAI;

/// <summary>
/// 依 <see cref="AIHints.PredictedDamage"/> 預先按下減傷／自保 oGCD 的模組。
/// </summary>
/// <remarks>
/// <para>
/// 存在的理由：使用者把「出招」交給外部循環外掛（WrathCombo 之類），BMR 只負責走位。
/// 但「知道有傷害要來了」這件事只有 BMR 知道——它是唯一有 boss 模組時間軸的一方，
/// 而外部循環外掛看不到 <c>PredictedDamage</c>。於是常見的結果是減傷技能整場沒開過。
/// 這個模組把兩邊接起來：<b>只按減傷／自保，不碰輸出、不碰目標</b>。
/// </para>
/// <para>
/// 🔑 <b>與外部循環外掛並行的機理</b>：<c>Hints.ActionsToExecute</c> 由
/// <c>ActionManagerEx.FinishActionGather()</c> → <c>ActionManagerEx.Update</c> 每幀無條件消費
/// （<c>Plugin.DrawUI</c> 固定呼叫，不看 AI 開關、也不看外部外掛在做什麼）。
/// 因此不需要走 WrathCombo 的 ActionRequest IPC——直推佇列較簡單也較不會壞：
/// IPC 路線多一個版本相依、對方改簽名就靜默失效，而這條路只依賴 BMR 自己的程式碼。
/// </para>
/// <para>
/// 🔴 <b>已知閘門（不是這個模組能解的）</b>：BMR 的 AI 模式在
/// <c>AIBehaviour.Execute</c> 是 <c>autorot.Preset = target.Target != null ? preset : null</c>——
/// <b>沒有目標時整個 preset 管線關掉</b>，本模組也就不會執行。對「團刀／死刑」而言這通常不成立
/// （有 boss 就有目標），但趕路、目標死亡的空窗期確實不會有減傷。
/// 手動掛 preset（<c>/bmr ar set</c>）的路徑沒有這個閘門。
/// </para>
/// <para>
/// 🔴 <b>安全設計：最壞情況必須是「不按技能」，不能是「亂按技能」。</b>每一次推送都同時滿足：
/// ①<c>ActionDefinition.IsUnlocked</c>（職業對、等級夠）②冷卻真的好了（不推 CD 中的技能，
/// 否則會在 <c>ActionQueue.FindBest</c> 裡變成 deadline 卡住別的動作）③同一格減傷的效果
/// 沒有正在罩著這次傷害 ④優先級一律 &lt; <c>High</c>，永遠不會延遲 GCD ⑤只在戰鬥中。
/// 完全不寫 <c>Hints.ForcedTarget</c>、不推任何 GCD。
/// </para>
/// </remarks>
public sealed class PredictiveMitigation(RotationModuleManager manager, Actor player) : AIBase(manager, player)
{
    public enum Track { PartyMit, SelfMit, Emergency, RaidwideLead, TankbusterLead, EmergencyHP }

    public enum SelfMitStrategy
    {
        TankbusterOnly,
        Raidwides,
        Disabled
    }

    // 推送優先級：全部低於 ActionQueue.Priority.High(4000)，所以永遠不會為了插入而延遲 GCD。
    private const float PrioParty = ActionQueue.Priority.Medium;   // 3000：團減值得搶第一個 ogcd 空檔
    private const float PrioSelf = ActionQueue.Priority.Low;       // 2000：自身減傷不必搶
    private const float PrioEmergency = ActionQueue.Priority.Medium;

    // 冷卻視為「好了」的容差；FindBest 自己用 0.05f，這裡放寬一點避免每幀邊界抖動。
    private const float ReadyEpsilon = 0.1f;

    // 太舊的預測不理會（正常情況下 PredictedDamage 每幀由 boss 模組重建，不會有殘留，
    // 但「活化時間已過」的項目若照推會變成傷害結算後才開減傷）。
    private const float StaleCutoff = -1f;

    public static RotationModuleDefinition Definition()
    {
        var def = new RotationModuleDefinition(
            "Predictive mitigation",
            "Presses mitigation and self-preservation oGCDs ahead of predicted raidwides and tankbusters. Never presses GCDs and never changes your target, so it can run alongside an external rotation plugin.",
            "AI",
            "ffxiv-tc-port",
            RotationModuleQuality.Basic,
            new BitMask(~0ul),
            1000);

        def.AbilityTrack(Track.PartyMit, "PartyMit", "Party mitigation", 100)
            .AddAssociatedActions(ClassShared.AID.Reprisal, ClassShared.AID.Feint, ClassShared.AID.Addle);

        def.Define(Track.SelfMit).As<SelfMitStrategy>("SelfMit", "Personal mitigation", 90)
            .AddOption(SelfMitStrategy.TankbusterOnly, "Only when a tankbuster targets me")
            .AddOption(SelfMitStrategy.Raidwides, "Also before raidwides")
            .AddOption(SelfMitStrategy.Disabled, "Do not use")
            .AddAssociatedActions(ClassShared.AID.Rampart);

        def.AbilityTrack(Track.Emergency, "Emergency", "Emergency self-preservation", 80)
            .AddAssociatedActions(ClassShared.AID.SecondWind);

        // ⚠️ StrategyConfigFloat.CreateEmpty() 回的是 MinValue ⇒ 這三條滑桿的「預設值就是 MinValue」。
        //    所以最小值刻意設成我們要的預設，範圍只往「更積極」的方向開。
        def.DefineFloat(Track.RaidwideLead, "Raidwide lead time (s)", 5, 20, 10);
        def.DefineFloat(Track.TankbusterLead, "Tankbuster lead time (s)", 4, 15, 9);
        def.DefineFloat(Track.EmergencyHP, "Emergency HP threshold (%)", 30, 90, 8);

        return def;
    }

    private ActionID _lastLogged;
    private DateTime _lastLogTime;

    public override string DescribeState()
    {
        var (rw, tb) = Imminent();
        var rwText = rw < float.MaxValue ? rw.ToString("f1") : "-";
        var tbText = tb < float.MaxValue ? tb.ToString("f1") : "-";
        return $"RW {rwText} / TB {tbText}";
    }

    public override void Execute(StrategyValues strategy, Actor? primaryTarget, float estimatedAnimLockDelay, bool isMoving)
    {
        if (Player.IsDeadOrDestroyed || !Player.InCombat)
            return;

        var (raidwideIn, tankbusterIn) = Imminent();

        var raidwideLead = strategy.GetFloat(Track.RaidwideLead);
        var tankbusterLead = strategy.GetFloat(Track.TankbusterLead);

        var raidwideImminent = raidwideIn <= raidwideLead;
        var tankbusterImminent = tankbusterIn <= tankbusterLead;

        if (raidwideImminent && strategy.Enabled(Track.PartyMit))
            ExecutePartyMit(raidwideIn, primaryTarget);

        var selfStrategy = strategy.Option(Track.SelfMit).As<SelfMitStrategy>();
        if (selfStrategy != SelfMitStrategy.Disabled)
        {
            if (tankbusterImminent)
                ExecuteSelfMit(tankbusterIn, PrioSelf);
            else if (selfStrategy == SelfMitStrategy.Raidwides && raidwideImminent)
                ExecuteSelfMit(raidwideIn, PrioSelf);
        }

        if (strategy.Enabled(Track.Emergency) && Player.PendingHPRatio < strategy.GetFloat(Track.EmergencyHP) * 0.01f)
        {
            ExecuteSelfHeal();
            ExecuteSelfMit(0, PrioEmergency);
        }
    }

    /// <summary>本幀最近的（團刀秒數, 指向自己的死刑秒數）；沒有就是 <c>float.MaxValue</c>。</summary>
    private (float Raidwide, float Tankbuster) Imminent()
    {
        var now = World.CurrentTime;
        var rw = float.MaxValue;
        var tb = float.MaxValue;

        foreach (var t in Raidwides)
        {
            var dt = (float)(t - now).TotalSeconds;
            if (dt > StaleCutoff && dt < rw)
                rw = dt;
        }

        foreach (var (actor, t) in Tankbusters)
        {
            if (actor != Player)
                continue;
            var dt = (float)(t - now).TotalSeconds;
            if (dt > StaleCutoff && dt < tb)
                tb = dt;
        }

        return (rw, tb);
    }

    #region 推送與判定

    /// <summary>
    /// 嘗試推一格減傷。
    /// </summary>
    /// <param name="action">技能。</param>
    /// <param name="duration">效果持續時間（秒）。⚠️ 這是<b>寫死的估計值</b>，遊戲資料表沒有可靠來源
    /// （TankAI 同樣是寫死的，見 <c>xan/AI/Tank.cs</c> 的註解）。估錯只會影響「這格是不是已經罩著了」
    /// 的判斷——太短＝可能提早補第二格、太長＝可能少開一格，<b>不會造成亂放技能</b>。</param>
    /// <param name="timeToHit">距離這次傷害還有幾秒；效果要撐過這個時間才算「已經罩著」。</param>
    /// <param name="priority">佇列優先級。</param>
    /// <param name="target">目標；預設是自己。null 一律不推。</param>
    /// <returns>true＝這一格已經解決（推出去了，或效果還罩著），呼叫端不要再往下找備援。</returns>
    private bool TryMit(ActionID action, float duration, float timeToHit, float priority, Actor? target = null)
    {
        var def = ActionDefinitions.Instance[action];
        if (def == null || !def.IsUnlocked(World, Player))
            return false;

        var cd = def.ReadyIn(World.Client.Cooldowns, World.Client.DutyActions);

        // 效果殘留 = 持續時間 -（已經過的冷卻）= duration -(maxCD - cd)。沒用過時 cd=0 ⇒ 必為 <= 0。
        // 🔑 這個推法不需要狀態 id，也就不會因為狀態 id 在台服對不上而靜默失效（沿用 TankAI 的做法）。
        var effectRemaining = duration + cd - def.Cooldown;
        if (effectRemaining > timeToHit)
            return true;

        if (cd > ReadyEpsilon)
            return false;

        var tgt = target ?? Player;
        Hints.ActionsToExecute.Push(action, tgt, priority);
        LogPush(action);
        return true;
    }

    private bool TryMit<AID>(AID aid, float duration, float timeToHit, float priority, Actor? target = null) where AID : Enum
        => TryMit(ActionID.MakeSpell(aid), duration, timeToHit, priority, target);

    /// <summary>
    /// 升級技的挑選：解鎖了就用升級版，否則用原版。
    /// ⚠️ 刻意不用 <c>BestActionUnlocked</c>——那支的參數是 <c>params AID[]</c>，
    /// 每次呼叫都會配一個陣列，而這裡位於每幀都可能走到的路徑上。
    /// </summary>
    private AID Upgrade<AID>(AID upgraded, AID basic) where AID : struct, Enum
        => ActionUnlocked(upgraded) ? upgraded : basic;

    /// <summary>
    /// 要使用者回報時看得到的診斷。刻意寫 <c>Information</c>（使用者跑 LogLevel 2，Debug/Verbose 收不到），
    /// 並且對「同一個技能連續推」節流，否則每幀一行會把 log 灌爆。
    /// </summary>
    private void LogPush(ActionID action)
    {
        var now = World.CurrentTime;
        if (action == _lastLogged && (now - _lastLogTime).TotalSeconds < 10)
            return;
        _lastLogged = action;
        _lastLogTime = now;
        Service.Logger.Information($"[PredMit] 推送減傷 {action}（HP {Player.PendingHPRatio:P0}）。");
    }

    #endregion

    #region 團體減傷

    private void ExecutePartyMit(float timeToHit, Actor? primaryTarget)
    {
        switch (Player.Class)
        {
            case Class.GLA:
            case Class.PLD:
                TryMit(BossMod.PLD.AID.DivineVeil, 30, timeToHit, PrioParty);
                TryReprisal(timeToHit, primaryTarget);
                break;
            case Class.MRD:
            case Class.WAR:
                TryMit(BossMod.WAR.AID.ShakeItOff, 15, timeToHit, PrioParty);
                TryReprisal(timeToHit, primaryTarget);
                break;
            case Class.DRK:
                TryMit(BossMod.DRK.AID.DarkMissionary, 15, timeToHit, PrioParty);
                TryReprisal(timeToHit, primaryTarget);
                break;
            case Class.GNB:
                TryMit(BossMod.GNB.AID.HeartOfLight, 15, timeToHit, PrioParty);
                TryReprisal(timeToHit, primaryTarget);
                break;

            // 近戰：牽制（對敵單體，射程 10）
            case Class.PGL:
            case Class.MNK:
            case Class.LNC:
            case Class.DRG:
            case Class.ROG:
            case Class.NIN:
            case Class.SAM:
            case Class.RPR:
            case Class.VPR:
                TryEnemyDebuff(ClassShared.AID.Feint, 10, timeToHit, primaryTarget);
                break;

            // 遠敏：各自的團減（皆為對自身施放的 30m 範圍增益）
            case Class.ARC:
            case Class.BRD:
                TryMit(BossMod.BRD.AID.Troubadour, 15, timeToHit, PrioParty);
                break;
            case Class.MCH:
                TryMit(BossMod.MCH.AID.Tactician, 15, timeToHit, PrioParty);
                break;
            case Class.DNC:
                TryMit(BossMod.DNC.AID.ShieldSamba, 15, timeToHit, PrioParty);
                break;

            // 法系：昏亂（對敵單體，射程 25）；赤魔另有抗死
            case Class.THM:
            case Class.BLM:
            case Class.ACN:
            case Class.SMN:
            case Class.BLU:
            case Class.PCT:
                TryEnemyDebuff(ClassShared.AID.Addle, 10, timeToHit, primaryTarget);
                break;
            case Class.RDM:
                TryMit(BossMod.RDM.AID.MagickBarrier, 10, timeToHit, PrioParty);
                TryEnemyDebuff(ClassShared.AID.Addle, 10, timeToHit, primaryTarget);
                break;

            // 治療：只挑「按下去就生效、不打斷走位、不需要寵物」的那一個。
            // ⚠️ 刻意不碰占星的命運之輪（詠唱式、會把人釘在原地，與走位模組直接打架），
            //    也不碰學者的異想的幻光（需要仙女在場，沒仙女時是靜默失效）。
            case Class.CNJ:
            case Class.WHM:
                TryMit(BossMod.WHM.AID.Temperance, 20, timeToHit, PrioParty);
                break;
            case Class.SCH:
                TryMit(BossMod.SCH.AID.Expedient, 20, timeToHit, PrioParty);
                break;
            case Class.SGE:
                TryMit(BossMod.SGE.AID.Kerachole, 15, timeToHit, PrioParty);
                break;
        }
    }

    /// <summary>
    /// 雪仇：以自己為中心的 5m 範圍減益，射程欄位是 0 ⇒ <c>ActionQueue</c> 不會幫我們做距離檢查，
    /// 沒有敵人在範圍內照推就是白白丟掉一次 60 秒 CD。所以這裡自己檢查距離（做法同 TankAI）。
    /// </summary>
    private void TryReprisal(float timeToHit, Actor? primaryTarget)
    {
        var enemy = primaryTarget ?? Bossmods.ActiveModule?.PrimaryActor;
        if (enemy == null || enemy.IsAlly || enemy.IsDeadOrDestroyed || Player.DistanceToHitbox(enemy) > 5)
            return;
        TryMit(ClassShared.AID.Reprisal, 10, timeToHit, PrioParty);
    }

    /// <summary>
    /// 牽制／昏亂這類「掛在敵人身上」的團減。
    /// 🔴 <b>不做目標選取</b>：只用呼叫端已經解析好的主要目標（<c>Hints.ForcedTarget</c> 或玩家自己選的），
    /// 沒有目標就不推。射程由 <c>ActionQueue.CanExecute</c> 依技能定義檢查（這兩招的 Range &gt; 0）。
    /// </summary>
    private void TryEnemyDebuff(ClassShared.AID aid, float duration, float timeToHit, Actor? primaryTarget)
    {
        if (primaryTarget == null || primaryTarget.IsAlly || primaryTarget.IsDeadOrDestroyed)
            return;
        TryMit(aid, duration, timeToHit, PrioParty, primaryTarget);
    }

    #endregion

    #region 自身減傷

    /// <summary>
    /// 自身減傷鏈：由「效果最強／最長」往下找，<b>只會成立一格</b>——
    /// <see cref="TryMit"/> 回 true 就中斷，所以同一次傷害不會連開三個減傷。
    /// </summary>
    private void ExecuteSelfMit(float timeToHit, float priority)
    {
        switch (Player.Class)
        {
            case Class.GLA:
            case Class.PLD:
                _ = TryMit(Upgrade(BossMod.PLD.AID.Guardian, BossMod.PLD.AID.Sentinel), 15, timeToHit, priority)
                    || TryMit(ClassShared.AID.Rampart, 20, timeToHit, priority)
                    || TryMit(BossMod.PLD.AID.Bulwark, 10, timeToHit, priority);
                break;
            case Class.MRD:
            case Class.WAR:
                _ = TryMit(Upgrade(BossMod.WAR.AID.Damnation, BossMod.WAR.AID.Vengeance), 15, timeToHit, priority)
                    || TryMit(ClassShared.AID.Rampart, 20, timeToHit, priority)
                    || TryMit(Upgrade(BossMod.WAR.AID.Bloodwhetting, BossMod.WAR.AID.RawIntuition), 8, timeToHit, priority);
                break;
            case Class.DRK:
                _ = TryMit(Upgrade(BossMod.DRK.AID.ShadowedVigil, BossMod.DRK.AID.ShadowWall), 15, timeToHit, priority)
                    || TryMit(ClassShared.AID.Rampart, 20, timeToHit, priority)
                    || TryMit(BossMod.DRK.AID.DarkMind, 10, timeToHit, priority);
                break;
            case Class.GNB:
                _ = TryMit(Upgrade(BossMod.GNB.AID.GreatNebula, BossMod.GNB.AID.Nebula), 15, timeToHit, priority)
                    || TryMit(ClassShared.AID.Rampart, 20, timeToHit, priority)
                    || TryMit(BossMod.GNB.AID.Camouflage, 20, timeToHit, priority)
                    || TryMit(Upgrade(BossMod.GNB.AID.HeartOfCorundum, BossMod.GNB.AID.HeartOfStone), 8, timeToHit, priority);
                break;

            case Class.PGL:
            case Class.MNK:
                TryMit(BossMod.MNK.AID.RiddleOfEarth, 10, timeToHit, priority);
                break;
            case Class.ROG:
            case Class.NIN:
                TryMit(BossMod.NIN.AID.ShadeShift, 20, timeToHit, priority);
                break;
            case Class.SAM:
                TryMit(Upgrade(BossMod.SAM.AID.Tengentsu, BossMod.SAM.AID.ThirdEye), 4, timeToHit, priority);
                break;
            case Class.RPR:
                TryMit(BossMod.RPR.AID.ArcaneCrest, 5, timeToHit, priority);
                break;
            case Class.THM:
            case Class.BLM:
                TryMit(BossMod.BLM.AID.Manaward, 20, timeToHit, priority);
                break;
            case Class.PCT:
                TryMit(BossMod.PCT.AID.TemperaCoat, 10, timeToHit, priority);
                break;

            // 其餘職業（龍騎、毒蛇、詩人、機工、舞者、召喚、赤魔、治療四職）在目前版本
            // 沒有「單體、無資源消耗、無副作用」的自身減傷可推 ⇒ 什麼都不做（fail-safe）。
            default:
                break;
        }
    }

    /// <summary>低血自保：只推「按下去就回血、不占 GCD、不需要目標」的技能。</summary>
    private void ExecuteSelfHeal()
    {
        switch (Player.Class)
        {
            case Class.MRD:
            case Class.WAR:
                TryMit(BossMod.WAR.AID.ThrillOfBattle, 10, 0, PrioEmergency);
                TryMit(BossMod.WAR.AID.Equilibrium, 15, 0, PrioEmergency);
                break;
            case Class.GNB:
                TryMit(BossMod.GNB.AID.Aurora, 18, 0, PrioEmergency);
                break;
            default:
                // 內丹是物理職的共通技（坦克沒有；ActionDefinition.IsUnlocked 會擋掉不該有的職業，
                // 所以這裡不需要再列一次職業清單）。
                TryMit(ClassShared.AID.SecondWind, 0, 0, PrioEmergency);
                break;
        }
    }

    #endregion
}
