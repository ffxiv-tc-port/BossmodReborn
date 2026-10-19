namespace BossMod.Global.DeepDungeon;

/// <summary>
/// 深層迷宮喝藥判斷：輸入一份快照、輸出這一幀要不要推秘藥／治療劑。
/// </summary>
/// <remarks>
/// 契約：純函式，不碰原生、不配置、不依賴 BMR 其他型別（離線一致性測試直接編譯這個檔）。
/// 🔴 與 ~/.claude/tools/bmr-replay/dd_potion_20261020/ddpot_policy.py 的 <c>advisor_decide</c> 是同一條式子的兩份：
/// 改常數或門檻要兩邊一起改，否則離線重放會靜默失準。
/// 比例一律是「最大 HP 的比例」（0..1），時間單位秒。
/// </remarks>
public static class DeepDungeonPotionAdvisor
{
    public const double RegenHorizon = 10.0;
    public const double HealHorizon = 4.0;
    public const double HealProjectedHP = 0.15;
    /// <summary>秘藥每跳的保守值：實測 MCH 0.09～0.11、WAR 0.076。</summary>
    public const double HotTickFraction = 0.08;
    public const double HotTickInterval = 3.0;
    public const int HotTicksTotal = 10;
    public const double RegenPerSecInCombat = 0.01 / 3.0;
    public const double RegenPerSecOutOfCombat = 0.10 / 3.0;
    /// <summary>脫戰喝秘藥的上限：高於這個，不喝也只要等約 6 秒。</summary>
    public const double OutOfCombatRegenHP = 0.5;

    /// <summary>每隻咬住的怪平均每秒傷害（最大 HP 比例）。dungeon：1＝死者宮殿、2＝天之御柱、3＝正統優雷卡。</summary>
    public static double PerMobDtps(int dungeon, int floor) => dungeon switch
    {
        1 => floor > 150 ? 0.012 : 0.008,
        2 => 0.007,
        3 => 0.005,
        _ => 0.008,
    };

    /// <summary>狀態 648 剩餘秒數換成剩餘跳數（第一跳在開始後約 1.5 秒，之後每 3 秒一跳）。</summary>
    public static int HotTicksLeft(double remainingSeconds)
    {
        if (remainingSeconds <= 0)
            return 0;
        var n = (int)Math.Ceiling((remainingSeconds - 1.5) / HotTickInterval);
        return Math.Clamp(n, 0, HotTicksTotal);
    }

    public struct Snapshot
    {
        public double HP;
        public bool InCombat;
        public int Engaged;
        public double ObservedDtps;
        public double PerMobDtps;
        public bool HotActive;
        public int HotTicksLeft;
        /// <summary>false＝死亡、變身、禁止使用道具：照算想不想喝（給 log），但不推。</summary>
        public bool Usable;
        /// <summary>同意自動喝秘藥（AutoUseDeepDungeonPotion），而且沒有被別的條件壓住。</summary>
        public bool RegenConsent;
        public bool RegenReady;
        public bool RegenInStock;
        public bool HealReady;
        public bool HealInStock;
        public bool HPGateBlocking;
        public double RegenCombatHP;
        public double RegenProjectedHP;
        public double EmergencyHP;
        public bool EmergencyOutOfCombat;
        public bool RegenWhenGateBlocks;
    }

    public struct Decision
    {
        public bool WantRegen;
        public bool WantHeal;
        public bool Regen;
        public bool Heal;
        public double Dtps;
        public double Projected10;
        public double Projected4;
    }

    public static double Project(double hp, double regenPerSec, double dtps, double hotLeft, double t)
        => hp + (regenPerSec - dtps) * t + Math.Min(hotLeft, HotTickFraction / HotTickInterval * t);

    public static Decision Decide(in Snapshot s)
    {
        var d = Math.Max(s.ObservedDtps, s.Engaged * s.PerMobDtps);
        var regen = s.InCombat ? RegenPerSecInCombat : RegenPerSecOutOfCombat;
        var hotLeft = s.HotTicksLeft * HotTickFraction;
        var p10 = Project(s.HP, regen, d, hotLeft, RegenHorizon);
        var p4 = Project(s.HP, regen, d, hotLeft, HealHorizon);

        bool wantRegen, wantHeal;
        if (s.InCombat)
        {
            wantRegen = s.RegenConsent && !s.HotActive && ((s.HP < s.RegenCombatHP && p10 < s.RegenCombatHP) || p10 < s.RegenProjectedHP);
            wantHeal = s.HP < s.EmergencyHP || p4 < HealProjectedHP;
        }
        else
        {
            wantRegen = s.RegenConsent && !s.HotActive && s.RegenWhenGateBlocks && s.HPGateBlocking && s.HP < OutOfCombatRegenHP;
            wantHeal = s.EmergencyOutOfCombat && s.HP < s.EmergencyHP;
        }

        return new()
        {
            WantRegen = wantRegen,
            WantHeal = wantHeal,
            Regen = s.Usable && wantRegen && s.RegenReady && s.RegenInStock,
            Heal = s.Usable && wantHeal && s.HealReady && s.HealInStock,
            Dtps = d,
            Projected10 = p10,
            Projected4 = p4,
        };
    }
}
