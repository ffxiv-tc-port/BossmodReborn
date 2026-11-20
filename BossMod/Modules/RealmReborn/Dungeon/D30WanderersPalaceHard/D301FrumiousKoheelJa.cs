namespace BossMod.RealmReborn.Dungeon.D30WanderersPalaceHard.D301FrumiousKoheelJa;

public enum OID : uint
{
    Boss = 0xDCA // 折角騎士 寇黑加
}

// 沒有列技能：讀條招式交給 UnclaimedCastAOEs 照後備猜法處理；
// 火投槍留下的長槍要盡快打掉，但長槍的 OID 沒有證據，刻意不做優先度。
sealed class D301FrumiousKoheelJaStates : StateMachineBuilder
{
    public D301FrumiousKoheelJaStates(BossModule module) : base(module)
    {
        TrivialPhase()
            .ActivateOnEnter<Components.UnclaimedCastAOEs>();
    }
}

// 場地與尋路沿用沒有模組時的做法（跟著玩家走的方框）：這個副本沒有障礙物圖，也沒有錄影可量場地。
[ModuleInfo(BossModuleInfo.Maturity.WIP, Contributors = "ffxiv-tc-port", GroupType = BossModuleInfo.GroupType.CFC, GroupID = 30, NameID = 3091)]
public sealed class D301FrumiousKoheelJa(WorldState ws, Actor primary) : SimpleBossModule(ws, primary);
