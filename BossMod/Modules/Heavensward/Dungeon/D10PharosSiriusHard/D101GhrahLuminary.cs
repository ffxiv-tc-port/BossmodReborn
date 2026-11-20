namespace BossMod.Heavensward.Dungeon.D10PharosSiriusHard.D101GhrahLuminary;

public enum OID : uint
{
    Boss = 0x143C // 流光晶片
}

// 沒有列技能：讀條範圍交給 UnclaimedCastAOEs 照後備猜法處理。
// 腐化物的獵物標記要傳給場邊繞圈的小怪（攻略），要判斷誰被標、找哪隻小怪，沒有可用的 id 證據，刻意不做。
sealed class D101GhrahLuminaryStates : StateMachineBuilder
{
    public D101GhrahLuminaryStates(BossModule module) : base(module)
    {
        TrivialPhase()
            .ActivateOnEnter<Components.UnclaimedCastAOEs>();
    }
}

// 場地與尋路沿用沒有模組時的做法（跟著玩家走的方框）：這個副本沒有障礙物圖，也沒有錄影可量場地。
[ModuleInfo(BossModuleInfo.Maturity.WIP, Contributors = "ffxiv-tc-port", GroupType = BossModuleInfo.GroupType.CFC, GroupID = 40, NameID = 4567)]
public sealed class D101GhrahLuminary(WorldState ws, Actor primary) : SimpleBossModule(ws, primary);
