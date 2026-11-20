namespace BossMod.Heavensward.Dungeon.D18SohmAlHard.D183LavaScorpion;

public enum OID : uint
{
    Boss = 0x1918, // 熔岩蠍
    ScorpionsClaw = 0x1919, // 硬螯蠍
    ScorpionsTail = 0x12AC // 尖尾蠍
}

// 契約：這個列舉＝模組知道的技能。列舉以外的敵方讀條由 Components.UnclaimedCastAOEs 照沒有模組時的猜法處理，
// 所以刻意不把其他技能列進來（列了就不再被猜）。
public enum AID : uint
{
    FlyingPress = 7230, // Boss->player, 4.0s cast, range 6 circle
    DeadlyThrust = 7232, // Boss->player, 4.0s cast, single-target
    MoltenSilkDonut = 7234, // Boss->self, 3.0s cast, range 5-10 donut
    Borehole = 7238, // ScorpionsClaw->location, 12.0s cast, single-target
    DeadlyThrustTail = 7240 // ScorpionsTail->player, 3.0s cast, single-target
}

sealed class FlyingPress(BossModule module) : Components.SpreadFromCastTargets(module, (uint)AID.FlyingPress, 6f);
sealed class DeadlyThrust(BossModule module) : Components.SingleTargetCast(module, (uint)AID.DeadlyThrust);
sealed class DeadlyThrustTail(BossModule module) : Components.SingleTargetCast(module, (uint)AID.DeadlyThrustTail);
// CastType 10 的甜甜圈後備猜法內徑一律 3；Omen gl_sircle_1005 是外 10／內 5。
sealed class MoltenSilkDonut(BossModule module) : Components.SimpleAOEs(module, (uint)AID.MoltenSilkDonut, new AOEShapeDonut(5f, 10f));
sealed class Borehole(BossModule module) : Components.CastHint(module, (uint)AID.Borehole, "Kill the Scorpion's Claw!", true);

sealed class D183LavaScorpionStates : StateMachineBuilder
{
    public D183LavaScorpionStates(BossModule module) : base(module)
    {
        TrivialPhase()
            .ActivateOnEnter<FlyingPress>()
            .ActivateOnEnter<DeadlyThrust>()
            .ActivateOnEnter<DeadlyThrustTail>()
            .ActivateOnEnter<MoltenSilkDonut>()
            .ActivateOnEnter<Borehole>()
            .ActivateOnEnter<Components.UnclaimedCastAOEs>();
    }
}

// 場地與尋路沿用沒有模組時的做法（跟著玩家走的方框）：這個副本沒有障礙物圖，也沒有錄影可量場地。
[ModuleInfo(BossModuleInfo.Maturity.WIP, Contributors = "ffxiv-tc-port", GroupType = BossModuleInfo.GroupType.CFC, GroupID = 221, NameID = 5531)]
public sealed class D183LavaScorpion(WorldState ws, Actor primary) : SimpleBossModule(ws, primary)
{
    // 硬螯蠍的打洞讀完前要打倒（攻略），尖尾蠍會放弱化版致命尾刺 ⇒ 硬螯蠍優先，其次尖尾蠍。
    protected override void CalculateModuleAIHints(int slot, Actor actor, PartyRolesConfig.Assignment assignment, AIHints hints)
    {
        base.CalculateModuleAIHints(slot, actor, assignment, hints);
        var count = hints.PotentialTargets.Count;
        for (var i = 0; i < count; ++i)
        {
            var e = hints.PotentialTargets[i];
            e.Priority = e.Actor.OID switch
            {
                (uint)OID.ScorpionsClaw => 2,
                (uint)OID.ScorpionsTail => 1,
                _ => e.Priority
            };
        }
    }

    protected override void DrawEnemies(int pcSlot, Actor pc)
    {
        Arena.Actor(PrimaryActor);
        Arena.Actors(Enemies((uint)OID.ScorpionsClaw));
        Arena.Actors(Enemies((uint)OID.ScorpionsTail));
    }
}
