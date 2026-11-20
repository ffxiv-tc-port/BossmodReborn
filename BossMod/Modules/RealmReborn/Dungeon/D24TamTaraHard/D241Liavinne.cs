namespace BossMod.RealmReborn.Dungeon.D24TamTaraHard.D241Liavinne;

public enum OID : uint
{
    Boss = 0xBEA // R?
}

sealed class D241LiavinneStates : StateMachineBuilder
{
    public D241LiavinneStates(BossModule module) : base(module)
    {
        TrivialPhase()
            .ActivateOnEnter<Components.UnclaimedCastAOEs>();
    }
}

// 沒有錄影：場地＝碰撞地板裡放得下的最大圓（比房間小，保守側）；讀條範圍技全交給 UnclaimedCastAOEs。
// 刻意不做非人暴行的集合：沒有讀條，集合範圍與持續時間都沒有證據。
[ModuleInfo(BossModuleInfo.Maturity.WIP, Contributors = "ffxiv-tc-port", GroupType = BossModuleInfo.GroupType.CFC, GroupID = 24, NameID = 2852)]
public sealed class D241Liavinne(WorldState ws, Actor primary) : BossModule(ws, primary, new(33f, 90.5f), new ArenaBoundsCircle(19f));
