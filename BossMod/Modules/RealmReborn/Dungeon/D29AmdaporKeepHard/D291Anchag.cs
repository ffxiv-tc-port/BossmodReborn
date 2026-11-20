namespace BossMod.RealmReborn.Dungeon.D29AmdaporKeepHard.D291Anchag;

public enum OID : uint
{
    Boss = 0xD60 // 安可哈格
}

// 契約：這個列舉＝模組知道的技能。列舉以外的敵方讀條由 Components.UnclaimedCastAOEs 照沒有模組時的猜法處理，
// 所以刻意不把其他技能列進來（列了就不再被猜）。
public enum AID : uint
{
    RipplesOfGloom = 3170 // Boss->self, 3.0s cast, range 80 circle raidwide
}

// 頭上標記躲雕像後面（視線阻擋）刻意不做：雕像的 OID 與位置沒有證據，猜錯會讓 AI 站到雕像以外的地方。
sealed class RipplesOfGloom(BossModule module) : Components.RaidwideCast(module, (uint)AID.RipplesOfGloom);

sealed class D291AnchagStates : StateMachineBuilder
{
    public D291AnchagStates(BossModule module) : base(module)
    {
        TrivialPhase()
            .ActivateOnEnter<RipplesOfGloom>()
            .ActivateOnEnter<Components.UnclaimedCastAOEs>();
    }
}

// 場地與尋路沿用沒有模組時的做法（跟著玩家走的方框）：這個副本沒有障礙物圖，也沒有錄影可量場地。
[ModuleInfo(BossModuleInfo.Maturity.WIP, Contributors = "ffxiv-tc-port", GroupType = BossModuleInfo.GroupType.CFC, GroupID = 29, NameID = 3272)]
public sealed class D291Anchag(WorldState ws, Actor primary) : SimpleBossModule(ws, primary);
