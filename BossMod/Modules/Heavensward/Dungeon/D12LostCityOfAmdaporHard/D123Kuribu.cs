namespace BossMod.Heavensward.Dungeon.D12LostCityOfAmdaporHard.D123Kuribu;

public enum OID : uint
{
    Boss = 0x14D3, // R2.7
}

// 契約：這個列舉＝模組知道的技能。列舉以外的敵方讀條由 Components.UnclaimedCastAOEs 照沒有模組時的猜法處理，
// 所以刻意不把其他技能列進來（列了就不再被猜）。
public enum AID : uint
{
    HolyCure = 5600, // Boss->self, 5.7s cast, range 40 circle with falloff, omen
}

// 距離衰減的全場圈：離中心 5.7 碼吃到超過最大 HP、24.7～26 碼只吃一到兩成多 ⇒ 危險區取 15 碼。
sealed class HolyCure(BossModule module) : Components.SimpleAOEs(module, (uint)AID.HolyCure, 15f);

sealed class D123KuribuStates : StateMachineBuilder
{
    public D123KuribuStates(BossModule module) : base(module)
    {
        // 基路伯會「死」兩次再復活（死亡期間仍在戰鬥中、物件不消失），最後一條命打完才消失
        // ⇒ 不用「首領死亡」當結束條件，否則第一次倒下就收掉模組、聖療在那之後。
        TrivialPhase()
            .ActivateOnEnter<HolyCure>()
            .ActivateOnEnter<Components.UnclaimedCastAOEs>()
            .Raw.Update = () => Module.PrimaryActor.IsDestroyed;
    }
}

// 場地與尋路沿用沒有模組時的做法（跟著玩家走的方框）：這個副本沒有障礙物圖，也沒有量過場地。
[ModuleInfo(BossModuleInfo.Maturity.Contributed, Contributors = "ffxiv-tc-port", GroupType = BossModuleInfo.GroupType.CFC, GroupID = 140, NameID = 4747)]
public sealed class D123Kuribu(WorldState ws, Actor primary) : SimpleBossModule(ws, primary);
