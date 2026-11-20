namespace BossMod.Endwalker.Raid.P1NErichthonios;

// 傷害來自王身上的 helper 讀條（比王的讀條晚 0.7 秒結束），一般難度是 180° 扇形，不是零式的 270°。
sealed class GaolerFlail(BossModule module) : Components.SimpleAOEGroups(module, [(uint)AID.GaolerFlailR, (uint)AID.GaolerFlailL], new AOEShapeCone(60f, 90f.Degrees()));

sealed class WarderWrath(BossModule module) : Components.RaidwideCast(module, (uint)AID.WarderWrath);
sealed class PitilessFlail(BossModule module) : Components.BaitAwayCast(module, (uint)AID.PitilessFlail, new AOEShapeRect(60f, 3f));
// 全隊分攤：最少人數小於最多人數時，圈外的人會同時收到「進圈」與「別進圈」兩個禁區，所以兩者都設 8。
sealed class TrueHoly(BossModule module) : Components.StackWithIcon(module, (uint)IconID.TrueHoly, (uint)AID.TrueHoly, 6f, 5.1d, 8, 8);
sealed class HeavyHand(BossModule module) : Components.SingleTargetCast(module, (uint)AID.HeavyHand);

sealed class P1NErichthoniosStates : StateMachineBuilder
{
    public P1NErichthoniosStates(BossModule module) : base(module)
    {
        TrivialPhase()
            .ActivateOnEnter<GaolerFlail>()
            .ActivateOnEnter<WarderWrath>()
            .ActivateOnEnter<PitilessFlail>()
            .ActivateOnEnter<TrueHoly>()
            .ActivateOnEnter<HeavyHand>()
            .ActivateOnEnter<Components.UnclaimedCastAOEs>();
    }
}

// 錄影只涵蓋開場約 85 秒（王在下一招前就倒了）；之後的監獄、冰火、內外抽擊沒有錄影，交給 UnclaimedCastAOEs 照後備猜法畫。
[ModuleInfo(BossModuleInfo.Maturity.WIP, Contributors = "ffxiv-tc-port", GroupType = BossModuleInfo.GroupType.CFC, GroupID = 808, NameID = 10345)]
public sealed class P1NErichthonios(WorldState ws, Actor primary) : BossModule(ws, primary, new(100f, 100f), new ArenaBoundsSquare(20f));
