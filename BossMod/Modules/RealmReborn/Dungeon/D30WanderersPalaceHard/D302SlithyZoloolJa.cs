namespace BossMod.RealmReborn.Dungeon.D30WanderersPalaceHard.D302SlithyZoloolJa;

public enum OID : uint
{
    Boss = 0xDCC // 濕皮妖師 佐爾加
}

// 契約：這個列舉＝模組知道的技能。列舉以外的敵方讀條由 Components.UnclaimedCastAOEs 照沒有模組時的猜法處理，
// 所以刻意不把其他技能列進來（列了就不再被猜）。
public enum AID : uint
{
    Firespit = 3217, // Boss->player, 2.0s cast, single-target tankbuster
    SoulDouse = 3230 // ?->self, 3.0s cast, gaze, inflicts Doom on players facing the caster
}

sealed class Firespit(BossModule module) : Components.SingleTargetCast(module, (uint)AID.Firespit);
// 靈魂熄滅的 Action 表沒有凝視旗標（VFX 不是 25），後備猜法不會當凝視處理 ⇒ 模組自己背對。
sealed class SoulDouse(BossModule module) : Components.CastGaze(module, (uint)AID.SoulDouse);

sealed class D302SlithyZoloolJaStates : StateMachineBuilder
{
    public D302SlithyZoloolJaStates(BossModule module) : base(module)
    {
        TrivialPhase()
            .ActivateOnEnter<Firespit>()
            .ActivateOnEnter<SoulDouse>()
            .ActivateOnEnter<Components.UnclaimedCastAOEs>();
    }
}

// 場地與尋路沿用沒有模組時的做法（跟著玩家走的方框）：這個副本沒有障礙物圖，也沒有錄影可量場地。
[ModuleInfo(BossModuleInfo.Maturity.WIP, Contributors = "ffxiv-tc-port", GroupType = BossModuleInfo.GroupType.CFC, GroupID = 30, NameID = 3093)]
public sealed class D302SlithyZoloolJa(WorldState ws, Actor primary) : SimpleBossModule(ws, primary);
