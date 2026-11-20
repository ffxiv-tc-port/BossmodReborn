namespace BossMod.Heavensward.Raid.A7NQuickthinxAllthoughts;

sealed class Sizzlespark1(BossModule module) : Components.RaidwideCast(module, (uint)AID.Sizzlespark1);
sealed class Sizzlespark2(BossModule module) : Components.RaidwideCast(module, (uint)AID.Sizzlespark2);
sealed class Kugelblitz1(BossModule module) : Components.RaidwideCast(module, (uint)AID.Kugelblitz1);
sealed class Kugelblitz2(BossModule module) : Components.RaidwideCast(module, (uint)AID.Kugelblitz2);
sealed class Kugelblitz1Stun(BossModule module) : Components.CastInterruptHint(module, (uint)AID.Kugelblitz1, canBeInterrupted: false, canBeStunned: true);
sealed class Kugelblitz2Stun(BossModule module) : Components.CastInterruptHint(module, (uint)AID.Kugelblitz2, canBeInterrupted: false, canBeStunned: true);

// 讀條期間的直線：形狀同沒有模組時的猜法（長度＝EffectRange＋施放者 hitbox）；兩個 ID 都列進列舉後後備就不畫，所以自己畫。
sealed class Sizzlebeam(BossModule module) : Components.GenericAOEs(module)
{
    private readonly List<AOEInstance> _aoes = [];

    public override ReadOnlySpan<AOEInstance> ActiveAOEs(int slot, Actor actor) => CollectionsMarshal.AsSpan(_aoes);

    public override void OnCastStarted(Actor caster, ActorCastInfo spell)
    {
        if (spell.Action.ID is (uint)AID.Sizzlebeam1 or (uint)AID.Sizzlebeam2)
            _aoes.Add(new(new AOEShapeRect(60f + caster.HitboxRadius, 3f), spell.LocXZ, spell.Rotation, Module.CastFinishAt(spell), actorID: caster.InstanceID));
    }

    public override void OnCastFinished(Actor caster, ActorCastInfo spell)
    {
        if (spell.Action.ID is (uint)AID.Sizzlebeam1 or (uint)AID.Sizzlebeam2)
            _aoes.RemoveAll(a => a.ActorID == caster.InstanceID);
    }
}

// 標記到讀條：首領會轉向被點名的人放直線，所以讀條前先叫其他人離開首領→被點名者這條線。
// 標記到讀條的秒數沒有錄影，5 秒只影響 AI 的急迫度；讀條一開始就交給 Sizzlebeam 畫固定方向。
sealed class SizzlebeamBait(BossModule module) : Components.GenericBaitAway(module)
{
    public override void OnEventIcon(Actor actor, uint iconID, ulong targetID)
    {
        if (iconID == (uint)IconID.Sizzlebeam)
            CurrentBaits.Add(new(Module.PrimaryActor, actor, new AOEShapeRect(60f + Module.PrimaryActor.HitboxRadius, 3f), WorldState.FutureTime(5d)));
    }

    public override void OnCastStarted(Actor caster, ActorCastInfo spell)
    {
        if (spell.Action.ID is (uint)AID.Sizzlebeam1 or (uint)AID.Sizzlebeam2)
            CurrentBaits.Clear();
    }

    public override void Update()
    {
        CurrentBaits.RemoveAll(b => b.Target.IsDead);
    }
}

sealed class A7NQuickthinxAllthoughtsStates : StateMachineBuilder
{
    public A7NQuickthinxAllthoughtsStates(BossModule module) : base(module)
    {
        TrivialPhase()
            .ActivateOnEnter<Sizzlespark1>()
            .ActivateOnEnter<Sizzlespark2>()
            .ActivateOnEnter<Kugelblitz1>()
            .ActivateOnEnter<Kugelblitz2>()
            .ActivateOnEnter<Kugelblitz1Stun>()
            .ActivateOnEnter<Kugelblitz2Stun>()
            .ActivateOnEnter<Sizzlebeam>()
            .ActivateOnEnter<SizzlebeamBait>()
            .ActivateOnEnter<Components.UnclaimedCastAOEs>();
    }
}

// 場地＝台服碰撞在 (0,0) 的 60×60 方形地板，內縮 0.5 碼；四角的籠子牆只在關人時出現，這裡不畫。
public abstract class A7NQuickthinxAllthoughts(WorldState ws, Actor primary) : UnverifiedOidTrialModule(ws, primary, default, new ArenaBoundsSquare(29.5f));

[ModuleInfo(BossModuleInfo.Maturity.WIP, StatesType = typeof(A7NQuickthinxAllthoughtsStates), PrimaryActorOID = (uint)OID.BossCandidateA, Contributors = "ffxiv-tc-port", Expansion = BossModuleInfo.Expansion.Heavensward, Category = BossModuleInfo.Category.Raid, GroupType = BossModuleInfo.GroupType.CFC, GroupID = 138u, NameID = 3376u, SortOrder = 1)]
public sealed class A7NQuickthinxAllthoughtsA(WorldState ws, Actor primary) : A7NQuickthinxAllthoughts(ws, primary);

[ModuleInfo(BossModuleInfo.Maturity.WIP, StatesType = typeof(A7NQuickthinxAllthoughtsStates), PrimaryActorOID = (uint)OID.BossCandidateB, Contributors = "ffxiv-tc-port", Expansion = BossModuleInfo.Expansion.Heavensward, Category = BossModuleInfo.Category.Raid, GroupType = BossModuleInfo.GroupType.CFC, GroupID = 138u, NameID = 3376u, SortOrder = 1)]
public sealed class A7NQuickthinxAllthoughtsB(WorldState ws, Actor primary) : A7NQuickthinxAllthoughts(ws, primary);
