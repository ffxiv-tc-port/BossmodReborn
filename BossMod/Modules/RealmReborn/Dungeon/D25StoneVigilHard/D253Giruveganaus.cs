namespace BossMod.RealmReborn.Dungeon.D25StoneVigilHard.D253Giruveganaus;

// 本體與約 60% 血量時出現的分身同名同資料，兩個 OID 哪個先出現沒有證據；取較小的當首領，另一個一起畫出來。
public enum OID : uint
{
    Boss = 0xBBF, // 吉魯維伽諾斯
    Twin = 0xBC0 // 吉魯維伽諾斯
}

// 沒有列技能：讀條招式（撕裂利爪、火球、尾部碎擊、狂野蓄力）的形狀後備猜法已能照 EXD 畫出，交給 UnclaimedCastAOEs。
sealed class D253GiruveganausStates : StateMachineBuilder
{
    public D253GiruveganausStates(BossModule module) : base(module)
    {
        TrivialPhase()
            .ActivateOnEnter<Components.UnclaimedCastAOEs>()
            .Raw.Update = () => module.PrimaryActor.IsDeadOrDestroyed && module.Enemies((uint)OID.Twin).All(e => e.IsDeadOrDestroyed);
    }
}

// 場地與尋路沿用沒有模組時的做法（跟著玩家走的方框）：這個副本沒有障礙物圖，也沒有錄影可量場地。
[ModuleInfo(BossModuleInfo.Maturity.WIP, Contributors = "ffxiv-tc-port", GroupType = BossModuleInfo.GroupType.CFC, GroupID = 25, NameID = 2774)]
public sealed class D253Giruveganaus(WorldState ws, Actor primary) : SimpleBossModule(ws, primary)
{
    protected override void DrawEnemies(int pcSlot, Actor pc)
    {
        Arena.Actor(PrimaryActor);
        Arena.Actors(Enemies((uint)OID.Twin));
    }
}
