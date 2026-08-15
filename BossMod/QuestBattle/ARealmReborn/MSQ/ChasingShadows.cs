namespace BossMod.QuestBattle.ARealmReborn.MSQ;

// EXD 驗證(exd-tc/7.20)：ContentFinderCondition #296 ContentLinkType=5、TerritoryType 233、
// QuestBattle #11 -> Quest 65981 ManFst005_00445「追蹤可疑者」(主線 Lv5)。
[ZoneModuleInfo(BossModuleInfo.Maturity.Contributed, 296)]
internal class ChasingShadows(WorldState ws) : QuestBattle(ws)
{
    // 🔴 上游覆寫的是 CalculateAIHints，本移植改掛 AddQuestAIHints。
    //    在 BMR 的基底裡 CalculateAIHints 已經被 QuestBattle 實作掉了(它負責推進目標、尋路、
    //    以及最外層那道 EnableQuestBattles 閘門)，子類別再覆寫一次會整段蓋掉——
    //    「完整副本自動化」關著的時候還是會改目標優先度。AddQuestAIHints 是基底留給模組的掛點，
    //    在同一個位置被呼叫，效果相同而且吃得到那道閘門。BMR 既有的 ARR 模組也都用這個掛點。
    public override void AddQuestAIHints(Actor player, AIHints hints)
    {
        foreach (var p in hints.PotentialTargets)
            p.Priority = p.Actor.OID switch
            {
                0x224 => 0,
                0x376 => 1,
                _ => 2
            };
    }
}
