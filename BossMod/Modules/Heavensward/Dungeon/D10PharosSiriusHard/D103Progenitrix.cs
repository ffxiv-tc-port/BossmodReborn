namespace BossMod.Heavensward.Dungeon.D10PharosSiriusHard.D103Progenitrix;

public enum OID : uint
{
    Boss = 0x1447, // 炸彈之母
    Progenitor = 0x1448, // 炸彈之父
    GreyBomb1 = 0x144A, // 灰色炸彈
    GreyBomb2 = 0x14C9 // 灰色炸彈
}

// 沒有列技能：讀條範圍交給 UnclaimedCastAOEs 照後備猜法處理。
sealed class D103ProgenitrixStates : StateMachineBuilder
{
    public D103ProgenitrixStates(BossModule module) : base(module)
    {
        TrivialPhase()
            .ActivateOnEnter<Components.UnclaimedCastAOEs>();
    }
}

// 炸彈之母倒下後炸彈之父接著打（攻略），兩者是不同的 OID ⇒ 各自一個模組、共用狀態與優先度。
public abstract class D103ProgenitrixBase(WorldState ws, Actor primary) : SimpleBossModule(ws, primary)
{
    // 攻略：灰色炸彈要立刻打掉。
    protected override void CalculateModuleAIHints(int slot, Actor actor, PartyRolesConfig.Assignment assignment, AIHints hints)
    {
        base.CalculateModuleAIHints(slot, actor, assignment, hints);
        var count = hints.PotentialTargets.Count;
        for (var i = 0; i < count; ++i)
        {
            var e = hints.PotentialTargets[i];
            if (e.Actor.OID is (uint)OID.GreyBomb1 or (uint)OID.GreyBomb2)
                e.Priority = 1;
        }
    }

    protected override void DrawEnemies(int pcSlot, Actor pc)
    {
        Arena.Actor(PrimaryActor);
        Arena.Actors(Enemies((uint)OID.GreyBomb1));
        Arena.Actors(Enemies((uint)OID.GreyBomb2));
    }
}

// 場地與尋路沿用沒有模組時的做法（跟著玩家走的方框）：這個副本沒有障礙物圖，也沒有錄影可量場地。
[ModuleInfo(BossModuleInfo.Maturity.WIP, Contributors = "ffxiv-tc-port", GroupType = BossModuleInfo.GroupType.CFC, GroupID = 40, NameID = 4575)]
public sealed class D103Progenitrix(WorldState ws, Actor primary) : D103ProgenitrixBase(ws, primary);

[ModuleInfo(BossModuleInfo.Maturity.WIP, Contributors = "ffxiv-tc-port", GroupType = BossModuleInfo.GroupType.CFC, GroupID = 40, NameID = 4576,
    PrimaryActorOID = (uint)OID.Progenitor, StatesType = typeof(D103ProgenitrixStates))]
public sealed class D103Progenitor(WorldState ws, Actor primary) : D103ProgenitrixBase(ws, primary);
