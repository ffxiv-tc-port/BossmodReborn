namespace BossMod.RealmReborn.Dungeon.D25StoneVigilHard.D251Gorynich;

public enum OID : uint
{
    Boss = 0xBB6 // 格里尼奇惡龍
}

// 契約：這個列舉＝模組知道的技能。列舉以外的敵方讀條由 Components.UnclaimedCastAOEs 照沒有模組時的猜法處理，
// 所以刻意不把其他技能列進來（列了就不再被猜）。
public enum AID : uint
{
    Rake = 2293 // Boss->player, 3.0s cast, single-target tankbuster
}

sealed class Rake(BossModule module) : Components.SingleTargetCast(module, (uint)AID.Rake);

sealed class D251GorynichStates : StateMachineBuilder
{
    public D251GorynichStates(BossModule module) : base(module)
    {
        TrivialPhase()
            .ActivateOnEnter<Rake>()
            .ActivateOnEnter<Components.UnclaimedCastAOEs>();
    }
}

// 場地與尋路沿用沒有模組時的做法（跟著玩家走的方框）：這個副本沒有障礙物圖，也沒有錄影可量場地。
[ModuleInfo(BossModuleInfo.Maturity.WIP, Contributors = "ffxiv-tc-port", GroupType = BossModuleInfo.GroupType.CFC, GroupID = 25, NameID = 2778)]
public sealed class D251Gorynich(WorldState ws, Actor primary) : SimpleBossModule(ws, primary);
