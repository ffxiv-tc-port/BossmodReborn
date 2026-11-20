namespace BossMod.Stormblood.Dungeon.D08TempleOfTheFist.D083IvonCoeurlfist;

public enum OID : uint
{
    Boss = 0x1B56 // 雙豹伊沃恩
}

// 契約：這個列舉＝模組知道的技能。列舉以外的敵方讀條由 Components.UnclaimedCastAOEs 照沒有模組時的猜法處理，
// 所以刻意不把其他技能列進來（列了就不再被猜）。
public enum AID : uint
{
    SpiritWave = 8167, // Boss->self, 4.0s cast, range 60 circle raidwide
    TheRoseOfDestruction = 8174 // Boss->player, 5.0s cast, range 5 circle stack
}

sealed class SpiritWave(BossModule module) : Components.RaidwideCast(module, (uint)AID.SpiritWave);

// 鬥靈彈是分攤；後備猜法會畫成要躲開的圓（AI 反而離開被點名的人）⇒ 認領。
sealed class TheRoseOfDestruction(BossModule module) : Components.StackWithCastTargets(module, (uint)AID.TheRoseOfDestruction, 5f);

sealed class D083IvonCoeurlfistStates : StateMachineBuilder
{
    public D083IvonCoeurlfistStates(BossModule module) : base(module)
    {
        TrivialPhase()
            .ActivateOnEnter<SpiritWave>()
            .ActivateOnEnter<TheRoseOfDestruction>()
            .ActivateOnEnter<Components.UnclaimedCastAOEs>();
    }
}

// 場地與尋路沿用沒有模組時的做法（跟著玩家走的方框）：這個副本沒有障礙物圖，也沒有錄影可量場地。
// 刻意不做破壞神拳（距離衰減，半徑沒有證據）與雙豹招來的龍頭列（物件 ID 沒有證據）。
[ModuleInfo(BossModuleInfo.Maturity.WIP, Contributors = "ffxiv-tc-port", GroupType = BossModuleInfo.GroupType.CFC, GroupID = 236, NameID = 6117)]
public sealed class D083IvonCoeurlfist(WorldState ws, Actor primary) : SimpleBossModule(ws, primary);
