namespace BossMod.RealmReborn.Dungeon.D22LostCityOfAmdapor.D221DecayingGourmand;

public enum OID : uint
{
    Boss = 0xA80 // R?
}

sealed class D221DecayingGourmandStates : StateMachineBuilder
{
    public D221DecayingGourmandStates(BossModule module) : base(module)
    {
        TrivialPhase()
            .ActivateOnEnter<Components.UnclaimedCastAOEs>();
    }
}

// 沒有錄影：場地＝碰撞地板裡放得下的最大圓（比房間小，保守側）；讀條範圍技全交給 UnclaimedCastAOEs。
// 刻意不做前方順劈與捕食：哪一招是順劈、被吞後要打的小怪是哪個物件，都沒有證據。
[ModuleInfo(BossModuleInfo.Maturity.WIP, Contributors = "ffxiv-tc-port", GroupType = BossModuleInfo.GroupType.CFC, GroupID = 22, NameID = 2556)]
public sealed class D221DecayingGourmand(WorldState ws, Actor primary) : BossModule(ws, primary, new(98f, -28f), new ArenaBoundsCircle(20f));
