namespace BossMod.Stormblood.Raid.O7NGuardian;

sealed class ArmAndHammer(BossModule module) : Components.SingleTargetCast(module, (uint)AID.ArmAndHammer);
sealed class DiffractivePlasma(BossModule module) : Components.RaidwideCast(module, (uint)AID.DiffractivePlasma);
// 擊退距離沒有證據，只提示全體傷害；場邊是電網（受傷＋麻痺），不會摔死。
sealed class Shockwave(BossModule module) : Components.RaidwideCast(module, (uint)AID.Shockwave);
// 沒有預兆；長度照 CastType 4 加首領 hitbox（與沒有模組時的猜法相同）。
sealed class MagitekRay(BossModule module) : Components.SimpleAOEs(module, (uint)AID.MagitekRay, new AOEShapeRect(40f + module.PrimaryActor.HitboxRadius, 3f));

// 主加農砲是距離衰減、衰減範圍沒有證據，刻意不畫；脈輪爆發的塔、惡魔模擬程式的三人圈、設置炸彈的電線沒有證據，也不處理。
sealed class O7NGuardianStates : StateMachineBuilder
{
    public O7NGuardianStates(BossModule module) : base(module)
    {
        TrivialPhase()
            .ActivateOnEnter<ArmAndHammer>()
            .ActivateOnEnter<DiffractivePlasma>()
            .ActivateOnEnter<Shockwave>()
            .ActivateOnEnter<MagitekRay>()
            .ActivateOnEnter<Components.UnclaimedCastAOEs>();
    }
}

// 場地＝台服碰撞在 (0,0) 的 40×40 方形地板，內縮 0.5 碼。
public abstract class O7NGuardian(WorldState ws, Actor primary) : UnverifiedOidTrialModule(ws, primary, default, new ArenaBoundsSquare(19.5f));

[ModuleInfo(BossModuleInfo.Maturity.WIP, StatesType = typeof(O7NGuardianStates), PrimaryActorOID = (uint)OID.BossCandidateA, Contributors = "ffxiv-tc-port", Expansion = BossModuleInfo.Expansion.Stormblood, Category = BossModuleInfo.Category.Raid, GroupType = BossModuleInfo.GroupType.CFC, GroupID = 288u, NameID = 7107u, SortOrder = 1)]
public sealed class O7NGuardianA(WorldState ws, Actor primary) : O7NGuardian(ws, primary);

[ModuleInfo(BossModuleInfo.Maturity.WIP, StatesType = typeof(O7NGuardianStates), PrimaryActorOID = (uint)OID.BossCandidateB, Contributors = "ffxiv-tc-port", Expansion = BossModuleInfo.Expansion.Stormblood, Category = BossModuleInfo.Category.Raid, GroupType = BossModuleInfo.GroupType.CFC, GroupID = 288u, NameID = 7107u, SortOrder = 1)]
public sealed class O7NGuardianB(WorldState ws, Actor primary) : O7NGuardian(ws, primary);
