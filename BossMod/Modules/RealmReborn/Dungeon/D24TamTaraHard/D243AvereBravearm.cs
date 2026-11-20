namespace BossMod.RealmReborn.Dungeon.D24TamTaraHard.D243AvereBravearm;

public enum OID : uint
{
    Boss = 0xBF3 // R?
}

sealed class D243AvereBravearmStates : StateMachineBuilder
{
    public D243AvereBravearmStates(BossModule module) : base(module)
    {
        TrivialPhase()
            .ActivateOnEnter<Components.UnclaimedCastAOEs>();
    }
}

// 沒有錄影：場地＝碰撞地板裡放得下的最大圓（比房間小，保守側）；讀條範圍技全交給 UnclaimedCastAOEs。
// 刻意不做中央艾妲腳下的出血圈：圈的物件與半徑都沒有證據。
[ModuleInfo(BossModuleInfo.Maturity.WIP, Contributors = "ffxiv-tc-port", GroupType = BossModuleInfo.GroupType.CFC, GroupID = 24, NameID = 2860)]
public sealed class D243AvereBravearm(WorldState ws, Actor primary) : BossModule(ws, primary, new(-50f, -13.5f), new ArenaBoundsCircle(17.5f));
