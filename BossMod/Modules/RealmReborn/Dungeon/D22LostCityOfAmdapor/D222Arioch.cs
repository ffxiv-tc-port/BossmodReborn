namespace BossMod.RealmReborn.Dungeon.D22LostCityOfAmdapor.D222Arioch;

public enum OID : uint
{
    Boss = 0xA8D // R?
}

sealed class D222AriochStates : StateMachineBuilder
{
    public D222AriochStates(BossModule module) : base(module)
    {
        TrivialPhase()
            .ActivateOnEnter<Components.UnclaimedCastAOEs>();
    }
}

// 沒有錄影：場地＝高台那一層碰撞地板裡放得下的最大圓（比高台小，保守側）；讀條範圍技全交給 UnclaimedCastAOEs。
[ModuleInfo(BossModuleInfo.Maturity.WIP, Contributors = "ffxiv-tc-port", GroupType = BossModuleInfo.GroupType.CFC, GroupID = 22, NameID = 2560)]
public sealed class D222Arioch(WorldState ws, Actor primary) : BossModule(ws, primary, new(-20f, 70.5f), new ArenaBoundsCircle(18f));
