namespace BossMod.Heavensward.Raid.A1NOppressor;

sealed class ThreeThousandTonzeMissile(BossModule module) : Components.RaidwideCast(module, (uint)AID.ThreeThousandTonzeMissile);

sealed class A1NOppressorStates : StateMachineBuilder
{
    public A1NOppressorStates(BossModule module) : base(module)
    {
        TrivialPhase()
            .ActivateOnEnter<ThreeThousandTonzeMissile>()
            .ActivateOnEnter<Components.UnclaimedCastAOEs>();
    }
}

// 場地＝台服碰撞在 (0,-160) 的那一塊地板（40×48 長方形），內縮 0.5 碼；場外會掉下去。
public abstract class A1NOppressor(WorldState ws, Actor primary) : UnverifiedOidTrialModule(ws, primary, new(0f, -160f), new ArenaBoundsRect(19.5f, 23.5f))
{
    protected override void DrawEnemies(int pcSlot, Actor pc)
    {
        Arena.Actor(PrimaryActor);
        Arena.Actors(Enemies((uint)OID.OppressorZeroFiveA));
        Arena.Actors(Enemies((uint)OID.OppressorZeroFiveB));
        Arena.Actors(Enemies((uint)OID.FaustA));
        Arena.Actors(Enemies((uint)OID.FaustB));
    }
}

[ModuleInfo(BossModuleInfo.Maturity.WIP, StatesType = typeof(A1NOppressorStates), PrimaryActorOID = (uint)OID.BossCandidateA, Contributors = "ffxiv-tc-port", Expansion = BossModuleInfo.Expansion.Heavensward, Category = BossModuleInfo.Category.Raid, GroupType = BossModuleInfo.GroupType.CFC, GroupID = 112u, NameID = 3747u, SortOrder = 1)]
public sealed class A1NOppressorA(WorldState ws, Actor primary) : A1NOppressor(ws, primary);

[ModuleInfo(BossModuleInfo.Maturity.WIP, StatesType = typeof(A1NOppressorStates), PrimaryActorOID = (uint)OID.BossCandidateB, Contributors = "ffxiv-tc-port", Expansion = BossModuleInfo.Expansion.Heavensward, Category = BossModuleInfo.Category.Raid, GroupType = BossModuleInfo.GroupType.CFC, GroupID = 112u, NameID = 3747u, SortOrder = 1)]
public sealed class A1NOppressorB(WorldState ws, Actor primary) : A1NOppressor(ws, primary);
