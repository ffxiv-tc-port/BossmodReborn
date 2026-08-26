namespace BossMod.Autorotation.MiscAI;

public sealed partial class GoToPositional(RotationModuleManager manager, Actor player) : RotationModule(manager, player)
{
    public enum Tracks
    {
        Positional,
        EdgeBuffer
    }

    /// <summary>
    /// 站位要離方位分界線多遠才算「站對」。
    /// </summary>
    /// <remarks>
    /// 移植自上游 <c>1f12f5f96</c>（"Added an edge cushion to allow more margin for positionals and
    /// boss movements"）。實際的收緊算式在 <c>AIHints.GoalSingleTarget</c> 的 <c>cushion</c> 參數，
    /// 那一半已經先併進來了（見 <c>c8cc348be</c>），在這一軌出現之前<b>沒有任何呼叫端傳非 0 值</b>。
    /// <para>
    /// 🔴 <see cref="None"/> 必須是索引 0：<c>StrategyConfigTrack.CreateEmpty()</c> 回
    /// <c>Option = 0</c>，所以「使用者沒動過這一軌」拿到的就是它。
    /// <c>None</c> ＝ <c>cushion 0f</c> ＝ 逐位元組的舊行為，既有使用者不會被這個新軌道改到。
    /// </para>
    /// <para>
    /// 📌 選項名（<c>InternalName</c>）是 preset／plan 的序列化鍵，不可改也不可翻譯；
    /// 顯示名走 <c>StrategyOption.UIName</c> ＝ <c>Loc.T(DisplayName)</c>，譯文在 <c>loc/tw.json</c>。
    /// </para>
    /// </remarks>
    public enum EdgeBufferStrategy { None, Small, Medium, Large }

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

        // 上游 1f12f5f96 的 uiPriority 20 照抄（軌道是由大到小排，所以它會排在 Positional 上面）。
        def.Define(Tracks.EdgeBuffer).As<EdgeBufferStrategy>("EdgeBuffer", "Edge buffer", 20)
            .AddOption(EdgeBufferStrategy.None, "Stand at positional edges")
            .AddOption(EdgeBufferStrategy.Small, "Prefer staying 0.5y inside from the edges")
            .AddOption(EdgeBufferStrategy.Medium, "Prefer staying 1.5y inside from the edges")
            .AddOption(EdgeBufferStrategy.Large, "Prefer staying 3y inside from the edges");

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

        // 上游 1f12f5f96：把方位區的判定往中心收緊 cushion 碼，避免緊貼分界線站位、
        // 目標稍微一轉就掉出方位。None（預設）＝ 0f ＝ 舊行為。
        var cushion = strategy.Option(Tracks.EdgeBuffer).As<EdgeBufferStrategy>() switch
        {
            EdgeBufferStrategy.Small => 0.5f,
            EdgeBufferStrategy.Medium => 1.5f,
            EdgeBufferStrategy.Large => 3f,
            _ => 0f
        };

        Hints.RecommendedPositional = (primaryTarget, positional, true, correct);
        Hints.GoalZones.Add(Hints.GoalSingleTarget(primaryTarget, positional, cushion: cushion));
    }
}
