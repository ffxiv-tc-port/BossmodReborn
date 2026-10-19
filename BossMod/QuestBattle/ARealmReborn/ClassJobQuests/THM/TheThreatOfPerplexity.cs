namespace BossMod.QuestBattle.ARealmReborn.ClassJobQuests.THM;

// EXD 驗證(exd-tc/7.20)：ContentFinderCondition #325 ContentLinkType=5、TerritoryType 267、
// QuestBattle #41 -> Quest 65887 ClsThm200_00351「圍困之危」(咒術士 Lv20)。
[ZoneModuleInfo(BossModuleInfo.Maturity.Contributed, 325)]
internal class TheThreatOfPerplexity(WorldState ws) : QuestBattle(ws)
{
    // ⚠️ 逐字照搬上游。PrioritizeTargetsByOID 的簽章是 (uint oid, int priority)，兩邊完全相同，
    //    所以這一行的語意是「OID 0x2A4 的優先度設成 0x2A5(=677)」而不是「0x2A4 與 0x2A5 兩種目標」。
    //    看起來像上游筆誤，但改掉是猜測而不是移植，這裡維持與上游一致。
    //    影響有限：0x2A4 照樣被排到最前面，0x2A5 只是拿預設優先度。
    public override List<QuestObjective> DefineObjectives(WorldState ws) => [
        new QuestObjective(ws)
            .Hints((player, hints) => hints.PrioritizeTargetsByOID(0x2A4, 0x2A5))
    ];
}
