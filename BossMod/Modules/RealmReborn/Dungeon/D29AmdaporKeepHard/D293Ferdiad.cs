namespace BossMod.RealmReborn.Dungeon.D29AmdaporKeepHard.D293Ferdiad;

public enum OID : uint
{
    Boss = 0xD68, // 弗迪亞
    WickedJester = 0xD6B // 邪輪小丑
}

// 契約：這個列舉＝模組知道的技能。列舉以外的敵方讀條由 Components.UnclaimedCastAOEs 照沒有模組時的猜法處理，
// 所以刻意不把其他技能列進來（列了就不再被猜）。
public enum AID : uint
{
    Blackout = 3182 // Boss->self, 3.0s cast, range 60 circle raidwide
}

sealed class Blackout(BossModule module) : Components.RaidwideCast(module, (uint)AID.Blackout);
// 全能牌 3183（讀條 24 秒、範圍隨時間變大）要在讀條結束前打倒連線的邪輪小丑，否則全滅：等同狂暴，只給提示。
// 刻意不列進 AID：留給後備猜法照 EXD 畫首領腳下的小圓（真實範圍會變大，沒有證據畫多大）。
sealed class WildCard(BossModule module) : Components.CastHint(module, 3183u, "Enrage", true);

sealed class D293FerdiadStates : StateMachineBuilder
{
    public D293FerdiadStates(BossModule module) : base(module)
    {
        TrivialPhase()
            .ActivateOnEnter<Blackout>()
            .ActivateOnEnter<WildCard>()
            .ActivateOnEnter<Components.UnclaimedCastAOEs>();
    }
}

// 場地與尋路沿用沒有模組時的做法（跟著玩家走的方框）：這個副本沒有障礙物圖，也沒有錄影可量場地。
[ModuleInfo(BossModuleInfo.Maturity.WIP, Contributors = "ffxiv-tc-port", GroupType = BossModuleInfo.GroupType.CFC, GroupID = 29, NameID = 3280)]
public sealed class D293Ferdiad(WorldState ws, Actor primary) : SimpleBossModule(ws, primary)
{
    // 連線的小丑沒倒，全能牌會全滅 ⇒ 小丑優先（連線編號沒有證據，所以全部小丑都優先）。
    protected override void CalculateModuleAIHints(int slot, Actor actor, PartyRolesConfig.Assignment assignment, AIHints hints)
    {
        base.CalculateModuleAIHints(slot, actor, assignment, hints);
        var count = hints.PotentialTargets.Count;
        for (var i = 0; i < count; ++i)
        {
            var e = hints.PotentialTargets[i];
            if (e.Actor.OID == (uint)OID.WickedJester)
                e.Priority = 1;
        }
    }

    protected override void DrawEnemies(int pcSlot, Actor pc)
    {
        Arena.Actor(PrimaryActor);
        Arena.Actors(Enemies((uint)OID.WickedJester));
    }
}
