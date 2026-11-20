namespace BossMod.RealmReborn.Dungeon.D30WanderersPalaceHard.D303ManxomeMolaaJaJa;

public enum OID : uint
{
    Boss = 0xDCE, // 光鱗兄弟 莫拉加加
    SacredIdol = 0xDD0 // 莫拉加加的祭器
}

// 契約：這個列舉＝模組知道的技能。列舉以外的敵方讀條由 Components.UnclaimedCastAOEs 照沒有模組時的猜法處理，
// 所以刻意不把其他技能列進來（列了就不再被猜）。
public enum AID : uint
{
    SoulDouse = 3230 // Boss->self, 3.0s cast, gaze, inflicts Doom on players facing the caster
}

// 靈魂熄滅的 Action 表沒有凝視旗標（VFX 不是 25），後備猜法不會當凝視處理 ⇒ 模組自己背對。
sealed class SoulDouse(BossModule module) : Components.CastGaze(module, (uint)AID.SoulDouse);

sealed class D303ManxomeMolaaJaJaStates : StateMachineBuilder
{
    public D303ManxomeMolaaJaJaStates(BossModule module) : base(module)
    {
        TrivialPhase()
            .ActivateOnEnter<SoulDouse>()
            .ActivateOnEnter<Components.UnclaimedCastAOEs>();
    }
}

// 場地與尋路沿用沒有模組時的做法（跟著玩家走的方框）：這個副本沒有障礙物圖，也沒有錄影可量場地。
[ModuleInfo(BossModuleInfo.Maturity.WIP, Contributors = "ffxiv-tc-port", GroupType = BossModuleInfo.GroupType.CFC, GroupID = 30, NameID = 3095)]
public sealed class D303ManxomeMolaaJaJa(WorldState ws, Actor primary) : SimpleBossModule(ws, primary)
{
    // 祭器出現並連線首領時要盡快打掉 ⇒ 祭器優先。
    protected override void CalculateModuleAIHints(int slot, Actor actor, PartyRolesConfig.Assignment assignment, AIHints hints)
    {
        base.CalculateModuleAIHints(slot, actor, assignment, hints);
        var count = hints.PotentialTargets.Count;
        for (var i = 0; i < count; ++i)
        {
            var e = hints.PotentialTargets[i];
            if (e.Actor.OID == (uint)OID.SacredIdol)
                e.Priority = 1;
        }
    }

    protected override void DrawEnemies(int pcSlot, Actor pc)
    {
        Arena.Actor(PrimaryActor);
        Arena.Actors(Enemies((uint)OID.SacredIdol));
    }
}
