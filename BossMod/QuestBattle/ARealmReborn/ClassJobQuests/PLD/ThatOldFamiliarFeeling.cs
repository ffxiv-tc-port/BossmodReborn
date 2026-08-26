namespace BossMod.QuestBattle.ARealmReborn.ClassJobQuests.PLD;

// EXD 驗證(exd-tc/7.20)：ContentFinderCondition #315 ContentLinkType=5、TerritoryType 254
// (與寫死的 territoryID 相符)、QuestBattle #20 -> Quest 65798 ClsGla150_00262「箭雨中的勝機」
// (劍術士 Lv15)。
[ZoneModuleInfo(BossModuleInfo.Maturity.Contributed, 315, 254)]
internal class ThatOldFamiliarFeeling(WorldState ws) : QuestBattle(ws)
{
    private const uint StoutAttacker = 0x277;
    private const uint NamelessAttacker1 = 0x276; // L20 Nameless Attacker, Archer
    private const uint NamelessAttacker2 = 0x29D; // L17 Nameless Attacker, Archer
    private const uint NamelessAttacker3 = 0x278; // 632 L14 Nameless Attacker, Archer
    private const uint NamelessAttacker4 = 0x29E; // 670 L15 Nameless Attacker, Lancer

    public override List<QuestObjective> DefineObjectives(WorldState ws) => [
        new QuestObjective(ws)
            .WithConnection(new Vector3(29.805323f, 7.999997f, -112.308754f))
            .PauseForCombat(false)
            .CompleteOnKilled(NamelessAttacker2, 4),
        new QuestObjective(ws)
            .WithConnection(new Vector3(39.71924f, 7.9999876f, -95.658875f))
            .Hints((player, hints) =>
            {
                hints.PrioritizeTargetsByOID(StoutAttacker, 5);
            })
            .PauseForCombat(false)
            .CompleteOnKilled(StoutAttacker, 2),
        new QuestObjective(ws)
            .WithConnection(new Vector3(12.652967f, 7.999997f, -103.202995f))
            .PauseForCombat(false)
            .CompleteOnKilled(NamelessAttacker1, 3),
        new QuestObjective(ws)
            .WithConnection(new Vector3(39.92471f, 7.9999714f, -99.20717f))
            .Hints((player, hints) =>
            {
                hints.PrioritizeTargetsByOID(NamelessAttacker3, 5);
            })
            .PauseForCombat(false)
            .CompleteOnKilled(NamelessAttacker3, 2),
        new QuestObjective(ws)
            .WithConnection(new Vector3(28.63875f, 7.209999f, -100.732704f))
            .Hints((player, hints) =>
            {
                hints.PrioritizeTargetsByOID(NamelessAttacker4, 5);
            })
            .PauseForCombat(false)
            .CompleteOnKilled(NamelessAttacker4, 2),
        new QuestObjective(ws)
            .WithConnection(new Vector3(29.805323f, 7.999997f, -112.308754f))
            .PauseForCombat(false)
            .CompleteAtDestination()
    ];
}
