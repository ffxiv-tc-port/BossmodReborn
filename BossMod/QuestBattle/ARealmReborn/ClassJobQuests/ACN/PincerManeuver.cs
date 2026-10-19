namespace BossMod.QuestBattle.ARealmReborn.ClassJobQuests.ACN;

// EXD 驗證(exd-tc/7.20)：ContentFinderCondition #347 ContentLinkType=5、TerritoryType 287、
// QuestBattle #52 -> Quest 65995 ClsAcn200_00459「固執的夾擊戰術」(秘術師 Lv20)。
[ZoneModuleInfo(BossModuleInfo.Maturity.Contributed, 347)]
internal class PincerManeuver(WorldState ws) : QuestBattle(ws)
{
    public override List<QuestObjective> DefineObjectives(WorldState ws) => [
        new QuestObjective(ws)
            .Hints((player, hints) => hints.PrioritizeAll())
            .PauseForCombat(true)
            .WithConnection(new Vector3(25.86f, 71.30f, 399.76f))
            .CompleteAtDestination(),
        new QuestObjective(ws)
            .Hints((player, hints) => hints.PrioritizeAll())
            .PauseForCombat(true)
            .WithConnection(new Vector3(-10.21f, 67.50f, 416.09f))
            .WithInteract(0x1E8E59)
    ];
}
