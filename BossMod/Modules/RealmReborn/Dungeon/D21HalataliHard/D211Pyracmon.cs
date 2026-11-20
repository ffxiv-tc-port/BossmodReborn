namespace BossMod.RealmReborn.Dungeon.D21HalataliHard.D211Pyracmon;

public enum OID : uint
{
    Boss = 0xAC5 // R?
}

public enum AID : uint
{
    ThousandTonzeSwing = 2075 // Boss->self, 16.0s cast, range 20+R circle, covers the whole arena; mitigated by the Manawall buff from the lever
}

// 範圍蓋滿整個場地，躲不掉；認領它是為了不讓後備猜法把 AI 往場邊趕（離拉桿更遠）。
sealed class ThousandTonzeSwing(BossModule module) : Components.CastHint(module, (uint)AID.ThousandTonzeSwing, "Use the lever for Manawall!", true);

sealed class D211PyracmonStates : StateMachineBuilder
{
    public D211PyracmonStates(BossModule module) : base(module)
    {
        TrivialPhase()
            .ActivateOnEnter<ThousandTonzeSwing>()
            .ActivateOnEnter<Components.UnclaimedCastAOEs>();
    }
}

// 沒有錄影：場地＝碰撞地板裡放得下的最大圓（比房間小，保守側）；其餘讀條範圍技交給 UnclaimedCastAOEs。
[ModuleInfo(BossModuleInfo.Maturity.WIP, Contributors = "ffxiv-tc-port", GroupType = BossModuleInfo.GroupType.CFC, GroupID = 21, NameID = 2597)]
public sealed class D211Pyracmon(WorldState ws, Actor primary) : BossModule(ws, primary, new(-68f, -104f), new ArenaBoundsCircle(19f));
