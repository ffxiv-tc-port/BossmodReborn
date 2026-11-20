namespace BossMod.Stormblood.Dungeon.D12SwallowsCompass.D122Daidarabotchi;

public enum OID : uint
{
    Boss = 0x21D8 // 大太法師
}

// 契約：這個列舉＝模組知道的技能。列舉以外的敵方讀條由 Components.UnclaimedCastAOEs 照沒有模組時的猜法處理，
// 所以刻意不把其他技能列進來（列了就不再被猜）。
public enum AID : uint
{
    MountainFalls = 11173 // Boss->player, 5.0s cast, range 8 circle spread
}

// 土石流打在被點名的玩家身上；後備猜法會畫成讀條開始當下的固定圓 ⇒ 認領成散開。
sealed class MountainFalls(BossModule module) : Components.SpreadFromCastTargets(module, (uint)AID.MountainFalls, 8f);

sealed class D122DaidarabotchiStates : StateMachineBuilder
{
    public D122DaidarabotchiStates(BossModule module) : base(module)
    {
        TrivialPhase()
            .ActivateOnEnter<MountainFalls>()
            .ActivateOnEnter<Components.UnclaimedCastAOEs>();
    }
}

// 場地與尋路沿用沒有模組時的做法（跟著玩家走的方框，套用本副本的障礙物圖）：沒有錄影可量場地。
// 刻意不做中央湖面擴大（範圍隨時間變化，沒有證據）與驚天動地（沒有讀條）。
[ModuleInfo(BossModuleInfo.Maturity.WIP, Contributors = "ffxiv-tc-port", GroupType = BossModuleInfo.GroupType.CFC, GroupID = 536, NameID = 7202)]
public sealed class D122Daidarabotchi(WorldState ws, Actor primary) : SimpleBossModule(ws, primary);
