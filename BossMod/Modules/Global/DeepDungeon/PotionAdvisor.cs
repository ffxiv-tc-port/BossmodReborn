using FFXIVClientStructs.FFXIV.Client.Game;

namespace BossMod.Global.DeepDungeon;

// 本檔假設與契約：
// - 全部在主執行緒（CalculateAIHints 的呼叫鏈）上跑；跨幀只存數值，不存 Actor 或原生指標。
// - 判斷本身在 DeepDungeonPotionAdvisor.Decide（純函式）；這裡只收集輸入、推動作、寫 log。
// - 背包存量（GetInventoryItemCount）只在「想喝」的那幀才查。
public abstract partial class AutoClear
{
    /// <summary>判斷模組最後一次執行的時間；xan 的 DeepDungeonAI 看到它還新鮮就不再自己推秘藥。</summary>
    internal static DateTime PotionAdvisorActiveAt;

    private enum HPGate : byte { None, Travel, Bronze, Silver }

    // 本幀各血量閘門寫進來，下一幀 UpdatePotions 讀（它排在閘門判斷之前）
    private HPGate _hpGateBuilding;
    private int _hpGatePctBuilding;

    private void MarkHPGate(HPGate gate, int pct)
    {
        if (_hpGateBuilding == HPGate.None || pct > _hpGatePctBuilding)
            (_hpGateBuilding, _hpGatePctBuilding) = (gate, pct);
    }

    /// <summary>
    /// 治療劑候選與它們的回復參數（百分比、上限），NQ／HQ 分開；參數讀 ItemAction 表，第一次用到才讀。
    /// </summary>
    /// <remarks>順序只在回復量相同時當平手規則。讀不到參數的那瓶回復量算 0，仍可當最後的候選。</remarks>
    private static readonly ActionID[] HealPotionCandidates = [
        ActionDefinitions.IDPotionMax,
        new(ActionType.Item, 13637),
        ActionDefinitions.IDPotionHyper,
        new(ActionType.Item, 38956),
        ActionDefinitions.IDPotionSuper,
        new(ActionType.Item, 23167),
    ];
    private static (float Pct, float Cap)[]? _healPotionParams;

    private static (float Pct, float Cap)[] HealPotionParams()
    {
        if (_healPotionParams != null)
            return _healPotionParams;
        var res = new (float, float)[HealPotionCandidates.Length];
        for (var i = 0; i < res.Length; ++i)
        {
            var aid = HealPotionCandidates[i];
            var action = Service.LuminaRow<Lumina.Excel.Sheets.Item>(aid.ID % 1000000u)?.ItemAction.ValueNullable;
            if (action is not { } a)
                continue;
            var hq = aid.ID >= 1000000u;
            res[i] = hq ? (a.DataHQ[0], a.DataHQ[1]) : (a.Data[0], a.Data[1]);
        }
        return _healPotionParams = res;
    }

    private static float HealAmount((float Pct, float Cap) p, uint maxHP) => Math.Min(p.Pct * 0.01f * maxHP, p.Cap);

    private const uint UneiOID = 0x3E1A;
    private const uint RegenStatusID = 648;

    private static ActionID RegenPotionFor(DeepDungeonState.DungeonType d) => d switch
    {
        DeepDungeonState.DungeonType.POTD => ActionDefinitions.IDPotionSustaining,
        DeepDungeonState.DungeonType.HOH => ActionDefinitions.IDPotionEmpyrean,
        DeepDungeonState.DungeonType.EO => ActionDefinitions.IDPotionOrthos,
        _ => default,
    };

    #region 近 10 秒實際受傷

    private const int DtpsBuckets = 20; // 每格 0.5 秒
    private static readonly long DtpsBucketTicks = TimeSpan.FromSeconds(0.5).Ticks;
    private readonly double[] _dtpsBucket = new double[DtpsBuckets];
    private long _dtpsLastBucket = -1;
    private uint _dtpsPrevHP, _dtpsPrevMax;

