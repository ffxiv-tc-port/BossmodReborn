namespace BossMod.QuestBattle.ARealmReborn.ClassJobQuests.LNC;

// EXD 驗證(exd-tc/7.20)：ContentFinderCondition #307 ContentLinkType=5、TerritoryType 237、
// QuestBattle #7 -> Quest 65591 ClsLnc003_00055「實力的證明」(槍術士 Lv15)。
[ZoneModuleInfo(BossModuleInfo.Maturity.Contributed, 307)]
internal class ADangerousProposition(WorldState ws) : QuestBattle(ws)
{
    public override List<QuestObjective> DefineObjectives(WorldState ws) => [
        new QuestObjective(ws)
            .WithConnection(new Vector3(304.20f, -1.18f, -285.26f))
            .Hints((player, hints) => hints.PrioritizeAll())
            .CompleteOnKilled(0x21F)
    ];
}
