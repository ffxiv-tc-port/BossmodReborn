namespace BossMod.Stormblood.Dungeon.D14SaintMociannesArboretumHard.D143Tokkapchi;

public enum OID : uint
{
    Boss = 0x242B // 枯腐泥妖
}

// 契約：這個列舉＝模組知道的技能。列舉以外的敵方讀條由 Components.UnclaimedCastAOEs 照沒有模組時的猜法處理，
// 所以刻意不把其他技能列進來（列了就不再被猜）。
public enum AID : uint
{
    Mudsling = 12597, // Boss->player, 3.0s cast, single-target tankbuster
    Quagmire = 12601, // Boss->player, no cast, range 6 circle spread
    BogBequest = 12603 // Boss->self, 5.0s cast, range 10-20 donut
}

sealed class Mudsling(BossModule module) : Components.SingleTargetCast(module, (uint)AID.Mudsling);

// 頭上標記 0x8B 到判定的秒數沒有證據；取污泥讀條（5 秒）的長度，只影響 AI 的急迫度。
sealed class Quagmire(BossModule module) : Components.SpreadFromIcon(module, 0x8Bu, (uint)AID.Quagmire, 6f, 5.1d);

// 污泥外無雙是 CastType 10，後備猜法的甜甜圈內徑一律 3；預兆 gl_sircle_2010 ＝外 20／內 10。
sealed class BogBequest(BossModule module) : Components.SimpleAOEs(module, (uint)AID.BogBequest, new AOEShapeDonut(10f, 20f));

sealed class D143TokkapchiStates : StateMachineBuilder
{
    public D143TokkapchiStates(BossModule module) : base(module)
    {
        TrivialPhase()
            .ActivateOnEnter<Mudsling>()
            .ActivateOnEnter<Quagmire>()
            .ActivateOnEnter<BogBequest>()
            .ActivateOnEnter<Components.UnclaimedCastAOEs>();
    }
}

// 場地與尋路沿用沒有模組時的做法（跟著玩家走的方框，套用本副本的障礙物圖）：沒有錄影可量場地。
// 刻意不做推泥球上平台與污泥噴出的站平台（平台位置與泥球物件 ID 沒有證據）。
[ModuleInfo(BossModuleInfo.Maturity.WIP, Contributors = "ffxiv-tc-port", GroupType = BossModuleInfo.GroupType.CFC, GroupID = 584, NameID = 7662)]
public sealed class D143Tokkapchi(WorldState ws, Actor primary) : SimpleBossModule(ws, primary);
