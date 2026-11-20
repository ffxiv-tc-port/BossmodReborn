namespace BossMod.RealmReborn.Dungeon.D24TamTaraHard.D242SpareBody;

public enum OID : uint
{
    Boss = 0xBED // R?
}

sealed class D242SpareBodyStates : StateMachineBuilder
{
    public D242SpareBodyStates(BossModule module) : base(module)
    {
        TrivialPhase()
            .ActivateOnEnter<Components.UnclaimedCastAOEs>();
    }
}

// 沒有錄影：場地＝碰撞地板裡放得下的最大圓（比房間小，保守側）；讀條範圍技全交給 UnclaimedCastAOEs。
[ModuleInfo(BossModuleInfo.Maturity.WIP, Contributors = "ffxiv-tc-port", GroupType = BossModuleInfo.GroupType.CFC, GroupID = 24, NameID = 2855)]
public sealed class D242SpareBody(WorldState ws, Actor primary) : BossModule(ws, primary, new(88f, -41.5f), new ArenaBoundsCircle(19f));
