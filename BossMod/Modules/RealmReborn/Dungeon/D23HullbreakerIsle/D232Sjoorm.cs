namespace BossMod.RealmReborn.Dungeon.D23HullbreakerIsle.D232Sjoorm;

public enum OID : uint
{
    Boss = 0xBB2 // R?
}

sealed class D232SjoormStates : StateMachineBuilder
{
    public D232SjoormStates(BossModule module) : base(module)
    {
        TrivialPhase()
            .ActivateOnEnter<Components.UnclaimedCastAOEs>();
    }
}

// 沒有錄影：場地＝碰撞地板裡放得下的最大圓（比房間小，保守側）；讀條範圍技全交給 UnclaimedCastAOEs。
// 刻意不做大漩渦與水牢：沒有讀條也沒有範圍資料。
[ModuleInfo(BossModuleInfo.Maturity.WIP, Contributors = "ffxiv-tc-port", GroupType = BossModuleInfo.GroupType.CFC, GroupID = 23, NameID = 2903)]
public sealed class D232Sjoorm(WorldState ws, Actor primary) : BossModule(ws, primary, new(-80.5f, -124f), new ArenaBoundsCircle(18f));
