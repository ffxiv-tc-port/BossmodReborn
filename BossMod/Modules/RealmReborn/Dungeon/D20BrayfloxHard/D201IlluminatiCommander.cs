namespace BossMod.RealmReborn.Dungeon.D20BrayfloxHard.D201IlluminatiCommander;

public enum OID : uint
{
    Boss = 0xAE7 // R?
}

sealed class D201IlluminatiCommanderStates : StateMachineBuilder
{
    public D201IlluminatiCommanderStates(BossModule module) : base(module)
    {
        TrivialPhase()
            .ActivateOnEnter<Components.UnclaimedCastAOEs>();
    }
}

// 沒有錄影：場地＝碰撞地板裡放得下的最大圓（比房間小，保守側）；讀條範圍技全交給 UnclaimedCastAOEs。
[ModuleInfo(BossModuleInfo.Maturity.WIP, Contributors = "ffxiv-tc-port", GroupType = BossModuleInfo.GroupType.CFC, GroupID = 20, NameID = 2548)]
public sealed class D201IlluminatiCommander(WorldState ws, Actor primary) : BossModule(ws, primary, new(114f, -26.5f), new ArenaBoundsCircle(15.5f));