    /// <summary>交戰中每幀的 HP 下降量（最大 HP 比例）記進環形緩衝；最大 HP 變動那幀不算，脫戰不累積。</summary>
    private void UpdateObservedDamage(Actor player)
    {
        var bucket = World.CurrentTime.Ticks / DtpsBucketTicks;
        // 中間有斷（模組剛開、剛被打開）時上一份 HP 不可信，那一幀不算
        var contiguous = _dtpsLastBucket >= 0 && bucket >= _dtpsLastBucket && bucket - _dtpsLastBucket <= 1;
        if (_dtpsLastBucket < 0 || bucket < _dtpsLastBucket || bucket - _dtpsLastBucket >= DtpsBuckets)
            Array.Clear(_dtpsBucket);
        else
            for (var b = _dtpsLastBucket + 1; b <= bucket; ++b)
                _dtpsBucket[b % DtpsBuckets] = 0;
        _dtpsLastBucket = bucket;

        var hp = player.HPMP.CurHP;
        var max = player.HPMP.MaxHP;
        if (contiguous && player.InCombat && !player.IsDead && max > 0 && max == _dtpsPrevMax && hp < _dtpsPrevHP)
            _dtpsBucket[bucket % DtpsBuckets] += (double)(_dtpsPrevHP - hp) / max;
        (_dtpsPrevHP, _dtpsPrevMax) = (hp, max);
    }

    /// <summary>max(近 5 秒/5, 近 10 秒/10)，單位：最大 HP 比例／秒。</summary>
    private double ObservedDtps()
    {
        double sum5 = 0, sum10 = 0;
        for (var i = 0; i < DtpsBuckets; ++i)
        {
            var v = _dtpsBucket[(_dtpsLastBucket - i + DtpsBuckets * 2) % DtpsBuckets];
            sum10 += v;
            if (i < DtpsBuckets / 2)
                sum5 += v;
        }
        return Math.Max(sum5 / 5.0, sum10 / 10.0);
    }

    #endregion

    #region 每層摘要

    private byte _potFloor;
    private DeepDungeonState.DungeonType _potDungeon;
    private int _potRegenCount, _potHealCount;
    private float _potMinHP = 1f;
    private double _potCombatSeconds;

    private void TrackPotionFloor(Actor player)
    {
        var floor = Palace.Floor;
        if (floor != _potFloor)
        {
            FlushPotionSummary();
            (_potFloor, _potDungeon) = (floor, Palace.DungeonId);
            (_potRegenCount, _potHealCount, _potMinHP, _potCombatSeconds) = (0, 0, 1f, 0);
        }
        if (floor == 0)
            return;
        if (player.HPMP.MaxHP > 0)
            _potMinHP = Math.Min(_potMinHP, (float)player.HPMP.CurHP / player.HPMP.MaxHP);
        if (player.InCombat)
            _potCombatSeconds += World.Frame.Duration;
    }

    /// <summary>上線後驗收的主要依據：換層（或離開）時一行。</summary>
    private void FlushPotionSummary()
    {
        if (_potFloor == 0)
            return;
        var regen = RegenPotionFor(_potDungeon);
        var regenName = regen != default ? regen.Name() : "秘藥";
        Service.Logger.Information($"[DD] 藥水摘要 樓層 {_potFloor}：{regenName} {_potRegenCount}、治療劑 {_potHealCount}、最低 HP {_potMinHP * 100f:f0}%、交戰 {_potCombatSeconds:f0} 秒");
        _potFloor = 0;
    }

    #endregion

    private bool _regenPushing, _healPushing, _oocHealSkipLogged;
    private DateTime _potionBlockedLogAfter;

