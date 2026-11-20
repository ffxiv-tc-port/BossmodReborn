namespace BossMod.Stormblood.Dungeon.D11FractalContinuumHard.D111Motherbit;

public enum OID : uint
{
    Boss = 0x20C6, // 浮游砲主機板
    PrototypeBit = 0x20C7 // 原型浮游砲
}

// 沒有列技能：有讀條的招式（攻城砲、亞拉戈重力、擴散雷射）後備猜法照 Action 表畫，交給 UnclaimedCastAOEs。
sealed class D111MotherbitStates : StateMachineBuilder
{
    public D111MotherbitStates(BossModule module) : base(module)
    {
        TrivialPhase()
            .ActivateOnEnter<Components.UnclaimedCastAOEs>();
    }
}

// 場地與尋路沿用沒有模組時的做法（跟著玩家走的方框）：這個副本沒有障礙物圖，也沒有錄影可量場地。
// 刻意不做亞拉戈重力留下的黑水窪與攻城砲時浮游砲腳下的地面線（物件 ID 沒有證據）。
[ModuleInfo(BossModuleInfo.Maturity.WIP, Contributors = "ffxiv-tc-port", GroupType = BossModuleInfo.GroupType.CFC, GroupID = 285, NameID = 7056)]
public sealed class D111Motherbit(WorldState ws, Actor primary) : SimpleBossModule(ws, primary)
{
    protected override void DrawEnemies(int pcSlot, Actor pc)
    {
        Arena.Actor(PrimaryActor);
        Arena.Actors(Enemies((uint)OID.PrototypeBit), Colors.Object);
    }
}
