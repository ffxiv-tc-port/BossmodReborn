namespace BossMod.Stormblood.Dungeon.D12SwallowsCompass.D123QitianDasheng;

public enum OID : uint
{
    Boss = 0x21D9, // 齊天大聖
    ShadowOfTheSage = 0x21DA, // 齊天大聖的幻影（分身，共用血量）
    ServantOfTheSage = 0x2232 // 齊天大聖的手下
}

// 契約：這個列舉＝模組知道的技能。列舉以外的敵方讀條由 Components.UnclaimedCastAOEs 照沒有模組時的猜法處理，
// 所以刻意不把其他技能列進來（列了就不再被猜）。
public enum AID : uint
{
    TheShortEnd1 = 11174, // Boss->player, 3.0s cast, single-target tankbuster
    TheShortEnd2 = 11527, // ShadowOfTheSage->player, 3.0s cast, single-target tankbuster
    MountHuaguo1 = 11178, // Boss->self, 3.0s cast, range 60 circle raidwide
    MountHuaguo2 = 11528, // ShadowOfTheSage->self, 3.0s cast, range 60 circle raidwide
    FiveFingeredPunishment1 = 11179, // Boss->player, 5.0s cast, range 6 circle stack
    FiveFingeredPunishment2 = 11184, // ShadowOfTheSage->player, 5.0s cast, range 6 circle stack
    SecondHeaven = 11185 // Boss->self, 4.0s cast, range 40 circle raidwide
}

sealed class TheShortEnd1(BossModule module) : Components.SingleTargetCast(module, (uint)AID.TheShortEnd1);
sealed class TheShortEnd2(BossModule module) : Components.SingleTargetCast(module, (uint)AID.TheShortEnd2);
sealed class MountHuaguo1(BossModule module) : Components.RaidwideCast(module, (uint)AID.MountHuaguo1);
sealed class MountHuaguo2(BossModule module) : Components.RaidwideCast(module, (uint)AID.MountHuaguo2);
sealed class SecondHeaven(BossModule module) : Components.RaidwideCast(module, (uint)AID.SecondHeaven);

// 五行山是分攤；後備猜法會畫成要躲開的圓（AI 反而離開被點名的人）⇒ 認領。
sealed class FiveFingeredPunishment1(BossModule module) : Components.StackWithCastTargets(module, (uint)AID.FiveFingeredPunishment1, 6f);
sealed class FiveFingeredPunishment2(BossModule module) : Components.StackWithCastTargets(module, (uint)AID.FiveFingeredPunishment2, 6f);

sealed class D123QitianDashengStates : StateMachineBuilder
{
    public D123QitianDashengStates(BossModule module) : base(module)
    {
        TrivialPhase()
            .ActivateOnEnter<TheShortEnd1>()
            .ActivateOnEnter<TheShortEnd2>()
            .ActivateOnEnter<MountHuaguo1>()
            .ActivateOnEnter<MountHuaguo2>()
            .ActivateOnEnter<SecondHeaven>()
            .ActivateOnEnter<FiveFingeredPunishment1>()
            .ActivateOnEnter<FiveFingeredPunishment2>()
            .ActivateOnEnter<Components.UnclaimedCastAOEs>()
            .Raw.Update = () => module.PrimaryActor.IsDeadOrDestroyed && module.Enemies((uint)OID.ShadowOfTheSage).All(e => e.IsDeadOrDestroyed);
    }
}

// 場地與尋路沿用沒有模組時的做法（跟著玩家走的方框，套用本副本的障礙物圖）：沒有錄影可量場地。
// 如意大旋風藍杖（貼近王才安全）刻意不做：安全範圍半徑沒有證據；紅杖是一般圓，交給後備猜法。
[ModuleInfo(BossModuleInfo.Maturity.WIP, Contributors = "ffxiv-tc-port", GroupType = BossModuleInfo.GroupType.CFC, GroupID = 536, NameID = 7203)]
public sealed class D123QitianDasheng(WorldState ws, Actor primary) : SimpleBossModule(ws, primary)
{
    // 手下要在集氣滿之前打完 ⇒ 手下優先。
    protected override void CalculateModuleAIHints(int slot, Actor actor, PartyRolesConfig.Assignment assignment, AIHints hints)
    {
        base.CalculateModuleAIHints(slot, actor, assignment, hints);
        var count = hints.PotentialTargets.Count;
        for (var i = 0; i < count; ++i)
        {
            var e = hints.PotentialTargets[i];
            if (e.Actor.OID == (uint)OID.ServantOfTheSage)
                e.Priority = 1;
        }
    }

    protected override void DrawEnemies(int pcSlot, Actor pc)
    {
        Arena.Actor(PrimaryActor);
        Arena.Actors(Enemies((uint)OID.ShadowOfTheSage));
        Arena.Actors(Enemies((uint)OID.ServantOfTheSage));
    }
}
