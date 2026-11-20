namespace BossMod.Shadowbringers.Raid.E01NEdenPrime;

sealed class EdensGravity(BossModule module) : Components.RaidwideCast(module, (uint)AID.EdensGravity);
sealed class DimensionalShift(BossModule module) : Components.RaidwideCast(module, (uint)AID.DimensionalShift);
sealed class FragorMaximus(BossModule module) : Components.RaidwideCast(module, (uint)AID.FragorMaximus);
sealed class ManaBurst(BossModule module) : Components.RaidwideCast(module, (uint)AID.ManaBurst);
// 內徑照 Omen gl_sircle_6005（外 60／內 5）；後備猜法對 CastType 10 一律給內徑 3。
sealed class EdensFlare(BossModule module) : Components.SimpleAOEs(module, (uint)AID.EdensFlare, new AOEShapeDonut(5f, 60f));
sealed class SpearOfParadise(BossModule module) : Components.SingleTargetCast(module, (uint)AID.SpearOfParadise);
sealed class EdensFireIII(BossModule module) : Components.SpreadFromCastTargets(module, (uint)AID.EdensFireIII, 5f);
sealed class ViceOfVanity(BossModule module) : Components.BaitAwayTethers(module, new AOEShapeRect(100f, 3f), (uint)TetherID.ViceOfVanity, (uint)AID.ViceOfVanity);

sealed class E01NEdenPrimeStates : StateMachineBuilder
{
    public E01NEdenPrimeStates(BossModule module) : base(module)
    {
        TrivialPhase()
            .ActivateOnEnter<EdensGravity>()
            .ActivateOnEnter<DimensionalShift>()
            .ActivateOnEnter<FragorMaximus>()
            .ActivateOnEnter<ManaBurst>()
            .ActivateOnEnter<EdensFlare>()
            .ActivateOnEnter<SpearOfParadise>()
            .ActivateOnEnter<EdensFireIII>()
            .ActivateOnEnter<ViceOfVanity>()
            .ActivateOnEnter<Components.UnclaimedCastAOEs>();
    }
}

// 場地＝台服碰撞在中心那一塊地板（40×40 正方形），內縮 0.5 碼；場外會掉下去。
public abstract class E01NEdenPrime(WorldState ws, Actor primary) : UnverifiedOidTrialModule(ws, primary, new(100f, 100f), new ArenaBoundsSquare(19.5f));

[ModuleInfo(BossModuleInfo.Maturity.WIP, StatesType = typeof(E01NEdenPrimeStates), PrimaryActorOID = (uint)OID.BossCandidateA, Contributors = "ffxiv-tc-port", Expansion = BossModuleInfo.Expansion.Shadowbringers, Category = BossModuleInfo.Category.Raid, GroupType = BossModuleInfo.GroupType.CFC, GroupID = 653u, NameID = 8345u, SortOrder = 1)]
public sealed class E01NEdenPrimeA(WorldState ws, Actor primary) : E01NEdenPrime(ws, primary);

[ModuleInfo(BossModuleInfo.Maturity.WIP, StatesType = typeof(E01NEdenPrimeStates), PrimaryActorOID = (uint)OID.BossCandidateB, Contributors = "ffxiv-tc-port", Expansion = BossModuleInfo.Expansion.Shadowbringers, Category = BossModuleInfo.Category.Raid, GroupType = BossModuleInfo.GroupType.CFC, GroupID = 653u, NameID = 8345u, SortOrder = 1)]
public sealed class E01NEdenPrimeB(WorldState ws, Actor primary) : E01NEdenPrime(ws, primary);