    /// <summary>
    /// 每幀的喝藥入口。判斷模組關著時完全走舊的保命藥水（xan 那邊的秘藥路徑也照舊）。
    /// </summary>
    private unsafe void UpdatePotions(Actor player, AIHints hints)
    {
        var gate = _hpGateBuilding;
        var gatePct = _hpGatePctBuilding;
        (_hpGateBuilding, _hpGatePctBuilding) = (HPGate.None, 0);

        if (!Config.PotionAdvisor)
        {
            UpdateEmergencyPotion(player, hints);
            return;
        }
        PotionAdvisorActiveAt = World.CurrentTime;
        UpdateObservedDamage(player);
        TrackPotionFloor(player);

        var maxHP = player.HPMP.MaxHP;
        if (maxHP == 0 || player.IsDead)
        {
            _regenPushing = _healPushing = false;
            return;
        }

        var engaged = 0;
        var unei = false;
        foreach (var a in World.Actors)
        {
            if (a.OID == UneiOID)
                unei = true;
            else if (a.Type == ActorType.Enemy && !a.IsDead && a.InCombat && a.TargetID == player.InstanceID)
                ++engaged;
        }

        string? blocked = player.FindStatus(TransfigurationSID) != null ? "變身中"
            : player.FindStatus((uint)SID.ItemPenalty) != null ? "身上有禁止使用道具"
            : null;
        var regenAid = RegenPotionFor(Palace.DungeonId);
        var hot = player.FindStatus(RegenStatusID);
        var s = new DeepDungeonPotionAdvisor.Snapshot
        {
            HP = player.PendingHPRatio,
            InCombat = player.InCombat,
            Engaged = engaged,
            ObservedDtps = ObservedDtps(),
            PerMobDtps = DeepDungeonPotionAdvisor.PerMobDtps((int)Palace.DungeonId, Palace.Floor),
            HotActive = hot != null,
            HotTicksLeft = hot is { } h ? DeepDungeonPotionAdvisor.HotTicksLeft((h.ExpireAt - World.CurrentTime).TotalSeconds) : 0,
            Usable = blocked == null,
            RegenConsent = Config.AutoUseDeepDungeonPotion && !unei && regenAid != default,
            RegenReady = true,
            RegenInStock = true,
            HealReady = true,
            HealInStock = true,
            HPGateBlocking = gate != HPGate.None,
            RegenCombatHP = Config.RegenPotionCombatHP / 100.0,
            RegenProjectedHP = Config.RegenPotionProjectedHP / 100.0,
            EmergencyHP = Config.EmergencyPotionHP / 100.0,
            EmergencyOutOfCombat = Config.EmergencyPotionOutOfCombat,
            RegenWhenGateBlocks = Config.RegenPotionWhenHPGateBlocks,
        };

        // 第一輪先假設有貨、冷卻好了：不想喝就不必碰背包
        var d = DeepDungeonPotionAdvisor.Decide(s);
        LogOutOfCombatHealSkip(player, s);
        if (!d.WantRegen && !d.WantHeal)
        {
            _regenPushing = _healPushing = false;
            return;
        }

        var defs = ActionDefinitions.Instance;
        var cds = World.Client.Cooldowns;
        var duty = World.Client.DutyActions;
        var inv = InventoryManager.Instance();
        if (inv == null)
            return;

        var regenCount = 0;
        var regenReadyIn = 0f;
        if (d.WantRegen)
        {
            regenReadyIn = defs[regenAid]?.ReadyIn(cds, duty) ?? float.MaxValue;
            regenCount = inv->GetInventoryItemCount(regenAid.ID, false, false, false);
            s.RegenReady = regenReadyIn <= 0f;
            s.RegenInStock = regenCount > Config.RegenPotionReserve;
        }

        var best = -1;
        var bestAmount = -1f;
        var second = -1;
        var healReadyIn = 0f;
        if (d.WantHeal)
        {
            var pars = HealPotionParams();
            for (var i = 0; i < HealPotionCandidates.Length; ++i)
            {
                var aid = HealPotionCandidates[i];
                if (inv->GetInventoryItemCount(aid.ID % 1000000u, aid.ID >= 1000000u, false, false) <= 0)
                    continue;
                var amount = HealAmount(pars[i], maxHP);
                if (amount > bestAmount)
                {
                    second = best;
                    (best, bestAmount) = (i, amount);
                }
                else if (second < 0 || amount > HealAmount(pars[second], maxHP))
                    second = i;
            }
            s.HealInStock = best >= 0;
            healReadyIn = best >= 0 ? defs[HealPotionCandidates[best]]?.ReadyIn(cds, duty) ?? float.MaxValue : 0f;
            s.HealReady = healReadyIn <= 0f;
        }

        d = DeepDungeonPotionAdvisor.Decide(s);

        if (d.Heal)
        {
            var aid = HealPotionCandidates[best];
            hints.ActionsToExecute.Push(aid, player, ActionQueue.Priority.VeryHigh);
            if (!_healPushing)
            {
                ++_potHealCount;
                LogHealPush(player, s, d, aid, bestAmount, second, maxHP);
            }
        }
        _healPushing = d.Heal;

        if (d.Regen)
        {
            hints.ActionsToExecute.Push(regenAid, player, ActionQueue.Priority.Medium);
            if (!_regenPushing)
            {
                ++_potRegenCount;
                LogRegenPush(s, d, regenAid, engaged, gate, gatePct);
            }
        }
        _regenPushing = d.Regen;

        string? why = null;
        if (d.WantHeal && !d.Heal)
            why = blocked != null ? $"想喝治療劑但{blocked}"
                : !s.HealInStock ? "想喝治療劑但背包裡沒有任何治療劑"
                : $"想喝治療劑但還在冷卻中，剩 {healReadyIn:f0} 秒";
        else if (d.WantRegen && !d.Regen && s.RegenReady)
            why = blocked != null ? $"想喝{regenAid.Name()}但{blocked}"
                : regenCount <= 0 ? $"想喝{regenAid.Name()}但背包裡沒有"
                : $"想喝{regenAid.Name()}但只剩 {regenCount} 瓶（保留 {Config.RegenPotionReserve} 瓶給手動）";
        if (why != null && World.CurrentTime >= _potionBlockedLogAfter)
        {
            _potionBlockedLogAfter = World.CurrentTime.AddSeconds(NoPotionLogIntervalSeconds);
            Service.Logger.Information($"[DD] {why}｜{(s.InCombat ? "交戰中" : "脫戰")} HP {s.HP * 100:f0}%");
        }
    }

