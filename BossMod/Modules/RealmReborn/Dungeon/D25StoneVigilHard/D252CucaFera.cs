namespace BossMod.RealmReborn.Dungeon.D25StoneVigilHard.D252CucaFera;

public enum OID : uint
{
    Boss = 0xBB8 // 庫卡龍龜
}

// 契約：這個列舉＝模組知道的技能。列舉以外的敵方讀條由 Components.UnclaimedCastAOEs 照沒有模組時的猜法處理，
// 所以刻意不把其他技能列進來（列了就不再被猜）。
public enum AID : uint
{
    HardStomp = 2299 // Boss->self, 5.0s cast, range 40 circle raidwide
}

// 猛力踏是全場傷害（後備猜法對半徑 30 以上的圓一律不畫），只給全場提示；打斷要靠玩家操作的加農砲，刻意不做。
sealed class HardStomp(BossModule module) : Components.RaidwideCast(module, (uint)AID.HardStomp);

sealed class D252CucaFeraStates : StateMachineBuilder
{
    public D252CucaFeraStates(BossModule module) : base(module)
    {
        TrivialPhase()
            .ActivateOnEnter<HardStomp>()
            .ActivateOnEnter<Components.UnclaimedCastAOEs>();
    }
}

// 場地與尋路沿用沒有模組時的做法（跟著玩家走的方框）：這個副本沒有障礙物圖，也沒有錄影可量場地。
[ModuleInfo(BossModuleInfo.Maturity.WIP, Contributors = "ffxiv-tc-port", GroupType = BossModuleInfo.GroupType.CFC, GroupID = 25, NameID = 2775)]
public sealed class D252CucaFera(WorldState ws, Actor primary) : SimpleBossModule(ws, primary);
