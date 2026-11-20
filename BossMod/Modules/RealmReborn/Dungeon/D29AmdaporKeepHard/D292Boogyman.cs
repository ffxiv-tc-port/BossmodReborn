namespace BossMod.RealmReborn.Dungeon.D29AmdaporKeepHard.D292Boogyman;

// 低血量時分裂成兩隻，兩個 OID 同名同資料、哪個先出現沒有證據；取較小的當首領，另一個一起畫出來。
public enum OID : uint
{
    Boss = 0xD62, // 夜魔人
    Twin = 0xD63, // 夜魔人
    Luminescence = 0xD64 // 幻光球
}

// 沒有列技能：空虛眼瞳 3173 是凝視（VFX 25），後備猜法已照凝視處理，交給 UnclaimedCastAOEs。
sealed class D292BoogymanStates : StateMachineBuilder
{
    public D292BoogymanStates(BossModule module) : base(module)
    {
        TrivialPhase()
            .ActivateOnEnter<Components.UnclaimedCastAOEs>()
            .Raw.Update = () => module.PrimaryActor.IsDeadOrDestroyed && module.Enemies((uint)OID.Twin).All(e => e.IsDeadOrDestroyed);
    }
}

// 場地與尋路沿用沒有模組時的做法（跟著玩家走的方框）：這個副本沒有障礙物圖，也沒有錄影可量場地。
[ModuleInfo(BossModuleInfo.Maturity.WIP, Contributors = "ffxiv-tc-port", GroupType = BossModuleInfo.GroupType.CFC, GroupID = 29, NameID = 3274)]
public sealed class D292Boogyman(WorldState ws, Actor primary) : SimpleBossModule(ws, primary)
{
    // 夜魔人隱形時要先打掉幻光球、站進它的範圍才看得見首領 ⇒ 幻光球優先。
    protected override void CalculateModuleAIHints(int slot, Actor actor, PartyRolesConfig.Assignment assignment, AIHints hints)
    {
        base.CalculateModuleAIHints(slot, actor, assignment, hints);
        var count = hints.PotentialTargets.Count;
        for (var i = 0; i < count; ++i)
        {
            var e = hints.PotentialTargets[i];
            if (e.Actor.OID == (uint)OID.Luminescence)
                e.Priority = 1;
        }
    }

    protected override void DrawEnemies(int pcSlot, Actor pc)
    {
        Arena.Actor(PrimaryActor);
        Arena.Actors(Enemies((uint)OID.Twin));
        Arena.Actors(Enemies((uint)OID.Luminescence), Colors.Object);
    }
}
