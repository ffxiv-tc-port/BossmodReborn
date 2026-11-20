namespace BossMod.Stormblood.Dungeon.D12SwallowsCompass.D121Otengu;

public enum OID : uint
{
    Boss = 0x21D6, // 大天狗
    TenguEmber = 0x21D7 // 天狗火
}

// 契約：這個列舉＝模組知道的技能。列舉以外的敵方讀條由 Components.UnclaimedCastAOEs 照沒有模組時的猜法處理，
// 所以刻意不把其他技能列進來（列了就不再被猜）。
public enum AID : uint
{
    MightOfTheTengu = 11156, // Boss->player, 3.5s cast, single-target tankbuster
    CloutOfTheTengu = 11157, // Boss->self, 3.0s cast, range 40 circle raidwide
    WileOfTheTengu = 11159 // Boss->self, 3.0s cast, range 40 circle gaze
}

sealed class MightOfTheTengu(BossModule module) : Components.SingleTargetCast(module, (uint)AID.MightOfTheTengu);
sealed class CloutOfTheTengu(BossModule module) : Components.RaidwideCast(module, (uint)AID.CloutOfTheTengu);
sealed class WileOfTheTengu(BossModule module) : Components.CastGaze(module, (uint)AID.WileOfTheTengu);

sealed class D121OtenguStates : StateMachineBuilder
{
    public D121OtenguStates(BossModule module) : base(module)
    {
        TrivialPhase()
            .ActivateOnEnter<MightOfTheTengu>()
            .ActivateOnEnter<CloutOfTheTengu>()
            .ActivateOnEnter<WileOfTheTengu>()
            .ActivateOnEnter<Components.UnclaimedCastAOEs>();
    }
}

// 場地與尋路沿用沒有模組時的做法（跟著玩家走的方框，套用本副本的障礙物圖）：沒有錄影可量場地。
// 刻意不做天狗火的移動路線（碰到才扣血，路線沒有證據）；它最後的怨念之火有讀條，交給後備猜法。
[ModuleInfo(BossModuleInfo.Maturity.WIP, Contributors = "ffxiv-tc-port", GroupType = BossModuleInfo.GroupType.CFC, GroupID = 536, NameID = 7200)]
public sealed class D121Otengu(WorldState ws, Actor primary) : SimpleBossModule(ws, primary)
{
    protected override void DrawEnemies(int pcSlot, Actor pc)
    {
        Arena.Actor(PrimaryActor);
        Arena.Actors(Enemies((uint)OID.TenguEmber), Colors.Object);
    }
}
