namespace BossMod.Stormblood.Dungeon.D07KuganeCastle.D071ZuikoMaru;

public enum OID : uint
{
    Boss = 0x1A74, // 瑞光丸
    HarakiriKosho = 0x1A75 // 切腹小姓
}

// 契約：這個列舉＝模組知道的技能。列舉以外的敵方讀條由 Components.UnclaimedCastAOEs 照沒有模組時的猜法處理，
// 所以刻意不把其他技能列進來（列了就不再被猜）。
public enum AID : uint
{
    KenkiRelease = 7827, // Boss->self, 3.0s cast, range 50 circle raidwide
    HelmCrack = 7828 // Boss->player, 5.0s cast, range 6 circle stack
}

sealed class KenkiRelease(BossModule module) : Components.RaidwideCast(module, (uint)AID.KenkiRelease);

// 破盔是分攤；後備猜法會畫成要躲開的圓（AI 反而離開被點名的人）⇒ 認領。
sealed class HelmCrack(BossModule module) : Components.StackWithCastTargets(module, (uint)AID.HelmCrack, 6f);

sealed class D071ZuikoMaruStates : StateMachineBuilder
{
    public D071ZuikoMaruStates(BossModule module) : base(module)
    {
        TrivialPhase()
            .ActivateOnEnter<KenkiRelease>()
            .ActivateOnEnter<HelmCrack>()
            .ActivateOnEnter<Components.UnclaimedCastAOEs>();
    }
}

// 場地與尋路沿用沒有模組時的做法（跟著玩家走的方框）：這個副本沒有障礙物圖，也沒有錄影可量場地。
// 切腹小姓刻意不設優先：打倒後原地爆炸（範圍 20），由後備猜法照讀條畫範圍。
[ModuleInfo(BossModuleInfo.Maturity.WIP, Contributors = "ffxiv-tc-port", GroupType = BossModuleInfo.GroupType.CFC, GroupID = 262, NameID = 6085)]
public sealed class D071ZuikoMaru(WorldState ws, Actor primary) : SimpleBossModule(ws, primary)
{
    protected override void DrawEnemies(int pcSlot, Actor pc)
    {
        Arena.Actor(PrimaryActor);
        Arena.Actors(Enemies((uint)OID.HarakiriKosho));
    }
}
