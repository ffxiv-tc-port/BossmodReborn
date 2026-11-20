namespace BossMod.RealmReborn.Dungeon.D19HaukkeHard.D191ManorJester;

public enum OID : uint
{
    Boss = 0x96C, // R0.75
    ManorSteward = 0x96D // R0.9
}

public enum AID : uint
{
    AutoAttack = 870, // ManorSteward->player, no cast, single-target
    Blizzard = 967, // Boss->player, 1.0s cast, single-target
    VoidBlizzard = 1697, // Boss->player, 2.5s cast, single-target, slow
    VoidBlizzard3 = 1696, // Boss->player, 3.0s cast, single-target, tankbuster
    IceSpikes = 1698 // Boss->location, no cast, range 5 circle at a player, leaves an ice patch
}

sealed class VoidBlizzard3(BossModule module) : Components.SingleTargetCast(module, (uint)AID.VoidBlizzard3);

sealed class D191ManorJesterStates : StateMachineBuilder
{
    public D191ManorJesterStates(BossModule module) : base(module)
    {
        TrivialPhase()
            .ActivateOnEnter<VoidBlizzard3>()
            .ActivateOnEnter<Components.UnclaimedCastAOEs>();
    }
}

// 「死者之舞」（隱形施放者的 8 碼圓、會復活圈內的管家）刻意不列，留給 UnclaimedCastAOEs 照沒有模組時的猜法處理。
// 場地＝錄影裡戰鬥範圍內、經碰撞驗過全是地板的矩形（二樓大廳，比房間小）。
[ModuleInfo(BossModuleInfo.Maturity.WIP, Contributors = "ffxiv-tc-port", GroupType = BossModuleInfo.GroupType.CFC, GroupID = 19, NameID = 426)]
public sealed class D191ManorJester(WorldState ws, Actor primary) : BossModule(ws, primary, new(0.5f, 6f), new ArenaBoundsRect(14.5f, 7f));
