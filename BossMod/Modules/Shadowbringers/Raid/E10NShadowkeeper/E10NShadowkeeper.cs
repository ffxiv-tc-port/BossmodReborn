namespace BossMod.Shadowbringers.Raid.E10NShadowkeeper;

sealed class DeepshadowNova(BossModule module) : Components.RaidwideCast(module, (uint)AID.DeepshadowNova);
sealed class VoidPulse(BossModule module) : Components.RaidwideCast(module, (uint)AID.VoidPulse);

// 影之狂吠的擊退距離、影之斬擊（沒有讀條的直線死刑）瞄準誰與何時判定都沒有證據，刻意不處理；
// 十億斬擊、向心聚爆（含影子版）照讀條預兆交給後備猜法。
sealed class E10NShadowkeeperStates : StateMachineBuilder
{
    public E10NShadowkeeperStates(BossModule module) : base(module)
    {
        TrivialPhase()
            .ActivateOnEnter<DeepshadowNova>()
            .ActivateOnEnter<VoidPulse>()
            .ActivateOnEnter<Components.UnclaimedCastAOEs>();
    }
}

// 場地＝台服碰撞在 (100,100) 的 50×50 正方形地板，內縮 0.5 碼；被擊退出場會掉下去。
public abstract class E10NShadowkeeper(WorldState ws, Actor primary) : UnverifiedOidTrialModule(ws, primary, new(100f, 100f), new ArenaBoundsSquare(24.5f));

[ModuleInfo(BossModuleInfo.Maturity.WIP, StatesType = typeof(E10NShadowkeeperStates), PrimaryActorOID = (uint)OID.BossCandidateA, Contributors = "ffxiv-tc-port", Expansion = BossModuleInfo.Expansion.Shadowbringers, Category = BossModuleInfo.Category.Raid, GroupType = BossModuleInfo.GroupType.CFC, GroupID = 747u, NameID = 9769u, SortOrder = 1)]
public sealed class E10NShadowkeeperA(WorldState ws, Actor primary) : E10NShadowkeeper(ws, primary);

[ModuleInfo(BossModuleInfo.Maturity.WIP, StatesType = typeof(E10NShadowkeeperStates), PrimaryActorOID = (uint)OID.BossCandidateB, Contributors = "ffxiv-tc-port", Expansion = BossModuleInfo.Expansion.Shadowbringers, Category = BossModuleInfo.Category.Raid, GroupType = BossModuleInfo.GroupType.CFC, GroupID = 747u, NameID = 9769u, SortOrder = 1)]
public sealed class E10NShadowkeeperB(WorldState ws, Actor primary) : E10NShadowkeeper(ws, primary);
