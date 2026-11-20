namespace BossMod.Heavensward.Dungeon.D07Neverreap.D072CanuVanu;

public enum OID : uint
{
    Boss = 0xF82, // 卡努瓦努
    CanuVanusTotem = 0xEC8 // 卡努瓦努圖騰
}

// 沒有列技能：圖騰再生的圓（Omen undeadraise）、瓦努擊的扇形交給 UnclaimedCastAOEs；
// 圖騰再生本身不傷人（攻略），被畫成危險區只會讓 AI 帶著圖騰離開圓，方向一致所以不認領。
sealed class D072CanuVanuStates : StateMachineBuilder
{
    public D072CanuVanuStates(BossModule module) : base(module)
    {
        TrivialPhase()
            .ActivateOnEnter<Components.UnclaimedCastAOEs>();
    }
}

// 場地與尋路沿用沒有模組時的做法（跟著玩家走的方框）：這個副本沒有障礙物圖，也沒有錄影可量場地。
[ModuleInfo(BossModuleInfo.Maturity.WIP, Contributors = "ffxiv-tc-port", GroupType = BossModuleInfo.GroupType.CFC, GroupID = 33, NameID = 3734)]
public sealed class D072CanuVanu(WorldState ws, Actor primary) : SimpleBossModule(ws, primary)
{
    // 攻略：留在圓裡的圖騰會變成卡努瓦努圖騰並讓首領無敵，要打倒。
    protected override void CalculateModuleAIHints(int slot, Actor actor, PartyRolesConfig.Assignment assignment, AIHints hints)
    {
        base.CalculateModuleAIHints(slot, actor, assignment, hints);
        var count = hints.PotentialTargets.Count;
        for (var i = 0; i < count; ++i)
        {
            var e = hints.PotentialTargets[i];
            if (e.Actor.OID == (uint)OID.CanuVanusTotem)
                e.Priority = 1;
        }
    }

    protected override void DrawEnemies(int pcSlot, Actor pc)
    {
        Arena.Actor(PrimaryActor);
        Arena.Actors(Enemies((uint)OID.CanuVanusTotem));
    }
}
