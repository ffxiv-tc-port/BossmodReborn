namespace BossMod.Stormblood.Dungeon.D11FractalContinuumHard.D112UltimaWarrior;

public enum OID : uint
{
    Boss = 0x20C4 // 究極戰士
}

// 契約：這個列舉＝模組知道的技能。列舉以外的敵方讀條由 Components.UnclaimedCastAOEs 照沒有模組時的猜法處理，
// 所以刻意不把其他技能列進來（列了就不再被猜）。
public enum AID : uint
{
    Aetheroplasm = 10131, // Boss->player, 3.0s cast, single-target tankbuster
    CeruleumVent = 10132, // Boss->self, 3.0s cast, range 40 circle raidwide with knockback
    MassAetheroplasm = 10133 // Boss->player, 5.0s cast, range 6 circle stack
}

sealed class Aetheroplasm(BossModule module) : Components.SingleTargetCast(module, (uint)AID.Aetheroplasm);
sealed class CeruleumVent(BossModule module) : Components.RaidwideCast(module, (uint)AID.CeruleumVent);

// 大型乙太爆雷是分攤；後備猜法會畫成要躲開的圓（AI 反而離開被點名的人）⇒ 認領。
sealed class MassAetheroplasm(BossModule module) : Components.StackWithCastTargets(module, (uint)AID.MassAetheroplasm, 6f);

sealed class D112UltimaWarriorStates : StateMachineBuilder
{
    public D112UltimaWarriorStates(BossModule module) : base(module)
    {
        TrivialPhase()
            .ActivateOnEnter<Aetheroplasm>()
            .ActivateOnEnter<CeruleumVent>()
            .ActivateOnEnter<MassAetheroplasm>()
            .ActivateOnEnter<Components.UnclaimedCastAOEs>();
    }
}

// 場地與尋路沿用沒有模組時的做法（跟著玩家走的方框）：這個副本沒有障礙物圖，也沒有錄影可量場地。
// 刻意不做青磷放射的擊退距離、三蠻神吸收後的機制（站對應隕石、黑白圈配對、綠圈擺放）：距離與物件 ID 沒有證據。
[ModuleInfo(BossModuleInfo.Maturity.WIP, Contributors = "ffxiv-tc-port", GroupType = BossModuleInfo.GroupType.CFC, GroupID = 285, NameID = 7055)]
public sealed class D112UltimaWarrior(WorldState ws, Actor primary) : SimpleBossModule(ws, primary);
