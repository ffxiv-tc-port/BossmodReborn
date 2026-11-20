namespace BossMod.Heavensward.Dungeon.D10PharosSiriusHard.D102BeGu;

public enum OID : uint
{
    Boss = 0x1441 // 第8團高級教士 孛罟
}

// 契約：這個列舉＝模組知道的技能。列舉以外的敵方讀條由 Components.UnclaimedCastAOEs 照沒有模組時的猜法處理，
// 所以刻意不把其他技能列進來（列了就不再被猜）。
public enum AID : uint
{
    StripMine = 5317, // Boss->location, 5.0s cast, range 50 circle with falloff
    Corrupture = 5318 // Boss->self, 3.0s cast, range 50 circle
}

sealed class Corrupture(BossModule module) : Components.RaidwideCast(module, (uint)AID.Corrupture);
// 距離衰減（Omen 是 shipimpact）；後備猜法遇到 50 碼圓會當全場而不畫。衰減沒有量過，15 碼沿用 BMR 慣例值。
sealed class StripMine(BossModule module) : Components.SimpleAOEs(module, (uint)AID.StripMine, 15f);

sealed class D102BeGuStates : StateMachineBuilder
{
    public D102BeGuStates(BossModule module) : base(module)
    {
        TrivialPhase()
            .ActivateOnEnter<Corrupture>()
            .ActivateOnEnter<StripMine>()
            .ActivateOnEnter<Components.UnclaimedCastAOEs>();
    }
}

// 場地與尋路沿用沒有模組時的做法（跟著玩家走的方框）：這個副本沒有障礙物圖，也沒有錄影可量場地。
// 鍊金術引擎要立刻打掉（攻略），但它的 OID 在 Garland 查不到，刻意不做優先度。
[ModuleInfo(BossModuleInfo.Maturity.WIP, Contributors = "ffxiv-tc-port", GroupType = BossModuleInfo.GroupType.CFC, GroupID = 40, NameID = 4571)]
public sealed class D102BeGu(WorldState ws, Actor primary) : SimpleBossModule(ws, primary);
