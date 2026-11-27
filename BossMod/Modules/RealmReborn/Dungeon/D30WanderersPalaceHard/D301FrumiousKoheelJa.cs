namespace BossMod.RealmReborn.Dungeon.D30WanderersPalaceHard.D301FrumiousKoheelJa;

public enum OID : uint
{
    Boss = 0xDCA, // 折角騎士 寇黑加
    FireAngon = 0xDCB // 光鱗戰鬥團的投槍：烈火投槍插下的長槍，活著約 7 秒後全體傷害並附加火傷
}

// 契約：這個列舉＝模組知道的技能；列舉以外的敵方讀條由 Components.UnclaimedCastAOEs 照沒有模組時的猜法處理。
// 只列沒有讀條的招式，讀條招式（烈火投槍 3213、燃燒的記憶 3214）刻意留給後備猜法。
public enum AID : uint
{
    Gore = 3211, // Boss->self, no cast, cone 5+R on main target
    FireAngonBlast = 3216 // FireAngon->self, no cast, range 50 circle (spear left alive)
}

// 無讀條、朝主目標的正面順劈（5+R3.6）；打中最寬 31 度、64 度外沒中，取半角 45 度。
sealed class Gore(BossModule module) : Components.Cleave(module, (uint)AID.Gore, new AOEShapeCone(8.6f, 45f.Degrees()));

sealed class D301FrumiousKoheelJaStates : StateMachineBuilder
{
    public D301FrumiousKoheelJaStates(BossModule module) : base(module)
    {
        TrivialPhase()
            .ActivateOnEnter<Gore>()
            .ActivateOnEnter<Components.UnclaimedCastAOEs>();
    }
}

// 場地沿用沒有模組時的做法（跟著玩家走的方框）；牆由整座副本的障礙物圖（188.30）提供。
[ModuleInfo(BossModuleInfo.Maturity.WIP, Contributors = "ffxiv-tc-port", GroupType = BossModuleInfo.GroupType.CFC, GroupID = 30, NameID = 3091)]
public sealed class D301FrumiousKoheelJa(WorldState ws, Actor primary) : SimpleBossModule(ws, primary)
{
    // 長槍活著就會全體傷害並疊火傷 ⇒ 長槍優先。
    protected override void CalculateModuleAIHints(int slot, Actor actor, PartyRolesConfig.Assignment assignment, AIHints hints)
    {
        base.CalculateModuleAIHints(slot, actor, assignment, hints);
        var count = hints.PotentialTargets.Count;
        for (var i = 0; i < count; ++i)
        {
            var e = hints.PotentialTargets[i];
            if (e.Actor.OID == (uint)OID.FireAngon)
                e.Priority = 1;
        }
    }

    protected override void DrawEnemies(int pcSlot, Actor pc)
    {
        Arena.Actor(PrimaryActor);
        Arena.Actors(Enemies((uint)OID.FireAngon));
    }
}
