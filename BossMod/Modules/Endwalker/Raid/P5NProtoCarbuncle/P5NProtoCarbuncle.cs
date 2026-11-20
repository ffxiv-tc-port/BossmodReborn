namespace BossMod.Endwalker.Raid.P5NProtoCarbuncle;

sealed class RubyGlow(BossModule module) : Components.RaidwideCast(module, (uint)AID.RubyGlow);
sealed class SonicHowl(BossModule module) : Components.RaidwideCast(module, (uint)AID.SonicHowl);
sealed class Crunch(BossModule module) : Components.SingleTargetCast(module, (uint)AID.Crunch);
sealed class ToxicCrunch(BossModule module) : Components.SingleTargetCast(module, (uint)AID.ToxicCrunch);
sealed class VenomRain(BossModule module) : Components.SpreadFromCastTargets(module, (uint)AID.VenomRain, 5f);
// 全隊分攤只有一個目標：最少人數小於最多人數時，圈外的人會同時收到「進圈」與「別進圈」兩個禁區，所以兩者都設 8。
sealed class VenomPool(BossModule module) : Components.StackWithCastTargets(module, (uint)AID.VenomPool, 6f, 8, 8);

sealed class P5NProtoCarbuncleStates : StateMachineBuilder
{
    public P5NProtoCarbuncleStates(BossModule module) : base(module)
    {
        TrivialPhase()
            .ActivateOnEnter<RubyGlow>()
            .ActivateOnEnter<SonicHowl>()
            .ActivateOnEnter<Crunch>()
            .ActivateOnEnter<ToxicCrunch>()
            .ActivateOnEnter<VenomRain>()
            .ActivateOnEnter<VenomPool>()
            .ActivateOnEnter<Components.UnclaimedCastAOEs>();
    }
}

// 沒有錄影：場地沿用零式（同一張地圖、半邊 15；攻略說後半場地略大，這裡偏保守）；
// 紅寶石反射後的灼熱射線（自訂形狀）與黃寶石射線的格子形狀沒有一般難度的數據，交給後備猜法或不畫。
[ModuleInfo(BossModuleInfo.Maturity.WIP, Contributors = "ffxiv-tc-port", GroupType = BossModuleInfo.GroupType.CFC, GroupID = 872, NameID = 11440)]
public sealed class P5NProtoCarbuncle(WorldState ws, Actor primary) : BossModule(ws, primary, new(100f, 100f), new ArenaBoundsSquare(15f));
