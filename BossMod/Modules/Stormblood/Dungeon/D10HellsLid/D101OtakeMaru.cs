namespace BossMod.Stormblood.Dungeon.D10HellsLid.D101OtakeMaru;

public enum OID : uint
{
    Boss = 0x20BC // 大岳丸
}

// 沒有列技能：有讀條的招式（100噸迴轉、10噸揮打、徒手、噴石）後備猜法照 Action 表就畫得對，交給 UnclaimedCastAOEs。
sealed class D101OtakeMaruStates : StateMachineBuilder
{
    public D101OtakeMaruStates(BossModule module) : base(module)
    {
        TrivialPhase()
            .ActivateOnEnter<Components.UnclaimedCastAOEs>();
    }
}

// 場地與尋路沿用沒有模組時的做法（跟著玩家走的方框）：這個副本沒有障礙物圖，也沒有錄影可量場地。
// 刻意不做頭上標記追擊與岩棍（沒有讀條，範圍與時機沒有 ID 證據）。
[ModuleInfo(BossModuleInfo.Maturity.WIP, Contributors = "ffxiv-tc-port", GroupType = BossModuleInfo.GroupType.CFC, GroupID = 284, NameID = 6994)]
public sealed class D101OtakeMaru(WorldState ws, Actor primary) : SimpleBossModule(ws, primary);
