namespace BossMod.Shadowbringers.Raid.E02NVoidwalker;

sealed class Entropy(BossModule module) : Components.RaidwideCast(module, (uint)AID.Entropy);
sealed class Shadowflame(BossModule module) : Components.SingleTargetCast(module, (uint)AID.Shadowflame);
// 頭上標記出現到判定的秒數沒有錄影可證，照前置讀條（5.0 秒）抓。
sealed class DarkFireIII(BossModule module) : Components.SpreadFromIcon(module, (uint)IconID.DarkFireIII, (uint)AID.DarkFireIII, 8f, 5.1d);
// 全隊分攤：最少人數小於最多人數時，圈外的人會同時收到「進圈」與「別進圈」兩個禁區，所以兩者都設 8。
sealed class UnholyDarkness(BossModule module) : Components.StackWithIcon(module, (uint)IconID.UnholyDarkness, (uint)AID.UnholyDarkness, 6f, 5.1d, 8, 8);

// 空無的惡意刻意不處理：擊退範圍（寬 40）比場地（寬 30）還寬，交給後備猜法會畫成整個場地都是禁區；擊退距離沒有證據。
sealed class E02NVoidwalkerStates : StateMachineBuilder
{
    public E02NVoidwalkerStates(BossModule module) : base(module)
    {
        TrivialPhase()
            .ActivateOnEnter<Entropy>()
            .ActivateOnEnter<Shadowflame>()
            .ActivateOnEnter<DarkFireIII>()
            .ActivateOnEnter<UnholyDarkness>()
            .ActivateOnEnter<Components.UnclaimedCastAOEs>();
    }
}

// 場地＝台服碰撞地板（30×40 長方形），內縮 0.5 碼。
public abstract class E02NVoidwalker(WorldState ws, Actor primary) : UnverifiedOidTrialModule(ws, primary, new(100f, 100f), new ArenaBoundsRect(14.5f, 19.5f));

[ModuleInfo(BossModuleInfo.Maturity.WIP, StatesType = typeof(E02NVoidwalkerStates), PrimaryActorOID = (uint)OID.BossCandidateA, Contributors = "ffxiv-tc-port", Expansion = BossModuleInfo.Expansion.Shadowbringers, Category = BossModuleInfo.Category.Raid, GroupType = BossModuleInfo.GroupType.CFC, GroupID = 684u, NameID = 8382u, SortOrder = 1)]
public sealed class E02NVoidwalkerA(WorldState ws, Actor primary) : E02NVoidwalker(ws, primary);

[ModuleInfo(BossModuleInfo.Maturity.WIP, StatesType = typeof(E02NVoidwalkerStates), PrimaryActorOID = (uint)OID.BossCandidateB, Contributors = "ffxiv-tc-port", Expansion = BossModuleInfo.Expansion.Shadowbringers, Category = BossModuleInfo.Category.Raid, GroupType = BossModuleInfo.GroupType.CFC, GroupID = 684u, NameID = 8382u, SortOrder = 1)]
public sealed class E02NVoidwalkerB(WorldState ws, Actor primary) : E02NVoidwalker(ws, primary);
