namespace BossMod.Heavensward.Dungeon.D14HullbreakerIsleHard.D142Ymir;

public enum OID : uint
{
    Boss = 0x1643 // 尤彌爾
}

// 契約：這個列舉＝模組知道的技能。列舉以外的敵方讀條由 Components.UnclaimedCastAOEs 照沒有模組時的猜法處理，
// 所以刻意不把其他技能列進來（列了就不再被猜）。
public enum AID : uint
{
    Megavolt = 6118, // Boss->self, 7.0s cast, single-target
    BlanketThunder = 6124 // Boss->self, 3.0s cast, range 40 circle
}

sealed class BlanketThunder(BossModule module) : Components.RaidwideCast(module, (uint)AID.BlanketThunder);
// 攻略：被點名的人要踩進黏液被束縛，才不會被雷打到；黏液的 OID 沒有證據，只給提示。
sealed class Megavolt(BossModule module) : Components.CastHint(module, (uint)AID.Megavolt, "Marked: stand in the slime", true);

sealed class D142YmirStates : StateMachineBuilder
{
    public D142YmirStates(BossModule module) : base(module)
    {
        TrivialPhase()
            .ActivateOnEnter<BlanketThunder>()
            .ActivateOnEnter<Megavolt>()
            .ActivateOnEnter<Components.UnclaimedCastAOEs>();
    }
}

// 場地與尋路沿用沒有模組時的做法（跟著玩家走的方框）：這個副本沒有障礙物圖，也沒有錄影可量場地。
[ModuleInfo(BossModuleInfo.Maturity.WIP, Contributors = "ffxiv-tc-port", GroupType = BossModuleInfo.GroupType.CFC, GroupID = 172, NameID = 4911)]
public sealed class D142Ymir(WorldState ws, Actor primary) : SimpleBossModule(ws, primary);
