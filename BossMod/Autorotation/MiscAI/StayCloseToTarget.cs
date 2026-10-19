namespace BossMod.Autorotation.MiscAI;

public sealed class StayCloseToTarget(RotationModuleManager manager, Actor player) : RotationModule(manager, player)
{
    // 軌道的列舉成員名就是 preset 的序列化鍵（DefineFloat 拿它當 InternalName），新軌一律加在最後
    public enum Tracks
    {
        Range,
        MeleeRange,
        RangedRange,
    }

    public static RotationModuleDefinition Definition()
    {
        RotationModuleDefinition def = new("Misc AI: Stay within range of target", "Module for use by AutoDuty preset.", "AI", "veyn", RotationModuleQuality.Basic, new(~0ul), 1000);

        // Range 是真滑桿（DefineFloat／FloatRenderer）。
        // 🔴 0 是哨兵值＝「停留在受擊框邊緣（±1）」：StrategyValueTrack 的空值也是 Option = 0 ⇒ 沒設定過這條軌的使用者拿到的就是它。
        // ⚠️ InternalName 是 "Range"（DefineFloat 直接用列舉成員名）：舊 preset 裡名為 "range" 的這一筆會找不到軌道而被略過
        //    （Preset.Read 記一行 log 後 continue，preset 其餘設定保留）。
        def.DefineFloat(Tracks.Range, "range", 0f, 30f, defaultValue: 0f, speed: 0.1f);

        // 依目前職業分近戰／遠程兩個值；預設 -1（小於 0）＝沿用上面的 Range ⇒ 沒設過的 preset、AutoDuty 經 IPC 改 Range 的行為都不變
        def.DefineFloat(Tracks.MeleeRange, "Range for tanks, melee and red mage (below 0 = use range)", -1f, 30f, defaultValue: -1f, speed: 0.1f);
        def.DefineFloat(Tracks.RangedRange, "Range for other ranged, casters and healers (below 0 = use range)", -1f, 30f, defaultValue: -1f, speed: 0.1f);

        return def;
    }

    /// <summary>這一幀實際要用的距離：坦克／近戰／赤魔取 <paramref name="melee"/>，其餘物理遠程／法系／治療（含青魔）取 <paramref name="ranged"/>；沒設定（小於 0）或職業不在這兩類時取 <paramref name="common"/>。</summary>
    /// <remarks>赤魔的能力技大多要貼身，只在這裡算近戰；全域的 GetClassCategory 不動（隊伍職能、模組站位都靠它）。</remarks>
    public static float EffectiveRange(Class cls, float common, float melee, float ranged)
    {
        var byClass = cls == Class.RDM ? melee : cls.GetClassCategory() switch
        {
            ClassCategory.Tank or ClassCategory.Melee => melee,
            ClassCategory.Healer or ClassCategory.PhysRanged or ClassCategory.Caster or ClassCategory.Limited => ranged,
            _ => -1f,
        };
        return byClass < 0f ? common : byClass;
    }

    private readonly GoalMemo _goal = new();
    private static Func<WPos, float> EdgeRing(WPos center, float inner, float outer) => p => p.InDonut(center, inner, outer) ? 0.5f : default;

    public override void Execute(StrategyValues strategy, Actor? primaryTarget, float estimatedAnimLockDelay, bool isMoving)
    {
        // 目標不是敵對（隊友、其他玩家、友方 NPC、非角色物件；IsAlly 由 WorldStateGameSync 依遊戲的目標分類填）就當沒有目標，走位交給其他模組
        if (primaryTarget != null && !primaryTarget.IsAlly)
        {
            var position = primaryTarget.Position;
            var radius = primaryTarget.HitboxRadius;
            var range = EffectiveRange(Player.Class, strategy.GetFloat(Tracks.Range), strategy.GetFloat(Tracks.MeleeRange), strategy.GetFloat(Tracks.RangedRange));
            if (range <= 0f)
            {
                Hints.GoalZones.Add(_goal.Get(position, radius - 1f, radius + 1f, EdgeRing));
            }
            else
            {
                Hints.GoalZones.Add(_goal.SingleTarget(Hints, position, range + radius, 0.5f));
            }
        }
    }
}
