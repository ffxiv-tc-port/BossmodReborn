namespace BossMod.Stormblood.Raid.O11NOmega;

sealed class Flamethrower(BossModule module) : Components.SimpleAOEs(module, (uint)AID.Flamethrower, new AOEShapeCone(60f, 15f.Degrees()));
sealed class Cannons(BossModule module) : Components.SimpleAOEGroups(module, [(uint)AID.StarboardCannon1, (uint)AID.StarboardCannon2, (uint)AID.LarboardCannon1, (uint)AID.LarboardCannon2], new AOEShapeCone(60f, 105f.Degrees()));
sealed class ElectricSlide(BossModule module) : Components.StackWithCastTargets(module, (uint)AID.ElectricSlide, 6f);
sealed class AtomicRay(BossModule module) : Components.RaidwideCast(module, (uint)AID.AtomicRay);
sealed class MustardBomb(BossModule module) : Components.BaitAwayCast(module, (uint)AID.MustardBomb, new AOEShapeCircle(5f), true, tankbuster: true, damageType: AIHints.PredictedDamageType.Tankbuster);
// 點名到判定的秒數沒有錄影：時間軸上飛彈發射到飛彈命中約 10 秒，取 10 秒。
sealed class BallisticImpact(BossModule module) : Components.SpreadFromIcon(module, (uint)IconID.BallisticImpact, (uint)AID.BallisticImpact, 6f, 10d);
sealed class Rush(BossModule module) : Components.SimpleAOEs(module, (uint)AID.Rush, new AOEShapeRect(49f, 7.5f));
sealed class Reformat(BossModule module) : Components.SimpleAOEs(module, (uint)AID.Reformat, new AOEShapeDonut(10f, 20f));
sealed class Reset(BossModule module) : Components.SimpleAOEs(module, (uint)AID.Reset, 12f);

sealed class O11NOmegaStates : StateMachineBuilder
{
    public O11NOmegaStates(BossModule module) : base(module)
    {
        TrivialPhase()
            .ActivateOnEnter<Flamethrower>()
            .ActivateOnEnter<Cannons>()
            .ActivateOnEnter<ElectricSlide>()
            .ActivateOnEnter<AtomicRay>()
            .ActivateOnEnter<MustardBomb>()
            .ActivateOnEnter<BallisticImpact>()
            .ActivateOnEnter<Rush>()
            .ActivateOnEnter<Reformat>()
            .ActivateOnEnter<Reset>()
            .ActivateOnEnter<Components.UnclaimedCastAOEs>();
    }
}

// 場地＝台服碰撞在 (100,100) 的方形地板（x/z 80.25～120）放得下的方形，取 0.5 碼整數。
public abstract class O11NOmega(WorldState ws, Actor primary) : UnverifiedOidTrialModule(ws, primary, new(100f, 100f), new ArenaBoundsSquare(19.5f));

[ModuleInfo(BossModuleInfo.Maturity.WIP, StatesType = typeof(O11NOmegaStates), PrimaryActorOID = (uint)OID.BossCandidateA, Contributors = "ffxiv-tc-port", Expansion = BossModuleInfo.Expansion.Stormblood, Category = BossModuleInfo.Category.Raid, GroupType = BossModuleInfo.GroupType.CFC, GroupID = 589u, NameID = 7635u, SortOrder = 1)]
public sealed class O11NOmegaA(WorldState ws, Actor primary) : O11NOmega(ws, primary);

[ModuleInfo(BossModuleInfo.Maturity.WIP, StatesType = typeof(O11NOmegaStates), PrimaryActorOID = (uint)OID.BossCandidateB, Contributors = "ffxiv-tc-port", Expansion = BossModuleInfo.Expansion.Stormblood, Category = BossModuleInfo.Category.Raid, GroupType = BossModuleInfo.GroupType.CFC, GroupID = 589u, NameID = 7635u, SortOrder = 1)]
public sealed class O11NOmegaB(WorldState ws, Actor primary) : O11NOmega(ws, primary);
