namespace BossMod.RealmReborn.Dungeon.D26SunkenTempleOfQarnHard.D261DamagedAdjudicator;

public enum OID : uint
{
    Boss = 0xCD4 // 受損的審判化身
}

// 沒有列技能：這隻首領的技能 id 與核心部位的 OID 都沒有證據；讀條交給 UnclaimedCastAOEs 照後備猜法處理。
sealed class D261DamagedAdjudicatorStates : StateMachineBuilder
{
    public D261DamagedAdjudicatorStates(BossModule module) : base(module)
    {
        TrivialPhase()
            .ActivateOnEnter<Components.UnclaimedCastAOEs>();
    }
}

// 場地與尋路沿用沒有模組時的做法（跟著玩家走的方框）：這個副本沒有障礙物圖，也沒有錄影可量場地。
[ModuleInfo(BossModuleInfo.Maturity.WIP, Contributors = "ffxiv-tc-port", GroupType = BossModuleInfo.GroupType.CFC, GroupID = 26, NameID = 3065)]
public sealed class D261DamagedAdjudicator(WorldState ws, Actor primary) : SimpleBossModule(ws, primary);
