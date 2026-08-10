namespace BossMod.Autorotation.MiscAI;

public sealed partial class GoToPositional(RotationModuleManager manager, Actor player) : RotationModule(manager, player)
{
    public enum Tracks
    {
        Positional
    }

    // Positional 的超集,多了一個「交給模組自己判斷」的選項。
    // 🔴 preset/plan 是用「選項名字串」序列化的(Strategy.cs 的 SerializeValue 寫 InternalName,
    //    Preset.cs/Plan.cs 用 Options.FindIndex(o => o.InternalName == optionName) 讀回來),
    //    所以前四項的名字與順序必須與 Positional 逐項一致,新選項只能附加在最後 ——
    //    這樣既有 preset 裡存的 "Rear" 才會繼續解析到同一個東西。
    // 🔴 另外 RotationModuleDefinition.AddOption 會斷言「選項索引 == 列舉值」,
    //    所以不能直接在 Positional 上加第五項,必須另立這個列舉。
    public enum PositionalStrategy
    {
        Any,
        Flank,
        Rear,
        Front,
        Automatic
    }

    public static RotationModuleDefinition Definition()
    {
        RotationModuleDefinition def = new("Misc AI: Goes to specified positional", "Module for use with other rotation plugins.", "AI", "erdelf", RotationModuleQuality.Basic, new(~0ul), 1000);

        // 名字逐字對應 Positional 的前四項,見上面 PositionalStrategy 的註解
        def.Define(Tracks.Positional).As<PositionalStrategy>("Positional", "Positional")
            .AddOption(PositionalStrategy.Any, "Any")
            .AddOption(PositionalStrategy.Flank, "Flank")
            .AddOption(PositionalStrategy.Rear, "Rear")
            .AddOption(PositionalStrategy.Front, "Front")
            .AddOption(PositionalStrategy.Automatic, "Automatic");
        return def;
    }

    public override void Execute(StrategyValues strategy, Actor? primaryTarget, float estimatedAnimLockDelay, bool isMoving)
    {
        if (!Player.InCombat
            // ⚠️ 這裡原本傳的是 ClassShared.AID.TrueNorth(7546,技能 ID),不是狀態 ID,
            //    所以真北的守衛從來沒有生效過;艦隊裡其他讀真北的地方(Basexan/AkechiTools)用的都是 SID(1250)。
            || Player.FindStatus((uint)ClassShared.SID.TrueNorth) != null
            || primaryTarget == null
            || primaryTarget is { Omnidirectional: true }
            || primaryTarget is { TargetID: var t, CastInfo: null, IsStrikingDummy: false } && t == Player.InstanceID)
        {
            return;
        }

        var strategyValue = strategy.Option(Tracks.Positional).As<PositionalStrategy>();

        Positional positional;
        if (strategyValue == PositionalStrategy.Automatic)
        {
            positional = UpdateAutoPositional(primaryTarget);
            // 判不出來(沒有方位技/連段位置不足以決定/不是有方位技的職業)就什麼都不推,
            // 寧可站著不動也不要走錯邊再折返
            if (positional is not (Positional.Flank or Positional.Rear))
                return;
        }
        else
        {
            ResetAutoPositional();
            positional = (Positional)strategyValue; // 前四項與 Positional 逐項對應
            if (positional == Positional.Any)
                return;
        }

        //mainly from Basexan.UpdatePositionals
        var correct = positional switch
        {
            Positional.Flank => MathF.Abs(primaryTarget.Rotation.ToDirection().Dot((Player.Position - primaryTarget.Position).Normalized())) < 0.7071067f,
            Positional.Rear => primaryTarget.Rotation.ToDirection().Dot((Player.Position - primaryTarget.Position).Normalized()) < -0.7071068f,
            _ => true
        };

        Hints.RecommendedPositional = (primaryTarget, positional, true, correct);
        Hints.GoalZones.Add(Hints.GoalSingleTarget(primaryTarget, positional));
    }
}