    private void LogOutOfCombatHealSkip(Actor player, in DeepDungeonPotionAdvisor.Snapshot s)
    {
        var low = !s.InCombat && !s.EmergencyOutOfCombat && s.HP < s.EmergencyHP;
        if (!low)
        {
            _oocHealSkipLogged = false;
            return;
        }
        if (_oocHealSkipLogged)
            return;
        _oocHealSkipLogged = true;
        Service.Logger.Information($"[DD] 脫戰 HP {player.HPRatio * 100f:f0}%：不喝治療劑（留給戰鬥；脫戰每 3 秒回 10%）");
    }

    private void LogHealPush(Actor player, in DeepDungeonPotionAdvisor.Snapshot s, in DeepDungeonPotionAdvisor.Decision d, ActionID aid, float amount, int second, uint maxHP)
    {
        var cmp = "";
        if (second >= 0)
        {
            var other = HealAmount(HealPotionParams()[second], maxHP);
            cmp = $"，{HealPotionCandidates[second].Name()} 只回 {other:N0}＝{other * 100f / maxHP:f0}%";
        }
        var reason = s.HP < s.EmergencyHP
            ? $"HP {s.HP * 100:f0}% < {s.EmergencyHP * 100:f0}%"
            : $"HP {s.HP * 100:f0}%，預估 4 秒後 {d.Projected4 * 100:f0}% < {DeepDungeonPotionAdvisor.HealProjectedHP * 100:f0}%";
        Service.Logger.Information($"[DD] 喝藥：{aid.Name()}（回 {amount:N0}＝{amount * 100f / maxHP:f0}%{cmp}）｜{(s.InCombat ? "交戰中" : "脫戰")} {reason}");
    }

    private void LogRegenPush(in DeepDungeonPotionAdvisor.Snapshot s, in DeepDungeonPotionAdvisor.Decision d, ActionID aid, int engaged, HPGate gate, int gatePct)
    {
        if (s.InCombat)
        {
            var thr = d.Projected10 < s.RegenProjectedHP ? s.RegenProjectedHP : s.RegenCombatHP;
            Service.Logger.Information($"[DD] 喝藥：{aid.Name()}｜交戰中 HP {s.HP * 100:f0}%，預估 10 秒後 {d.Projected10 * 100:f0}%（門檻 {thr * 100:f0}%）｜咬住 {engaged} 隻，近 10 秒每秒受傷 {s.ObservedDtps * 100:f1}%（樓層下限 {engaged * s.PerMobDtps * 100:f1}%）");
            return;
        }
        var what = gate switch
        {
            HPGate.Bronze => "開銅寶箱",
            HPGate.Silver => "開銀寶箱",
            _ => "趕路",
        };
        var target = gatePct / 100.0;
        var natural = Math.Max(0, (target - s.HP) / DeepDungeonPotionAdvisor.RegenPerSecOutOfCombat);
        var withPotion = Math.Max(0, (target - s.HP) / (DeepDungeonPotionAdvisor.RegenPerSecOutOfCombat + DeepDungeonPotionAdvisor.HotTickFraction / DeepDungeonPotionAdvisor.HotTickInterval));
        Service.Logger.Information($"[DD] 喝藥：{aid.Name()}｜脫戰 HP {s.HP * 100:f0}%，低於{what}門檻 {gatePct}%（自然回復約 {natural:f0} 秒，加秘藥約 {withPotion:f0} 秒）");
    }
}
