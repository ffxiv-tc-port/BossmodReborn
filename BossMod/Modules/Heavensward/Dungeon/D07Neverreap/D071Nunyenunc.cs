namespace BossMod.Heavensward.Dungeon.D07Neverreap.D071Nunyenunc;

public enum OID : uint
{
    Boss = 0xF6B, // 奴涅努克怪鳥
    NunyenuncsShadow = 0xF80, // 奴涅努克之影
    NunyenuncsFeather = 0xF81 // 奴涅努克之羽
}

// 沒有列技能：狂風暴羽有 Omen，交給 UnclaimedCastAOEs 照後備猜法處理。
sealed class D071NunyenuncStates : StateMachineBuilder
{
    public D071NunyenuncStates(BossModule module) : base(module)
    {
        TrivialPhase()
            .ActivateOnEnter<Components.UnclaimedCastAOEs>();
    }
}

// 場地與尋路沿用沒有模組時的做法（跟著玩家走的方框）：這個副本沒有障礙物圖，也沒有錄影可量場地。
[ModuleInfo(BossModuleInfo.Maturity.WIP, Contributors = "ffxiv-tc-port", GroupType = BossModuleInfo.GroupType.CFC, GroupID = 33, NameID = 3726)]
public sealed class D071Nunyenunc(WorldState ws, Actor primary) : SimpleBossModule(ws, primary)
{
    // 攻略：羽毛會爆炸並上易傷，要盡快打掉；霧中首領消失時要找到影子打倒。
    protected override void CalculateModuleAIHints(int slot, Actor actor, PartyRolesConfig.Assignment assignment, AIHints hints)
    {
        base.CalculateModuleAIHints(slot, actor, assignment, hints);
        var count = hints.PotentialTargets.Count;
        for (var i = 0; i < count; ++i)
        {
            var e = hints.PotentialTargets[i];
            if (e.Actor.OID is (uint)OID.NunyenuncsFeather or (uint)OID.NunyenuncsShadow)
                e.Priority = 1;
        }
    }

    protected override void DrawEnemies(int pcSlot, Actor pc)
    {
        Arena.Actor(PrimaryActor);
        Arena.Actors(Enemies((uint)OID.NunyenuncsShadow));
        Arena.Actors(Enemies((uint)OID.NunyenuncsFeather));
    }
}
