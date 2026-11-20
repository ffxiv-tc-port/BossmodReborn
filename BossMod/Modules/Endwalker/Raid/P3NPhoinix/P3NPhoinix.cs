namespace BossMod.Endwalker.Raid.P3NPhoinix;

sealed class DeadRebirth(BossModule module) : Components.RaidwideCast(module, (uint)AID.DeadRebirth);
sealed class ScorchedExaltation(BossModule module) : Components.RaidwideCast(module, (uint)AID.ScorchedExaltation);
// 一般難度是兩名坦克各自身上的圓形範圍（零式是連線）。
sealed class HeatOfCondemnation(BossModule module) : Components.BaitAwayCast(module, (uint)AID.HeatOfCondemnation, 6f, tankbuster: true, damageType: AIHints.PredictedDamageType.Tankbuster);
sealed class Charplume(BossModule module) : Components.SpreadFromCastTargets(module, (uint)AID.Charplume, 6f);
sealed class FlareOfCondemnation(BossModule module) : Components.SpreadFromCastTargets(module, (uint)AID.FlareOfCondemnation, 6f);

sealed class P3NPhoinixStates : StateMachineBuilder
{
    public P3NPhoinixStates(BossModule module) : base(module)
    {
        TrivialPhase()
            .ActivateOnEnter<DeadRebirth>()
            .ActivateOnEnter<ScorchedExaltation>()
            .ActivateOnEnter<HeatOfCondemnation>()
            .ActivateOnEnter<Charplume>()
            .ActivateOnEnter<FlareOfCondemnation>()
            .ActivateOnEnter<Components.UnclaimedCastAOEs>();
    }
}

// 沒有錄影：場地沿用零式（同一張地圖）；扇形、直線、圓形範圍交給 UnclaimedCastAOEs 照讀條預兆畫。
[ModuleInfo(BossModuleInfo.Maturity.WIP, Contributors = "ffxiv-tc-port", GroupType = BossModuleInfo.GroupType.CFC, GroupID = 806, NameID = 10720)]
public sealed class P3NPhoinix(WorldState ws, Actor primary) : BossModule(ws, primary, new(100f, 100f), new ArenaBoundsCircle(20f));
