namespace BossMod.Shadowbringers.Raid.E06NGarudaIfrit;

sealed class Superstorm(BossModule module) : Components.RaidwideCast(module, (uint)AID.Superstorm);
sealed class InfernoHowl(BossModule module) : Components.RaidwideCast(module, (uint)AID.InfernoHowl);
// 伊弗利特落地是距離衰減的全體傷害；衰減半徑沒有證據，只做全體傷害預告。
sealed class Touchdown(BossModule module) : Components.RaidwideCast(module, (uint)AID.Touchdown);
sealed class InstantIncineration(BossModule module) : Components.BaitAwayCast(module, (uint)AID.InstantIncineration, 4f, tankbuster: true, damageType: AIHints.PredictedDamageType.Tankbuster);
// 被點名的人要有至少一個沒被點名的人一起分攤；人數上限沒有證據，不設。
sealed class AirBump(BossModule module) : Components.StackWithCastTargets(module, (uint)AID.AirBump, 4f);

// 凶猛風暴、暴怒風暴連線的扇形沒有讀條也沒有預兆，角度沒有證據；下行突風、吸引力的擊退距離沒有證據，刻意不處理。
sealed class E06NGarudaIfritStates : StateMachineBuilder
{
    public E06NGarudaIfritStates(BossModule module) : base(module)
    {
        TrivialPhase()
            .ActivateOnEnter<Superstorm>()
            .ActivateOnEnter<InfernoHowl>()
            .ActivateOnEnter<Touchdown>()
            .ActivateOnEnter<InstantIncineration>()
            .ActivateOnEnter<AirBump>()
            .ActivateOnEnter<Components.UnclaimedCastAOEs>();
    }
}

// 場地＝台服碰撞的圓形地板（半徑 20），內縮 0.5 碼；碰到邊緣即死。
// 三個首領各自註冊：前半場迦樓羅、伊弗利特輪流上場，最後合體成赤翼羅羯坨博叉（新的角色）。
public abstract class E06NGarudaIfrit(WorldState ws, Actor primary) : UnverifiedOidTrialModule(ws, primary, new(100f, 100f), new ArenaBoundsCircle(19.5f));

[ModuleInfo(BossModuleInfo.Maturity.WIP, StatesType = typeof(E06NGarudaIfritStates), PrimaryActorOID = (uint)OID.GarudaA, Contributors = "ffxiv-tc-port", Expansion = BossModuleInfo.Expansion.Shadowbringers, Category = BossModuleInfo.Category.Raid, GroupType = BossModuleInfo.GroupType.CFC, GroupID = 719u, NameID = 9287u, SortOrder = 1)]
public sealed class E06NGarudaA(WorldState ws, Actor primary) : E06NGarudaIfrit(ws, primary);

[ModuleInfo(BossModuleInfo.Maturity.WIP, StatesType = typeof(E06NGarudaIfritStates), PrimaryActorOID = (uint)OID.GarudaB, Contributors = "ffxiv-tc-port", Expansion = BossModuleInfo.Expansion.Shadowbringers, Category = BossModuleInfo.Category.Raid, GroupType = BossModuleInfo.GroupType.CFC, GroupID = 719u, NameID = 9287u, SortOrder = 1)]
public sealed class E06NGarudaB(WorldState ws, Actor primary) : E06NGarudaIfrit(ws, primary);

[ModuleInfo(BossModuleInfo.Maturity.WIP, StatesType = typeof(E06NGarudaIfritStates), PrimaryActorOID = (uint)OID.IfritA, Contributors = "ffxiv-tc-port", Expansion = BossModuleInfo.Expansion.Shadowbringers, Category = BossModuleInfo.Category.Raid, GroupType = BossModuleInfo.GroupType.CFC, GroupID = 719u, NameID = 9288u, SortOrder = 2)]
public sealed class E06NIfritA(WorldState ws, Actor primary) : E06NGarudaIfrit(ws, primary);

[ModuleInfo(BossModuleInfo.Maturity.WIP, StatesType = typeof(E06NGarudaIfritStates), PrimaryActorOID = (uint)OID.IfritB, Contributors = "ffxiv-tc-port", Expansion = BossModuleInfo.Expansion.Shadowbringers, Category = BossModuleInfo.Category.Raid, GroupType = BossModuleInfo.GroupType.CFC, GroupID = 719u, NameID = 9288u, SortOrder = 2)]
public sealed class E06NIfritB(WorldState ws, Actor primary) : E06NGarudaIfrit(ws, primary);

[ModuleInfo(BossModuleInfo.Maturity.WIP, StatesType = typeof(E06NGarudaIfritStates), PrimaryActorOID = (uint)OID.RaktapaksaA, Contributors = "ffxiv-tc-port", Expansion = BossModuleInfo.Expansion.Shadowbringers, Category = BossModuleInfo.Category.Raid, GroupType = BossModuleInfo.GroupType.CFC, GroupID = 719u, NameID = 9289u, SortOrder = 3)]
public sealed class E06NRaktapaksaA(WorldState ws, Actor primary) : E06NGarudaIfrit(ws, primary);

[ModuleInfo(BossModuleInfo.Maturity.WIP, StatesType = typeof(E06NGarudaIfritStates), PrimaryActorOID = (uint)OID.RaktapaksaB, Contributors = "ffxiv-tc-port", Expansion = BossModuleInfo.Expansion.Shadowbringers, Category = BossModuleInfo.Category.Raid, GroupType = BossModuleInfo.GroupType.CFC, GroupID = 719u, NameID = 9289u, SortOrder = 3)]
public sealed class E06NRaktapaksaB(WorldState ws, Actor primary) : E06NGarudaIfrit(ws, primary);
