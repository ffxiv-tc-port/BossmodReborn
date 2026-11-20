namespace BossMod.Stormblood.Dungeon.D07KuganeCastle.D072DojunMaru;

public enum OID : uint
{
    Boss = 0x1A76, // 道順丸
    EliteOnmitsu = 0x1A77 // 攘夷隱秘（分身）
}

// 契約：這個列舉＝模組知道的技能。列舉以外的敵方讀條由 Components.UnclaimedCastAOEs 照沒有模組時的猜法處理，
// 所以刻意不把其他技能列進來（列了就不再被猜）。
public enum AID : uint
{
    ClockworkRaiton = 7836, // Boss->player, no cast, range 8 circle spread
    Harakiri = 7839 // EliteOnmitsu->self, 8.0s cast, range 50 circle raidwide
}

// 頭上標記 0x5F 到判定的秒數沒有證據；取機關雷遁讀條（5 秒）的長度，只影響 AI 的急迫度。
sealed class ClockworkRaiton(BossModule module) : Components.SpreadFromIcon(module, 0x5Fu, (uint)AID.ClockworkRaiton, 8f, 5.1d);
sealed class Harakiri(BossModule module) : Components.RaidwideCast(module, (uint)AID.Harakiri);

sealed class D072DojunMaruStates : StateMachineBuilder
{
    public D072DojunMaruStates(BossModule module) : base(module)
    {
        TrivialPhase()
            .ActivateOnEnter<ClockworkRaiton>()
            .ActivateOnEnter<Harakiri>()
            .ActivateOnEnter<Components.UnclaimedCastAOEs>();
    }
}

// 場地與尋路沿用沒有模組時的做法（跟著玩家走的方框）：這個副本沒有障礙物圖，也沒有錄影可量場地。
[ModuleInfo(BossModuleInfo.Maturity.WIP, Contributors = "ffxiv-tc-port", GroupType = BossModuleInfo.GroupType.CFC, GroupID = 262, NameID = 6087)]
public sealed class D072DojunMaru(WorldState ws, Actor primary) : SimpleBossModule(ws, primary)
{
    // 分身讀完切腹會打全體 ⇒ 分身優先。
    protected override void CalculateModuleAIHints(int slot, Actor actor, PartyRolesConfig.Assignment assignment, AIHints hints)
    {
        base.CalculateModuleAIHints(slot, actor, assignment, hints);
        var count = hints.PotentialTargets.Count;
        for (var i = 0; i < count; ++i)
        {
            var e = hints.PotentialTargets[i];
            if (e.Actor.OID == (uint)OID.EliteOnmitsu)
                e.Priority = 1;
        }
    }

    protected override void DrawEnemies(int pcSlot, Actor pc)
    {
        Arena.Actor(PrimaryActor);
        Arena.Actors(Enemies((uint)OID.EliteOnmitsu));
    }
}
