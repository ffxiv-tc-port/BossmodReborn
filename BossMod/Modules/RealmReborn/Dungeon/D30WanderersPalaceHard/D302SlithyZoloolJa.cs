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
    Toad = 3219 // Boss->player, 2.5s cast, 隨機一人變青蛙；不是凝視（面向首領的人沒中）
}

sealed class Firespit(BossModule module) : Components.SingleTargetCast(module, (uint)AID.Firespit);

// 刻意不處理設置咒具（3220）放下的咒具：四種效果（增傷／降火耐／加重／青蛙）外觀只差顏色，錄影分不出是哪一種，
// 一律避開會讓首領收回咒具自己吃增益。
sealed class D302SlithyZoloolJaStates : StateMachineBuilder
{
    public D302SlithyZoloolJaStates(BossModule module) : base(module)
    {
        TrivialPhase()
            .ActivateOnEnter<Firespit>()
            .ActivateOnEnter<Components.UnclaimedCastAOEs>();
    }
}

// 場地沿用沒有模組時的做法（跟著玩家走的方框）；牆由整座副本的障礙物圖（188.30）提供。
[ModuleInfo(BossModuleInfo.Maturity.WIP, Contributors = "ffxiv-tc-port", GroupType = BossModuleInfo.GroupType.CFC, GroupID = 30, NameID = 3093)]
public sealed class D302SlithyZoloolJa(WorldState ws, Actor primary) : SimpleBossModule(ws, primary);
