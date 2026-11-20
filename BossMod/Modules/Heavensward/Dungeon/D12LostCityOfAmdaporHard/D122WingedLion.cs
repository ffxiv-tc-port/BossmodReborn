namespace BossMod.Heavensward.Dungeon.D12LostCityOfAmdaporHard.D122WingedLion;

public enum OID : uint
{
    Boss = 0x14D1, // 雙翼飛獅
    ArcaneSphere = 0x155F // 立體魔法陣
}

// 契約：這個列舉＝模組知道的技能。列舉以外的敵方讀條由 Components.UnclaimedCastAOEs 照沒有模組時的猜法處理，
// 所以刻意不把其他技能列進來（列了就不再被猜）。
public enum AID : uint
{
    AncientHoly = 5577 // Boss->self, 3.0s cast, single-target (goes untargetable, Arcane Sphere grows)
}

sealed class AncientHoly(BossModule module) : Components.CastHint(module, (uint)AID.AncientHoly, "Kill the Arcane Sphere!");

sealed class D122WingedLionStates : StateMachineBuilder
{
    public D122WingedLionStates(BossModule module) : base(module)
    {
        TrivialPhase()
            .ActivateOnEnter<AncientHoly>()
            .ActivateOnEnter<Components.UnclaimedCastAOEs>();
    }
}

// 場地與尋路沿用沒有模組時的做法（跟著玩家走的方框）：這個副本沒有障礙物圖，也沒有錄影可量場地。
[ModuleInfo(BossModuleInfo.Maturity.WIP, Contributors = "ffxiv-tc-port", GroupType = BossModuleInfo.GroupType.CFC, GroupID = 140, NameID = 4745)]
public sealed class D122WingedLion(WorldState ws, Actor primary) : SimpleBossModule(ws, primary)
{
    // 古代神聖期間魔法陣會長大，攻略寫「魔法陣最優先、小怪等魔法陣倒了再打」。
    protected override void CalculateModuleAIHints(int slot, Actor actor, PartyRolesConfig.Assignment assignment, AIHints hints)
    {
        base.CalculateModuleAIHints(slot, actor, assignment, hints);
        var count = hints.PotentialTargets.Count;
        for (var i = 0; i < count; ++i)
        {
            var e = hints.PotentialTargets[i];
            if (e.Actor.OID == (uint)OID.ArcaneSphere)
                e.Priority = 1;
        }
    }

    protected override void DrawEnemies(int pcSlot, Actor pc)
    {
        Arena.Actor(PrimaryActor);
        Arena.Actors(Enemies((uint)OID.ArcaneSphere));
    }
}
