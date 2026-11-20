namespace BossMod.Heavensward.Dungeon.D16GreatGubalLibraryHard.D161DemonOfTheTome;

public enum OID : uint
{
    Boss = 0x1767 // 書中惡魔
}

// 沒有列技能：發行／停刊的直線、駭人嚎叫的圓都有 Omen，交給 UnclaimedCastAOEs 照後備猜法處理；
// 三連爪是沒有讀條的小死刑，沒有讀條可掛提示。
sealed class D161DemonOfTheTomeStates : StateMachineBuilder
{
    public D161DemonOfTheTomeStates(BossModule module) : base(module)
    {
        TrivialPhase()
            .ActivateOnEnter<Components.UnclaimedCastAOEs>();
    }
}

// 場地與尋路沿用沒有模組時的做法（跟著玩家走的方框）：這個副本沒有障礙物圖，也沒有錄影可量場地。
[ModuleInfo(BossModuleInfo.Maturity.WIP, Contributors = "ffxiv-tc-port", GroupType = BossModuleInfo.GroupType.CFC, GroupID = 196, NameID = 5216)]
public sealed class D161DemonOfTheTome(WorldState ws, Actor primary) : SimpleBossModule(ws, primary);
