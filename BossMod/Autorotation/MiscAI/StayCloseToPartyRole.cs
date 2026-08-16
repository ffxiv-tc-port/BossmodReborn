namespace BossMod.Autorotation.MiscAI;

public sealed class StayCloseToPartyRole(RotationModuleManager manager, Actor player) : RotationModule(manager, player)
{
    public enum Tracks
    {
        Role,
        Range
    }

    public static RotationModuleDefinition Definition()
    {
        RotationModuleDefinition def = new("Misc AI: Stay within range of party role", "Module for use by AutoDuty preset.", "AI", "erdelf", RotationModuleQuality.Basic, new(~0ul), 1000);

        var roleRef = def.Define(Tracks.Role).As<Role>("Role", "Role to stay close to");

        foreach (var role in Enum.GetValues<Role>())
        {
            roleRef.AddOption(role);
        }

        // 與 StayCloseToTarget 同一套改動：291 個假造檔位換成真滑桿，0 是「停留在受擊框邊緣（±1）」
        // 的哨兵值（等於舊列舉的 OnHitbox，也是舊的空值預設）。理由與 InternalName 變動的說明
        // 見 StayCloseToTarget.Definition()。
        // ⚠️ 軌道順序不能動：Role 必須留在索引 0，DefineFloat 會斷言「索引 == 目前 Configs 數量」。
        def.DefineFloat(Tracks.Range, "range", 0f, 30f, defaultValue: 0f, speed: 0.1f);

        return def;
    }

    public override void Execute(StrategyValues strategy, Actor? primaryTarget, float estimatedAnimLockDelay, bool isMoving)
    {
        var role = strategy.Option(Tracks.Role).As<Role>();
        if (role != Role.None && role != Manager.Player?.Role)
        {
            var roleActor = World.Party.WithoutSlot(false, true).FirstOrDefault(a => a.Role == role);
            if (roleActor != null)
            {
                var position = roleActor.Position;
                var radius = roleActor.HitboxRadius;
                var range = strategy.GetFloat(Tracks.Range);
                if (range <= 0f)
                    Hints.GoalZones.Add(p => p.InDonut(position, radius - 1, radius + 1) ? 0.5f : 0);
                else
                    Hints.GoalZones.Add(Hints.GoalSingleTarget(position, range + radius, 1f));
            }
        }
    }
}
