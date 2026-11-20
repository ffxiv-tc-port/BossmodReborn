namespace BossMod.Stormblood.Dungeon.D14SaintMociannesArboretumHard.D141Nullchu;

public enum OID : uint
{
    Boss = 0x242F // 爛泥食腐獸
}

// 契約：這個列舉＝模組知道的技能。列舉以外的敵方讀條由 Components.UnclaimedCastAOEs 照沒有模組時的猜法處理，
// 所以刻意不把其他技能列進來（列了就不再被猜）。
public enum AID : uint
{
    VineWhip = 11848, // Boss->player, 3.0s cast, single-target tankbuster
    FaultWarren = 11850, // Boss->player, 5.0s cast, range 6 circle stack
    Taproot = 11852 // ?->self, no cast, range 7 circle on marked players
}

sealed class VineWhip(BossModule module) : Components.SingleTargetCast(module, (uint)AID.VineWhip);

// 地層穿刺是分攤；後備猜法會畫成要躲開的圓（AI 反而離開被點名的人）⇒ 認領。
sealed class FaultWarren(BossModule module) : Components.StackWithCastTargets(module, (uint)AID.FaultWarren, 6f);

// 頭上標記 0x8D 到判定的秒數沒有證據；取主根讀條（4 秒）的長度，只影響 AI 的急迫度。
sealed class Taproot(BossModule module) : Components.SpreadFromIcon(module, 0x8Du, (uint)AID.Taproot, 7f, 4.1d);

sealed class D141NullchuStates : StateMachineBuilder
{
    public D141NullchuStates(BossModule module) : base(module)
    {
        TrivialPhase()
            .ActivateOnEnter<VineWhip>()
            .ActivateOnEnter<FaultWarren>()
            .ActivateOnEnter<Taproot>()
            .ActivateOnEnter<Components.UnclaimedCastAOEs>();
    }
}

// 場地與尋路沿用沒有模組時的做法（跟著玩家走的方框，套用本副本的障礙物圖）：沒有錄影可量場地。
// 刻意不做捕食（躲到花後面，花的物件 ID 沒有證據）。
[ModuleInfo(BossModuleInfo.Maturity.WIP, Contributors = "ffxiv-tc-port", GroupType = BossModuleInfo.GroupType.CFC, GroupID = 584, NameID = 7659)]
public sealed class D141Nullchu(WorldState ws, Actor primary) : SimpleBossModule(ws, primary);
