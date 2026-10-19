namespace BossMod.QuestBattle.ARealmReborn.ClassJobQuests.THM;

// EXD 驗證(exd-tc/7.20)：ContentFinderCondition #324 ContentLinkType=5、TerritoryType 266、
// QuestBattle #40 -> Quest 65886 ClsThm150_00350「狂猛之危」(咒術士 Lv15)。
[ZoneModuleInfo(BossModuleInfo.Maturity.Contributed, 324)]
internal class TheThreatOfSuperiority(WorldState ws) : QuestBattle(ws)
{
    public override List<QuestObjective> DefineObjectives(WorldState ws) => [
        new QuestObjective(ws)
            .Hints((player, hints) => hints.PrioritizeAll())
            .WithConnection(new Vector3(100.94f, -24.09f, 257.01f))
            .WithInteract(0x1E8A3F)
    ];
}
