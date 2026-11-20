namespace BossMod.Heavensward.Dungeon.D12LostCityOfAmdaporHard.D121Achamoth;

public enum OID : uint
{
    Boss = 0x14CF, // 阿卡莫特
    WhisperOfExistence = 0x1510 // 冒險者的幻影
}

// 契約：這個列舉＝模組知道的技能。列舉以外的敵方讀條由 Components.UnclaimedCastAOEs 照沒有模組時的猜法處理，
// 所以刻意不把其他技能列進來（列了就不再被猜）。
public enum AID : uint
{
    NeuroSquama = 5573 // Boss->self, 6.0s cast, range 40 circle
}

// 幻惑鱗粉讀完會照每個人「當下面向」生出幻影，幻影之後朝那個方向放扇形 ⇒ 要背對場中、面向牆。
// 首領通常在場中，所以用「背對首領」近似；EXD 的 VFX 不是 25，後備猜法不會當成凝視。
sealed class NeuroSquama(BossModule module) : Components.CastGaze(module, (uint)AID.NeuroSquama);

sealed class D121AchamothStates : StateMachineBuilder
{
    public D121AchamothStates(BossModule module) : base(module)
    {
        TrivialPhase()
            .ActivateOnEnter<NeuroSquama>()
            .ActivateOnEnter<Components.UnclaimedCastAOEs>();
    }
}

// 場地與尋路沿用沒有模組時的做法（跟著玩家走的方框）：這個副本沒有障礙物圖，也沒有錄影可量場地。
[ModuleInfo(BossModuleInfo.Maturity.WIP, Contributors = "ffxiv-tc-port", GroupType = BossModuleInfo.GroupType.CFC, GroupID = 140, NameID = 4744)]
public sealed class D121Achamoth(WorldState ws, Actor primary) : SimpleBossModule(ws, primary);
