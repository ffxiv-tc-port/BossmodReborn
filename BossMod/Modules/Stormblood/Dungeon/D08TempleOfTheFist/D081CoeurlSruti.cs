namespace BossMod.Stormblood.Dungeon.D08TempleOfTheFist.D081CoeurlSruti;

public enum OID : uint
{
    Boss = 0x1B53, // 凶豹所聞
    CoeurlSmriti = 0x1B54 // 凶豹所憶，戰鬥中途加入
}

// 契約：這個列舉＝模組知道的技能。列舉以外的敵方讀條由 Components.UnclaimedCastAOEs 照沒有模組時的猜法處理，
// 所以刻意不把其他技能列進來（列了就不再被猜）。
public enum AID : uint
{
    ElectricBurst = 8150, // Boss/CoeurlSmriti->self, 4.0s cast, range 80 circle raidwide
    HeatLightning = 8152 // Boss/CoeurlSmriti->player, 4.0s cast, range 5 circle spread
}

sealed class ElectricBurst(BossModule module) : Components.RaidwideCast(module, (uint)AID.ElectricBurst);

// 驚電打在被點名的玩家身上；後備猜法會畫成讀條開始當下的固定圓 ⇒ 認領成散開。
sealed class HeatLightning(BossModule module) : Components.SpreadFromCastTargets(module, (uint)AID.HeatLightning, 5f);

sealed class D081CoeurlSrutiStates : StateMachineBuilder
{
    public D081CoeurlSrutiStates(BossModule module) : base(module)
    {
        TrivialPhase()
            .ActivateOnEnter<ElectricBurst>()
            .ActivateOnEnter<HeatLightning>()
            .ActivateOnEnter<Components.UnclaimedCastAOEs>()
            .Raw.Update = () => module.PrimaryActor.IsDeadOrDestroyed && module.Enemies((uint)OID.CoeurlSmriti).All(e => e.IsDeadOrDestroyed);
    }
}

// 場地與尋路沿用沒有模組時的做法（跟著玩家走的方框）：這個副本沒有障礙物圖，也沒有錄影可量場地。
[ModuleInfo(BossModuleInfo.Maturity.WIP, Contributors = "ffxiv-tc-port", GroupType = BossModuleInfo.GroupType.CFC, GroupID = 236, NameID = 6119)]
public sealed class D081CoeurlSruti(WorldState ws, Actor primary) : SimpleBossModule(ws, primary)
{
    protected override void DrawEnemies(int pcSlot, Actor pc)
    {
        Arena.Actor(PrimaryActor);
        Arena.Actors(Enemies((uint)OID.CoeurlSmriti));
    }
}
