namespace BossMod.Stormblood.Raid.O2NCatastrophe;

sealed class GravitationalWave(BossModule module) : Components.RaidwideCast(module, (uint)AID.GravitationalWave);
sealed class Evilsphere(BossModule module) : Components.SingleTargetCast(module, (uint)AID.Evilsphere);
sealed class DemonEye(BossModule module) : Components.CastGaze(module, (uint)AID.DemonEye);
sealed class Earthquake(BossModule module) : Components.CastHint(module, (uint)AID.Earthquake, "Levitate");

// 重力爆發：頭上標記 0x71 的人約 7.2 秒後吃半徑 7 的分攤，要全隊浮空一起分。判定有兩個同形狀的 ID，兩個都接受；
// 都沒看到時過了預定時間 10 秒自動清掉。全隊分攤最少與最多都設 8：最少小於最多時，圈外的人會同時收到「進圈」與「別進圈」兩個禁區。
sealed class GravitationalExplosion(BossModule module) : Components.UniformStackSpread(module, 7f, default, 8, 8)
{
    public override void OnEventIcon(Actor actor, uint iconID, ulong targetID)
    {
        if (iconID == (uint)IconID.GravitationalExplosion)
        {
            AddStack(actor, WorldState.FutureTime(7.2d));
        }
    }

    public override void OnEventCast(Actor caster, ActorCastEvent spell)
    {
        if (spell.Action.ID is (uint)AID.GravitationalExplosion or (uint)AID.GravitationalExplosionAlt)
        {
            Stacks.Clear();
        }
    }

    public override void Update()
    {
        var expired = WorldState.FutureTime(-10d);
        Stacks.RemoveAll(s => s.Activation < expired || s.Target.IsDead);
    }
}

// 浮空要用任務指令，AI 不會按，只顯示提示。暗黑光的石球爆炸要不要浮空看顏色與次數、詛咒波（沒有讀條的坦克扇形）、
// 觸手亂舞／震源製造之後哪根觸手放主震、重力-100 的安全區都沒有足夠證據，刻意不處理；侵入照讀條預兆交給後備。
sealed class O2NCatastropheStates : StateMachineBuilder
{
    public O2NCatastropheStates(BossModule module) : base(module)
    {
        TrivialPhase()
            .ActivateOnEnter<GravitationalWave>()
            .ActivateOnEnter<Evilsphere>()
            .ActivateOnEnter<DemonEye>()
            .ActivateOnEnter<Earthquake>()
            .ActivateOnEnter<GravitationalExplosion>()
            .ActivateOnEnter<Components.UnclaimedCastAOEs>();
    }
}

// 場地＝台服碰撞在原點的圓形地板（半徑約 20，外圈另一塊地板之間有隔線），內縮 0.5 碼。
public abstract class O2NCatastrophe(WorldState ws, Actor primary) : UnverifiedOidTrialModule(ws, primary, default, new ArenaBoundsCircle(19.5f));

[ModuleInfo(BossModuleInfo.Maturity.WIP, StatesType = typeof(O2NCatastropheStates), PrimaryActorOID = (uint)OID.BossCandidateA, Contributors = "ffxiv-tc-port", Expansion = BossModuleInfo.Expansion.Stormblood, Category = BossModuleInfo.Category.Raid, GroupType = BossModuleInfo.GroupType.CFC, GroupID = 253u, NameID = 5631u, SortOrder = 1)]
public sealed class O2NCatastropheA(WorldState ws, Actor primary) : O2NCatastrophe(ws, primary);

[ModuleInfo(BossModuleInfo.Maturity.WIP, StatesType = typeof(O2NCatastropheStates), PrimaryActorOID = (uint)OID.BossCandidateB, Contributors = "ffxiv-tc-port", Expansion = BossModuleInfo.Expansion.Stormblood, Category = BossModuleInfo.Category.Raid, GroupType = BossModuleInfo.GroupType.CFC, GroupID = 253u, NameID = 5631u, SortOrder = 1)]
public sealed class O2NCatastropheB(WorldState ws, Actor primary) : O2NCatastrophe(ws, primary);
