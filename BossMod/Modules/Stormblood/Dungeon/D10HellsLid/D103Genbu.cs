namespace BossMod.Stormblood.Dungeon.D10HellsLid.D103Genbu;

public enum OID : uint
{
    Boss = 0x20BF, // 玄武
    ChelonianGate = 0x2116 // 玄結界
}

// 契約：這個列舉＝模組知道的技能。列舉以外的敵方讀條由 Components.UnclaimedCastAOEs 照沒有模組時的猜法處理，
// 所以刻意不把其他技能列進來（列了就不再被猜）。
public enum AID : uint
{
    HellOfWaste = 10194 // Boss->player, no cast, range 8 circle spread
}

// 頭上標記 0x2B 到判定的秒數沒有證據；取 5 秒，只影響 AI 的急迫度。
sealed class HellOfWaste(BossModule module) : Components.SpreadFromIcon(module, 0x2Bu, (uint)AID.HellOfWaste, 8f, 5d);

sealed class D103GenbuStates : StateMachineBuilder
{
    public D103GenbuStates(BossModule module) : base(module)
    {
        TrivialPhase()
            .ActivateOnEnter<HellOfWaste>()
            .ActivateOnEnter<Components.UnclaimedCastAOEs>();
    }
}

// 場地與尋路沿用沒有模組時的做法（跟著玩家走的方框）：這個副本沒有障礙物圖，也沒有錄影可量場地。
// 刻意不做迷潮的水流路線與玄冥波的擊退距離（地板與距離沒有證據）、蛇杖擊（沒有讀條）。
[ModuleInfo(BossModuleInfo.Maturity.WIP, Contributors = "ffxiv-tc-port", GroupType = BossModuleInfo.GroupType.CFC, GroupID = 284, NameID = 6996)]
public sealed class D103Genbu(WorldState ws, Actor primary) : SimpleBossModule(ws, primary)
{
    // 玄冥波期間要在集氣滿之前打掉玄結界 ⇒ 玄結界優先。
    protected override void CalculateModuleAIHints(int slot, Actor actor, PartyRolesConfig.Assignment assignment, AIHints hints)
    {
        base.CalculateModuleAIHints(slot, actor, assignment, hints);
        var count = hints.PotentialTargets.Count;
        for (var i = 0; i < count; ++i)
        {
            var e = hints.PotentialTargets[i];
            if (e.Actor.OID == (uint)OID.ChelonianGate)
                e.Priority = 1;
        }
    }

    protected override void DrawEnemies(int pcSlot, Actor pc)
    {
        Arena.Actor(PrimaryActor);
        Arena.Actors(Enemies((uint)OID.ChelonianGate));
    }
}
