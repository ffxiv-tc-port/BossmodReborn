namespace BossMod.Autorotation.MiscAI;

public sealed class StayCloseToTarget(RotationModuleManager manager, Actor player) : RotationModule(manager, player)
{
    public enum Tracks
    {
        Range
    }

    public static RotationModuleDefinition Definition()
    {
        RotationModuleDefinition def = new("Misc AI: Stay within range of target", "Module for use by AutoDuty preset.", "AI", "veyn", RotationModuleQuality.Basic, new(~0ul), 1000);

        // 這條以前是「用列舉假造滑桿」：迴圈把 1.1~30.0 每 0.1 一檔全部 AddOption 進去，共 291 個選項，
        // 而且值是編碼過的（存 f*10-10，讀出來再 (Option+10)/10 還原）。使用者要的是連續控制，
        // 而框架本來就有 DefineFloat／FloatRenderer，所以直接換成真滑桿，編解碼一併拆掉。
        // 🔴 0 是哨兵值＝「停留在受擊框邊緣（±1）」，也就是舊列舉的 OnHitbox。
        //    這樣選是因為 OnHitbox 本來就是選項 0，而 StrategyValueTrack 的空值也是 Option = 0
        //    ⇒ 沒設定過這條軌的使用者，行為與改動前完全相同。
        // ⚠️ InternalName 從 "range" 變成 "Range"（DefineFloat 直接用列舉成員名）。既有 preset 的
        //    這一筆會找不到軌道而被略過（Preset.Read 記一行 log 後 continue，preset 其餘設定保留）。
        //    已與使用者確認可接受、自行重建。
        def.DefineFloat(Tracks.Range, "range", 0f, 30f, defaultValue: 0f, speed: 0.1f);

        return def;
    }

    public override void Execute(StrategyValues strategy, Actor? primaryTarget, float estimatedAnimLockDelay, bool isMoving)
    {
        if (primaryTarget != null)
        {
            var position = primaryTarget.Position;
            var radius = primaryTarget.HitboxRadius;
            var range = strategy.GetFloat(Tracks.Range);
            if (range <= 0f)
            {
                Hints.GoalZones.Add(p => p.InDonut(position, radius - 1f, radius + 1f) ? 0.5f : default);
            }
            else
            {
                Hints.GoalZones.Add(Hints.GoalSingleTarget(position, range + radius, 0.5f));
            }
        }
    }
}
