namespace BossMod.Heavensward.Dungeon.D18SohmAlHard.D181Leightonward;

public enum OID : uint
{
    Boss = 0x1913, // 萊頓瓦德
    SporeSac = 0x1914, // 孢囊
    SmallSporeSac = 0x1915 // 小型孢囊
}

// 契約：這個列舉＝模組知道的技能。列舉以外的敵方讀條由 Components.UnclaimedCastAOEs 照沒有模組時的猜法處理，
// 所以刻意不把其他技能列進來（列了就不再被猜）。
public enum AID : uint
{
    InflammableFumes = 7216, // Boss->self, 3.0s cast, range 50 circle (damage comes from 7217)
    GloriousBlaze = 7218 // SporeSac->self, 3.0s cast, range 50 circle with falloff
}

sealed class InflammableFumes(BossModule module) : Components.RaidwideCast(module, (uint)AID.InflammableFumes);
// 大孢囊的引火是距離衰減（Omen 是 shipimpact）；後備猜法遇到 50 碼圓會當全場而不畫。
// 衰減曲線沒有量過，15 碼沿用 BMR 其他距離衰減招式的慣例值。
sealed class GloriousBlaze(BossModule module) : Components.SimpleAOEs(module, (uint)AID.GloriousBlaze, 15f);

sealed class D181LeightonwardStates : StateMachineBuilder
{
    public D181LeightonwardStates(BossModule module) : base(module)
    {
        TrivialPhase()
            .ActivateOnEnter<InflammableFumes>()
            .ActivateOnEnter<GloriousBlaze>()
            .ActivateOnEnter<Components.UnclaimedCastAOEs>();
    }
}

// 場地與尋路沿用沒有模組時的做法（跟著玩家走的方框）：這個副本沒有障礙物圖，也沒有錄影可量場地。
[ModuleInfo(BossModuleInfo.Maturity.WIP, Contributors = "ffxiv-tc-port", GroupType = BossModuleInfo.GroupType.CFC, GroupID = 221, NameID = 5529)]
public sealed class D181Leightonward(WorldState ws, Actor primary) : SimpleBossModule(ws, primary);
