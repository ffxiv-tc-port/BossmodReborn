namespace BossMod.RealmReborn.Dungeon.D23HullbreakerIsle.D231Sasquatch;

// BNpcBase 0xB98 與 0x13DF 同名同模型；取與本副本其他首領連號的 0xB98（0xB9C 克拉肯的腕足、0xBB2 肖魯姆）。
public enum OID : uint
{
    Boss = 0xB98 // R?
}

sealed class D231SasquatchStates : StateMachineBuilder
{
    public D231SasquatchStates(BossModule module) : base(module)
    {
        TrivialPhase()
            .ActivateOnEnter<Components.UnclaimedCastAOEs>();
    }
}

// 沒有錄影：場地＝碰撞地板裡放得下的最大圓（比房間小，保守側）；讀條範圍技全交給 UnclaimedCastAOEs。
// 刻意不做搖香蕉樹的提示：捶胸沒有讀條，提示該顯示多久沒有證據。
[ModuleInfo(BossModuleInfo.Maturity.WIP, Contributors = "ffxiv-tc-port", GroupType = BossModuleInfo.GroupType.CFC, GroupID = 23, NameID = 2901)]
public sealed class D231Sasquatch(WorldState ws, Actor primary) : BossModule(ws, primary, new(220f, -11.5f), new ArenaBoundsCircle(20.5f));
