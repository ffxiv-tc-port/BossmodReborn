namespace BossMod.Heavensward.Dungeon.D07Neverreap.D073Waukkeon;

public enum OID : uint
{
    Boss = 0xF83 // 招雷巨蛇
}

// 契約：這個列舉＝模組知道的技能。列舉以外的敵方讀條由 Components.UnclaimedCastAOEs 照沒有模組時的猜法處理，
// 所以刻意不把其他技能列進來（列了就不再被猜）。
public enum AID : uint
{
    Turbine = 4116 // Boss->self, 5.0s cast, range 40 circle, knockback
}

// 渦輪是全場傷害加擊退（攻略：貼著首領、背後留最大空間，免得被推下平台）。
// 擊退距離與平台形狀沒有證據，只給提示不算擊退落點。
sealed class Turbine(BossModule module) : Components.RaidwideCast(module, (uint)AID.Turbine, "Raidwide + knockback: stand next to boss, room behind you");

sealed class D073WaukkeonStates : StateMachineBuilder
{
    public D073WaukkeonStates(BossModule module) : base(module)
    {
        TrivialPhase()
            .ActivateOnEnter<Turbine>()
            .ActivateOnEnter<Components.UnclaimedCastAOEs>();
    }
}

// 場地與尋路沿用沒有模組時的做法（跟著玩家走的方框）：這個副本沒有障礙物圖，也沒有錄影可量場地。
// 小怪優先度刻意不做：攻略同時寫「神域水泡先不管」與「先打水泡再打旋風」，順序沒有定論。
[ModuleInfo(BossModuleInfo.Maturity.WIP, Contributors = "ffxiv-tc-port", GroupType = BossModuleInfo.GroupType.CFC, GroupID = 33, NameID = 3740)]
public sealed class D073Waukkeon(WorldState ws, Actor primary) : SimpleBossModule(ws, primary);
