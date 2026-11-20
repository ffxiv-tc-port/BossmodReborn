namespace BossMod.RealmReborn.Dungeon.D26SunkenTempleOfQarnHard.D262SabotenderEmperatriz;

public enum OID : uint
{
    Boss = 0xD14, // 仙人掌怪女皇
    SabotenderGuardia = 0xD13 // 仙人掌怪守衛
}

// 契約：這個列舉＝模組知道的技能。列舉以外的敵方讀條由 Components.UnclaimedCastAOEs 照沒有模組時的猜法處理，
// 所以刻意不把其他技能列進來（列了就不再被猜）。
public enum AID : uint
{
    HundredThousandNeedles = 3105 // Boss->self, 20.0s cast, range 40 circle, 100,000 damage unless enough damage is dealt during the cast
}

// 十萬根針讀條期間要打出足夠傷害才會中斷，否則全滅：等同狂暴，只給提示。
sealed class HundredThousandNeedles(BossModule module) : Components.CastHint(module, (uint)AID.HundredThousandNeedles, "Enrage", true);

sealed class D262SabotenderEmperatrizStates : StateMachineBuilder
{
    public D262SabotenderEmperatrizStates(BossModule module) : base(module)
    {
        TrivialPhase()
            .ActivateOnEnter<HundredThousandNeedles>()
            .ActivateOnEnter<Components.UnclaimedCastAOEs>();
    }
}

// 場地與尋路沿用沒有模組時的做法（跟著玩家走的方框）：這個副本沒有障礙物圖，也沒有錄影可量場地。
[ModuleInfo(BossModuleInfo.Maturity.WIP, Contributors = "ffxiv-tc-port", GroupType = BossModuleInfo.GroupType.CFC, GroupID = 26, NameID = 3071)]
public sealed class D262SabotenderEmperatriz(WorldState ws, Actor primary) : SimpleBossModule(ws, primary)
{
    // 守衛會對女皇施放仙人盾（3112）⇒ 先打守衛。
    protected override void CalculateModuleAIHints(int slot, Actor actor, PartyRolesConfig.Assignment assignment, AIHints hints)
    {
        base.CalculateModuleAIHints(slot, actor, assignment, hints);
        var count = hints.PotentialTargets.Count;
        for (var i = 0; i < count; ++i)
        {
            var e = hints.PotentialTargets[i];
            if (e.Actor.OID == (uint)OID.SabotenderGuardia)
                e.Priority = 1;
        }
    }

    protected override void DrawEnemies(int pcSlot, Actor pc)
    {
        Arena.Actor(PrimaryActor);
        Arena.Actors(Enemies((uint)OID.SabotenderGuardia));
    }
}
