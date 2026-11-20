namespace BossMod.RealmReborn.Dungeon.D20BrayfloxHard.D202MagitekVangobGIII;

public enum OID : uint
{
    Boss = 0xAE9 // R?
}

sealed class D202MagitekVangobGIIIStates : StateMachineBuilder
{
    public D202MagitekVangobGIIIStates(BossModule module) : base(module)
    {
        TrivialPhase()
            .ActivateOnEnter<Components.UnclaimedCastAOEs>();
    }
}

// 沒有錄影：場地＝碰撞地板裡放得下的最大圓（比房間小，保守側）；讀條範圍技全交給 UnclaimedCastAOEs。
// 刻意不做青磷水窪：水窪的物件 ID 沒有證據。
[ModuleInfo(BossModuleInfo.Maturity.WIP, Contributors = "ffxiv-tc-port", GroupType = BossModuleInfo.GroupType.CFC, GroupID = 20, NameID = 2549)]
public sealed class D202MagitekVangobGIII(WorldState ws, Actor primary) : BossModule(ws, primary, new(-8f, -162f), new ArenaBoundsCircle(19f));
