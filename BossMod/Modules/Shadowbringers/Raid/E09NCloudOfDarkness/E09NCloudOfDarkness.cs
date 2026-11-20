namespace BossMod.Shadowbringers.Raid.E09NCloudOfDarkness;

sealed class GroundRazingParticleBeam(BossModule module) : Components.RaidwideCast(module, (uint)AID.GroundRazingParticleBeam);
sealed class EmptyPlane(BossModule module) : Components.RaidwideCast(module, (uint)AID.EmptyPlane);
sealed class ObscureWoods(BossModule module) : Components.RaidwideCast(module, (uint)AID.ObscureWoods);
sealed class DelugeOfDarkness(BossModule module) : Components.RaidwideCast(module, (uint)AID.DelugeOfDarkness);

// 枯萎的擊退距離、廣角式跳躍波動砲（沒有預兆）的角度、暗之戰技的左右方向都沒有證據，刻意交給後備猜法照讀條預兆畫。
sealed class E09NCloudOfDarknessStates : StateMachineBuilder
{
    public E09NCloudOfDarknessStates(BossModule module) : base(module)
    {
        TrivialPhase()
            .ActivateOnEnter<GroundRazingParticleBeam>()
            .ActivateOnEnter<EmptyPlane>()
            .ActivateOnEnter<ObscureWoods>()
            .ActivateOnEnter<DelugeOfDarkness>()
            .ActivateOnEnter<Components.UnclaimedCastAOEs>();
    }
}

// 場地＝台服碰撞在 (100,100) 的 40×40 正方形地板，內縮 0.5 碼；暗之氾濫打掉的角落刻意不畫成場地切換（沒有地圖效果的證據）。
public abstract class E09NCloudOfDarkness(WorldState ws, Actor primary) : UnverifiedOidTrialModule(ws, primary, new(100f, 100f), new ArenaBoundsSquare(19.5f));

[ModuleInfo(BossModuleInfo.Maturity.WIP, StatesType = typeof(E09NCloudOfDarknessStates), PrimaryActorOID = (uint)OID.BossCandidateA, Contributors = "ffxiv-tc-port", Expansion = BossModuleInfo.Expansion.Shadowbringers, Category = BossModuleInfo.Category.Raid, GroupType = BossModuleInfo.GroupType.CFC, GroupID = 749u, NameID = 9764u, SortOrder = 1)]
public sealed class E09NCloudOfDarknessA(WorldState ws, Actor primary) : E09NCloudOfDarkness(ws, primary);

[ModuleInfo(BossModuleInfo.Maturity.WIP, StatesType = typeof(E09NCloudOfDarknessStates), PrimaryActorOID = (uint)OID.BossCandidateB, Contributors = "ffxiv-tc-port", Expansion = BossModuleInfo.Expansion.Shadowbringers, Category = BossModuleInfo.Category.Raid, GroupType = BossModuleInfo.GroupType.CFC, GroupID = 749u, NameID = 9764u, SortOrder = 1)]
public sealed class E09NCloudOfDarknessB(WorldState ws, Actor primary) : E09NCloudOfDarkness(ws, primary);
