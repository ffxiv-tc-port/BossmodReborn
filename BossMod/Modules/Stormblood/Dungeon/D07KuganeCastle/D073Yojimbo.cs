namespace BossMod.Stormblood.Dungeon.D07KuganeCastle.D073Yojimbo;

public enum OID : uint
{
    Boss = 0x1A78, // 保鑣
    Daigoro = 0x1A79, // 大五郎
    DragonsHead = 0x1A90, // 龍頭
    Inoshikacho = 0x1A91 // 豬鹿蝶
}

// 契約：這個列舉＝模組知道的技能。列舉以外的敵方讀條由 Components.UnclaimedCastAOEs 照沒有模組時的猜法處理，
// 所以刻意不把其他技能列進來（列了就不再被猜）。
public enum AID : uint
{
    MettaGiri = 7843 // Boss->self, 3.0s cast, range 80 circle raidwide
}

sealed class MettaGiri(BossModule module) : Components.RaidwideCast(module, (uint)AID.MettaGiri);

sealed class D073YojimboStates : StateMachineBuilder
{
    public D073YojimboStates(BossModule module) : base(module)
    {
        TrivialPhase()
            .ActivateOnEnter<MettaGiri>()
            .ActivateOnEnter<Components.UnclaimedCastAOEs>();
    }
}

// 場地與尋路沿用沒有模組時的做法（跟著玩家走的方框）：這個副本沒有障礙物圖，也沒有錄影可量場地。
// 刻意不做撿金幣（金幣物件 ID 沒有證據）與斬魔刀／腰刀（沒有讀條）。
[ModuleInfo(BossModuleInfo.Maturity.WIP, Contributors = "ffxiv-tc-port", GroupType = BossModuleInfo.GroupType.CFC, GroupID = 262, NameID = 6089)]
public sealed class D073Yojimbo(WorldState ws, Actor primary) : SimpleBossModule(ws, primary)
{
    // 雲蒸龍變叫出的龍頭會朝玩家放直線、血量少 ⇒ 龍頭優先。
    protected override void CalculateModuleAIHints(int slot, Actor actor, PartyRolesConfig.Assignment assignment, AIHints hints)
    {
        base.CalculateModuleAIHints(slot, actor, assignment, hints);
        var count = hints.PotentialTargets.Count;
        for (var i = 0; i < count; ++i)
        {
            var e = hints.PotentialTargets[i];
            if (e.Actor.OID == (uint)OID.DragonsHead)
                e.Priority = 1;
        }
    }

    protected override void DrawEnemies(int pcSlot, Actor pc)
    {
        Arena.Actor(PrimaryActor);
        Arena.Actors(Enemies((uint)OID.DragonsHead));
        Arena.Actors(Enemies((uint)OID.Daigoro), Colors.Object);
        Arena.Actors(Enemies((uint)OID.Inoshikacho), Colors.Object);
    }
}
