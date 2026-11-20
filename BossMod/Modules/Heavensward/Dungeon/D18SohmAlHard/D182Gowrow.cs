namespace BossMod.Heavensward.Dungeon.D18SohmAlHard.D182Gowrow;

public enum OID : uint
{
    Boss = 0x1916 // 高牢怪龍
}

// 沒有列技能：撕裂利爪、尾部碎擊、熔岩流這些沒有 Omen 的扇形，後備猜法會照讀條方向畫 180° 扇形，
// 比攻略寫的範圍寬（偏安全）；衝鋒、迴旋尾、火球的形狀 EXD 有，交給後備猜法。
sealed class D182GowrowStates : StateMachineBuilder
{
    public D182GowrowStates(BossModule module) : base(module)
    {
        TrivialPhase()
            .ActivateOnEnter<Components.UnclaimedCastAOEs>();
    }
}

// 場地與尋路沿用沒有模組時的做法（跟著玩家走的方框）：這個副本沒有障礙物圖，也沒有錄影可量場地。
[ModuleInfo(BossModuleInfo.Maturity.WIP, Contributors = "ffxiv-tc-port", GroupType = BossModuleInfo.GroupType.CFC, GroupID = 221, NameID = 5530)]
public sealed class D182Gowrow(WorldState ws, Actor primary) : SimpleBossModule(ws, primary);
