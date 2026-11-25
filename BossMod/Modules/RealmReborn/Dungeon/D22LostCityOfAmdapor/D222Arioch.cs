namespace BossMod.RealmReborn.Dungeon.D22LostCityOfAmdapor.D222Arioch;

public enum OID : uint
{
    Boss = 0xA8D // R4.0
}

public enum AID : uint
{
    EerieSoundwave = 1902, // Boss->self, 3.5s cast, raidwide
    Dive = 1899, // Boss->self, no cast, range 6+R circle where the boss lands (not handled: lands wherever its untargetable flight path ends, no telegraph)
    Storm = 1900 // Boss->self, no cast, cone on the main target (not handled: only the tank was ever hit, angle unknown)
}

sealed class EerieSoundwave(BossModule module) : Components.RaidwideCast(module, (uint)AID.EerieSoundwave);

sealed class D222AriochStates : StateMachineBuilder
{
    public D222AriochStates(BossModule module) : base(module)
    {
        TrivialPhase()
            .ActivateOnEnter<EerieSoundwave>()
            .ActivateOnEnter<Components.UnclaimedCastAOEs>();
    }
}

// 場地＝高台那一層碰撞地板裡放得下的最大圓（比高台小，保守側）；其餘讀條範圍技交給 UnclaimedCastAOEs。
// 刻意不做馴養瓦魔蛾倒下時的妖鱗粉：沒有讀條、形狀（扇形角度）沒有足夠證據。
[ModuleInfo(BossModuleInfo.Maturity.Contributed, Contributors = "ffxiv-tc-port", GroupType = BossModuleInfo.GroupType.CFC, GroupID = 22, NameID = 2560)]
public sealed class D222Arioch(WorldState ws, Actor primary) : BossModule(ws, primary, new(-20f, 70.5f), new ArenaBoundsCircle(18f));
