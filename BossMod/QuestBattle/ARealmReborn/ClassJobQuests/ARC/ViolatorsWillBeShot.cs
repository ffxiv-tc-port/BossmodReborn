namespace BossMod.QuestBattle.ARealmReborn.ClassJobQuests.ARC;

// EXD 驗證(exd-tc/7.20)：ContentFinderCondition #302 ContentLinkType=5、TerritoryType 228、
// QuestBattle #6 -> Quest 65604 ClsArc003_00068「西爾瓦爾的弓術訓練」(弓箭手 Lv15)。
[ZoneModuleInfo(BossModuleInfo.Maturity.Contributed, 302)]
internal class ViolatorsWillBeShot(WorldState ws) : QuestBattle(ws)
{
    public override List<QuestObjective> DefineObjectives(WorldState ws) => [
        new QuestObjective(ws)
            .WithConnection(new Vector3(404.64f, -5.45f, 68.67f))
            .PauseForCombat(false)
            .Hints((player, hints) => hints.PrioritizeTargetsByOID(0x5AB, 1))
            .CompleteOnKilled(0x5AB)
    ];
}
