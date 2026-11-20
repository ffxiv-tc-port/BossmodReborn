namespace BossMod.Endwalker.Raid.P7NAgdistis;

sealed class SparkOfLife(BossModule module) : Components.RaidwideCast(module, (uint)AID.SparkOfLife);
sealed class BoughOfAttisOut(BossModule module) : Components.SimpleAOEs(module, (uint)AID.BoughOfAttisOutAOE, 19f);
sealed class BoughOfAttisSide(BossModule module) : Components.SimpleAOEs(module, (uint)AID.BoughOfAttisSideAOE, new AOEShapeRect(50f, 12.5f));
sealed class BoughOfAttisIn(BossModule module) : Components.SimpleAOEs(module, (uint)AID.BoughOfAttisInAOE, 25f);
sealed class HemitheosHoly(BossModule module) : Components.SpreadFromCastTargets(module, (uint)AID.HemitheosHoly, 6f);
sealed class HemitheosAeroII(BossModule module) : Components.BaitAwayCast(module, (uint)AID.HemitheosAeroII, 8f, tankbuster: true);
sealed class StaticMoon(BossModule module) : Components.SimpleAOEs(module, (uint)AID.StaticMoon, 10f);
sealed class StymphalianStrike(BossModule module) : Components.SimpleAOEs(module, (uint)AID.StymphalianStrike, new AOEShapeRect(60f, 4f));

// 只畫有讀條的第一下：一般難度每步的距離、間隔與發數沒有證據，刻意不照零式預測後續落點。
sealed class BladesOfAttis(BossModule module) : Components.SimpleAOEs(module, (uint)AID.BladesOfAttisFirst, 7f);

sealed class P7NAgdistisStates : StateMachineBuilder
{
    public P7NAgdistisStates(BossModule module) : base(module)
    {
        TrivialPhase()
            .ActivateOnEnter<SparkOfLife>()
            .ActivateOnEnter<BoughOfAttisOut>()
            .ActivateOnEnter<BoughOfAttisSide>()
            .ActivateOnEnter<BoughOfAttisIn>()
            .ActivateOnEnter<HemitheosHoly>()
            .ActivateOnEnter<HemitheosAeroII>()
            .ActivateOnEnter<StaticMoon>()
            .ActivateOnEnter<StymphalianStrike>()
            .ActivateOnEnter<BladesOfAttis>()
            .ActivateOnEnter<Components.UnclaimedCastAOEs>();
    }
}

// 場地＝靜態碰撞地板（半徑 20 圓盤），不用零式的半徑 27：場地外沒有地板、掉下去會死，模組場地不得大於地板。
// 不滅者的導枝之後的三平台與橋不在靜態碰撞裡、切換時機也沒有證據，刻意不畫。
[ModuleInfo(BossModuleInfo.Maturity.WIP, Contributors = "ffxiv-tc-port", GroupType = BossModuleInfo.GroupType.CFC, GroupID = 876, NameID = 11374)]
public sealed class P7NAgdistis(WorldState ws, Actor primary) : BossModule(ws, primary, new(100f, 100f), new ArenaBoundsCircle(20f));
