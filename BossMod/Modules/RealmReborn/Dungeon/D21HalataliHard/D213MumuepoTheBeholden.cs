namespace BossMod.RealmReborn.Dungeon.D21HalataliHard.D213MumuepoTheBeholden;

public enum OID : uint
{
    Boss = 0xAA4, // R?
    Narasimha = 0xACC // R?
}

public enum AID : uint
{
    Standstill = 2127 // Boss->self, 4.0s cast, binds players in place
}

sealed class Standstill(BossModule module) : Components.CastHint(module, (uint)AID.Standstill, "Free bound players with the nail (Thal's Scepter)!");

sealed class D213MumuepoTheBeholdenStates : StateMachineBuilder
{
    public D213MumuepoTheBeholdenStates(BossModule module) : base(module)
    {
        TrivialPhase()
            .ActivateOnEnter<Standstill>()
            .ActivateOnEnter<Components.UnclaimedCastAOEs>();
    }
}

// 沒有錄影：場地＝碰撞地板裡放得下的最大圓（比房間小，保守側）；其餘讀條範圍技交給 UnclaimedCastAOEs。
[ModuleInfo(BossModuleInfo.Maturity.WIP, Contributors = "ffxiv-tc-port", GroupType = BossModuleInfo.GroupType.CFC, GroupID = 21, NameID = 2602)]
public sealed class D213MumuepoTheBeholden(WorldState ws, Actor primary) : BossModule(ws, primary, new(-257f, 18.5f), new ArenaBoundsCircle(23f))
{
    protected override void DrawEnemies(int pcSlot, Actor pc)
    {
        Arena.Actor(PrimaryActor);
        Arena.Actors(Enemies((uint)OID.Narasimha));
    }
}
