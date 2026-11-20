namespace BossMod.Endwalker.Raid.P4NHesperos;

sealed class Bloodrake(BossModule module) : Components.RaidwideCast(module, (uint)AID.Bloodrake);
sealed class Decollation(BossModule module) : Components.RaidwideCast(module, (uint)AID.Decollation);
sealed class ElegantEvisceration(BossModule module) : Components.BaitAwayCast(module, (uint)AID.ElegantEvisceration, 5f, tankbuster: true, damageType: AIHints.PredictedDamageType.Tankbuster);
sealed class AcidMekhane(BossModule module) : Components.SpreadFromCastTargets(module, (uint)AID.AcidMekhane, 5f);
// 全隊分攤只有一個目標：最少人數小於最多人數時，圈外的人會同時收到「進圈」與「別進圈」兩個禁區，所以兩者都設 8。
sealed class LavaMekhane(BossModule module) : Components.StackWithCastTargets(module, (uint)AID.LavaMekhane, 6f, 8, 8);

sealed class P4NHesperosStates : StateMachineBuilder
{
    public P4NHesperosStates(BossModule module) : base(module)
    {
        TrivialPhase()
            .ActivateOnEnter<Bloodrake>()
            .ActivateOnEnter<Decollation>()
            .ActivateOnEnter<ElegantEvisceration>()
            .ActivateOnEnter<AcidMekhane>()
            .ActivateOnEnter<LavaMekhane>()
            .ActivateOnEnter<Components.UnclaimedCastAOEs>();
    }
}

// 沒有錄影：場地沿用零式（同一張地圖）；擊退（噴水板畫、換位強襲）與雷電板畫的距離衰減沒有一般難度的數據，不列。
[ModuleInfo(BossModuleInfo.Maturity.WIP, Contributors = "ffxiv-tc-port", GroupType = BossModuleInfo.GroupType.CFC, GroupID = 800, NameID = 10742)]
public sealed class P4NHesperos(WorldState ws, Actor primary) : BossModule(ws, primary, new(100f, 100f), new ArenaBoundsSquare(20f));
