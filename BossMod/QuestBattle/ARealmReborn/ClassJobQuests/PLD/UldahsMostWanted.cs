namespace BossMod.QuestBattle.ARealmReborn.ClassJobQuests.PLD;

// EXD 驗證(exd-tc/7.20)：ContentFinderCondition #314 ContentLinkType=5、TerritoryType 253
// (與寫死的 territoryID 相符)、ClassJobLevelSync 14。
// ⚠️ QuestBattle #19 的 Quest 反向連結在台服資料裡是懸空的(852184，Quest 表最大列號 70952)。
//    這不是本模組的問題：同樣的懸空連結在 QuestBattle 表裡有 6 筆(#13/#19/#50/#54/#87/#88)，
//    是系統性的；而 ZoneModuleRegistry 完全以 CFCID 註冊，執行期不碰這個連結。
//    唯一會解參考它的是偵錯視窗的「Generate module stub」按鈕，那是手動觸發的既有路徑。
[ZoneModuleInfo(BossModuleInfo.Maturity.Contributed, 314, 253)]
internal class UldahsMostWanted(WorldState ws) : QuestBattle(ws)
{
    public override List<QuestObjective> DefineObjectives(WorldState ws) => [
        new QuestObjective(ws)
            .WithConnection(new Vector3(13.6473255f, 13.518082f, 44.521732f))
            .PauseForCombat(true)
            .CompleteAtDestination(),
        new QuestObjective(ws)
            .WithConnection(new Vector3(34.927856f, 13.266486f, 89.40259f))
            .PauseForCombat(true)
            .CompleteAtDestination(),

        new QuestObjective(ws)
            .WithConnection(new Vector3(7.823333f, 12.688625f, 32.762177f))
            .Hints((player, hints) =>
            {
                hints.PrioritizeTargetsByOID(0x274, 5);
                foreach (var e in hints.PotentialTargets)
                    if (e.Actor.OID == 0x271) // Bruce the Big
                        e.Priority = AIHints.Enemy.PriorityForbidden;

                // Stand on the far side of the captain from Bruce so the frontal AoE faces away from Bruce
                var captain = World.Actors.FirstOrDefault(x => x.OID == 0x274);
                var bruce = World.Actors.FirstOrDefault(x => x.OID == 0x271);
                if (captain != null && bruce != null)
                {
                    var dir = (captain.Position - bruce.Position).Normalized();
                    hints.GoalZones.Add(hints.GoalSingleTarget(captain.Position + dir * 3f, 2f));
                }
            })
            .PauseForCombat(false)
            .CompleteOnKilled(0x274) // Duskwight Freelancer Captain
    ];
}
