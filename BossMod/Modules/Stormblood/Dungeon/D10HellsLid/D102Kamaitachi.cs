namespace BossMod.Stormblood.Dungeon.D10HellsLid.D102Kamaitachi;

public enum OID : uint
{
    Boss = 0x20BD // 鐮鼬
}

// 契約：這個列舉＝模組知道的技能。列舉以外的敵方讀條由 Components.UnclaimedCastAOEs 照沒有模組時的猜法處理，
// 所以刻意不把其他技能列進來（列了就不再被猜）。
public enum AID : uint
{
    CirclingWinds = 10184 // Boss->self, 5.0s cast, range 10-20 donut
}

// 疾風衝是 CastType 10，後備猜法的甜甜圈內徑一律 3（AI 會以為只有王腳下安全）；預兆 gl_sircle_2010 ＝外 20／內 10。
sealed class CirclingWinds(BossModule module) : Components.SimpleAOEs(module, (uint)AID.CirclingWinds, new AOEShapeDonut(10f, 20f));

sealed class D102KamaitachiStates : StateMachineBuilder
{
    public D102KamaitachiStates(BossModule module) : base(module)
    {
        TrivialPhase()
            .ActivateOnEnter<CirclingWinds>()
            .ActivateOnEnter<Components.UnclaimedCastAOEs>();
    }
}

// 場地與尋路沿用沒有模組時的做法（跟著玩家走的方框）：這個副本沒有障礙物圖，也沒有錄影可量場地。
// 刻意不做鐮風（距離衰減，半徑沒有證據）、野鐮衝鋒與鞭鼬（沒有讀條）。
[ModuleInfo(BossModuleInfo.Maturity.WIP, Contributors = "ffxiv-tc-port", GroupType = BossModuleInfo.GroupType.CFC, GroupID = 284, NameID = 6995)]
public sealed class D102Kamaitachi(WorldState ws, Actor primary) : SimpleBossModule(ws, primary);
