namespace BossMod.Heavensward.Raid.A5NRatfinxTwinkledinks;

// 標記到落地的秒數沒有錄影，5 秒只影響 AI 開始散開的時機；讀條開始後由 GlupgloopAOE 畫實際落點。
sealed class GlupgloopSpread(BossModule module) : Components.SpreadFromIcon(module, (uint)IconID.Glupgloop, (uint)AID.Glupgloop, 8f, 5d);
sealed class GlupgloopAOE(BossModule module) : Components.SimpleAOEs(module, (uint)AID.Glupgloop, 8f);

sealed class A5NRatfinxTwinkledinksStates : StateMachineBuilder
{
    public A5NRatfinxTwinkledinksStates(BossModule module) : base(module)
    {
        TrivialPhase()
            .ActivateOnEnter<GlupgloopSpread>()
            .ActivateOnEnter<GlupgloopAOE>()
            .ActivateOnEnter<Components.UnclaimedCastAOEs>();
    }
}

// 場地＝台服碰撞在 (0,-190) 的 40×40 方形地板，內縮 0.5 碼。
public abstract class A5NRatfinxTwinkledinks(WorldState ws, Actor primary) : UnverifiedOidTrialModule(ws, primary, new(0f, -190f), new ArenaBoundsSquare(19.5f));

[ModuleInfo(BossModuleInfo.Maturity.WIP, StatesType = typeof(A5NRatfinxTwinkledinksStates), PrimaryActorOID = (uint)OID.BossCandidateA, Contributors = "ffxiv-tc-port", Expansion = BossModuleInfo.Expansion.Heavensward, Category = BossModuleInfo.Category.Raid, GroupType = BossModuleInfo.GroupType.CFC, GroupID = 136u, NameID = 4687u, SortOrder = 1)]
public sealed class A5NRatfinxTwinkledinksA(WorldState ws, Actor primary) : A5NRatfinxTwinkledinks(ws, primary);

[ModuleInfo(BossModuleInfo.Maturity.WIP, StatesType = typeof(A5NRatfinxTwinkledinksStates), PrimaryActorOID = (uint)OID.BossCandidateB, Contributors = "ffxiv-tc-port", Expansion = BossModuleInfo.Expansion.Heavensward, Category = BossModuleInfo.Category.Raid, GroupType = BossModuleInfo.GroupType.CFC, GroupID = 136u, NameID = 4687u, SortOrder = 1)]
public sealed class A5NRatfinxTwinkledinksB(WorldState ws, Actor primary) : A5NRatfinxTwinkledinks(ws, primary);
