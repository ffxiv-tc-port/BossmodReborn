namespace BossMod.Heavensward.Raid.A8NBruteJustice;

sealed class PerpetualRay(BossModule module) : Components.SingleTargetCast(module, (uint)AID.PerpetualRay);
// 擊退距離沒有證據，只當全體傷害處理。
sealed class SuperCyclone(BossModule module) : Components.RaidwideCast(module, (uint)AID.SuperCyclone);
sealed class DoubleRocketPunch(BossModule module) : Components.CastSharedTankbuster(module, (uint)AID.DoubleRocketPunch, 3f);
sealed class SuperJump(BossModule module) : Components.SpreadFromCastTargets(module, (uint)AID.SuperJump, 10f);
// 標記到判定的秒數沒有錄影，5 秒只影響 AI 開始集合的時機。
sealed class LongNeedleStack(BossModule module) : Components.StackWithIcon(module, (uint)IconID.LongNeedle, (uint)AID.LongNeedleStack, 6f, 5d);

// 標記上的球數＝圈裡要剛好幾個人；人數不對才會受傷，所以正確分攤時不算全體傷害。
// 標記到判定的秒數沒有錄影，5 秒只影響 AI 開始集合的時機。
sealed class Enumeration(BossModule module) : Components.GenericStackSpread(module, raidwideOnResolve: false)
{
    public override void OnEventIcon(Actor actor, uint iconID, ulong targetID)
    {
        var count = iconID switch
        {
            (uint)IconID.Enumeration2 => 2,
            (uint)IconID.Enumeration3 => 3,
            (uint)IconID.Enumeration4 => 4,
            _ => 0
        };
        if (count != 0)
            Stacks.Add(new(actor, 5f, count, count, WorldState.FutureTime(5d)));
    }

    public override void OnEventCast(Actor caster, ActorCastEvent spell)
    {
        if (spell.Action.ID == (uint)AID.Enumeration)
            Stacks.Clear();
    }
}

// 讀條結束後首領朝前方持續放射線（約 5 秒，期間會轉向），每次判定都重抓首領當下的位置與面向。
// 角度沒有證據（沒有 Omen）：攻略只說「站到首領背後」，畫前方半圓（保守側）；半徑＝EffectRange 25＋首領 hitbox。
sealed class ApocalypticRay(BossModule module) : Components.GenericAOEs(module)
{
    private readonly AOEInstance[] _aoe = new AOEInstance[1];
    private Actor? _caster;
    private DateTime _end;

    public override ReadOnlySpan<AOEInstance> ActiveAOEs(int slot, Actor actor) => _caster != null ? _aoe : [];

    public override void OnCastStarted(Actor caster, ActorCastInfo spell)
    {
        if (spell.Action.ID == (uint)AID.ApocalypticRay)
        {
            _caster = caster;
            var activation = Module.CastFinishAt(spell);
            _end = activation.AddSeconds(5.5d);
            _aoe[0] = new(new AOEShapeCone(25f + caster.HitboxRadius, 90f.Degrees()), caster.Position, caster.Rotation, activation);
        }
    }

    public override void OnEventCast(Actor caster, ActorCastEvent spell)
    {
        if (spell.Action.ID == (uint)AID.ApocalypticRayHit && _caster != null)
        {
            var next = WorldState.FutureTime(1.5d);
            if (next > _end)
                _end = next;
        }
    }

    public override void Update()
    {
        if (_caster == null)
            return;
        if (_caster.IsDestroyed || _caster.IsDead || WorldState.CurrentTime > _end)
        {
            _caster = null;
            return;
        }
        _aoe[0].Origin = _caster.Position;
        _aoe[0].Rotation = _caster.Rotation;
    }
}

sealed class A8NBruteJusticeStates : StateMachineBuilder
{
    public A8NBruteJusticeStates(BossModule module) : base(module)
    {
        TrivialPhase()
            .ActivateOnEnter<PerpetualRay>()
            .ActivateOnEnter<SuperCyclone>()
            .ActivateOnEnter<DoubleRocketPunch>()
            .ActivateOnEnter<SuperJump>()
            .ActivateOnEnter<LongNeedleStack>()
            .ActivateOnEnter<Enumeration>()
            .ActivateOnEnter<ApocalypticRay>()
            .ActivateOnEnter<Components.UnclaimedCastAOEs>();
    }
}

// 場地＝台服碰撞在 (0,0) 的 48×48 方形平台（四周是落差），內縮 0.5 碼；與既有障礙物圖 523.139 一致。
public abstract class A8NBruteJustice(WorldState ws, Actor primary) : UnverifiedOidTrialModule(ws, primary, default, new ArenaBoundsSquare(23.5f))
{
    private static readonly uint[] Bosses = [(uint)OID.OnslaughterA, (uint)OID.OnslaughterB, (uint)OID.BruteJusticeA, (uint)OID.BruteJusticeB,
        (uint)OID.BlasterA, (uint)OID.BlasterB, (uint)OID.BrawlerA, (uint)OID.BrawlerB, (uint)OID.SwindlerA, (uint)OID.SwindlerB, (uint)OID.VortexerA, (uint)OID.VortexerB];

    protected override void DrawEnemies(int pcSlot, Actor pc)
    {
        for (var i = 0; i < Bosses.Length; ++i)
            Arena.Actors(Enemies(Bosses[i]));
    }
}

[ModuleInfo(BossModuleInfo.Maturity.WIP, StatesType = typeof(A8NBruteJusticeStates), PrimaryActorOID = (uint)OID.OnslaughterA, Contributors = "ffxiv-tc-port", Expansion = BossModuleInfo.Expansion.Heavensward, Category = BossModuleInfo.Category.Raid, GroupType = BossModuleInfo.GroupType.CFC, GroupID = 139u, NameID = 4707u, SortOrder = 1)]
public sealed class A8NOnslaughterA(WorldState ws, Actor primary) : A8NBruteJustice(ws, primary);

[ModuleInfo(BossModuleInfo.Maturity.WIP, StatesType = typeof(A8NBruteJusticeStates), PrimaryActorOID = (uint)OID.OnslaughterB, Contributors = "ffxiv-tc-port", Expansion = BossModuleInfo.Expansion.Heavensward, Category = BossModuleInfo.Category.Raid, GroupType = BossModuleInfo.GroupType.CFC, GroupID = 139u, NameID = 4707u, SortOrder = 1)]
public sealed class A8NOnslaughterB(WorldState ws, Actor primary) : A8NBruteJustice(ws, primary);

[ModuleInfo(BossModuleInfo.Maturity.WIP, StatesType = typeof(A8NBruteJusticeStates), PrimaryActorOID = (uint)OID.BrawlerA, Contributors = "ffxiv-tc-port", Expansion = BossModuleInfo.Expansion.Heavensward, Category = BossModuleInfo.Category.Raid, GroupType = BossModuleInfo.GroupType.CFC, GroupID = 139u, NameID = 4703u, SortOrder = 2)]
public sealed class A8NBrawlerA(WorldState ws, Actor primary) : A8NBruteJustice(ws, primary);

[ModuleInfo(BossModuleInfo.Maturity.WIP, StatesType = typeof(A8NBruteJusticeStates), PrimaryActorOID = (uint)OID.BrawlerB, Contributors = "ffxiv-tc-port", Expansion = BossModuleInfo.Expansion.Heavensward, Category = BossModuleInfo.Category.Raid, GroupType = BossModuleInfo.GroupType.CFC, GroupID = 139u, NameID = 4703u, SortOrder = 2)]
public sealed class A8NBrawlerB(WorldState ws, Actor primary) : A8NBruteJustice(ws, primary);

[ModuleInfo(BossModuleInfo.Maturity.WIP, StatesType = typeof(A8NBruteJusticeStates), PrimaryActorOID = (uint)OID.VortexerA, Contributors = "ffxiv-tc-port", Expansion = BossModuleInfo.Expansion.Heavensward, Category = BossModuleInfo.Category.Raid, GroupType = BossModuleInfo.GroupType.CFC, GroupID = 139u, NameID = 4706u, SortOrder = 2)]
public sealed class A8NVortexerA(WorldState ws, Actor primary) : A8NBruteJustice(ws, primary);

[ModuleInfo(BossModuleInfo.Maturity.WIP, StatesType = typeof(A8NBruteJusticeStates), PrimaryActorOID = (uint)OID.VortexerB, Contributors = "ffxiv-tc-port", Expansion = BossModuleInfo.Expansion.Heavensward, Category = BossModuleInfo.Category.Raid, GroupType = BossModuleInfo.GroupType.CFC, GroupID = 139u, NameID = 4706u, SortOrder = 2)]
public sealed class A8NVortexerB(WorldState ws, Actor primary) : A8NBruteJustice(ws, primary);

[ModuleInfo(BossModuleInfo.Maturity.WIP, StatesType = typeof(A8NBruteJusticeStates), PrimaryActorOID = (uint)OID.SwindlerA, Contributors = "ffxiv-tc-port", Expansion = BossModuleInfo.Expansion.Heavensward, Category = BossModuleInfo.Category.Raid, GroupType = BossModuleInfo.GroupType.CFC, GroupID = 139u, NameID = 4705u, SortOrder = 3)]
public sealed class A8NSwindlerA(WorldState ws, Actor primary) : A8NBruteJustice(ws, primary);

[ModuleInfo(BossModuleInfo.Maturity.WIP, StatesType = typeof(A8NBruteJusticeStates), PrimaryActorOID = (uint)OID.SwindlerB, Contributors = "ffxiv-tc-port", Expansion = BossModuleInfo.Expansion.Heavensward, Category = BossModuleInfo.Category.Raid, GroupType = BossModuleInfo.GroupType.CFC, GroupID = 139u, NameID = 4705u, SortOrder = 3)]
public sealed class A8NSwindlerB(WorldState ws, Actor primary) : A8NBruteJustice(ws, primary);

[ModuleInfo(BossModuleInfo.Maturity.WIP, StatesType = typeof(A8NBruteJusticeStates), PrimaryActorOID = (uint)OID.BlasterA, Contributors = "ffxiv-tc-port", Expansion = BossModuleInfo.Expansion.Heavensward, Category = BossModuleInfo.Category.Raid, GroupType = BossModuleInfo.GroupType.CFC, GroupID = 139u, NameID = 4699u, SortOrder = 3)]
public sealed class A8NBlasterA(WorldState ws, Actor primary) : A8NBruteJustice(ws, primary);

[ModuleInfo(BossModuleInfo.Maturity.WIP, StatesType = typeof(A8NBruteJusticeStates), PrimaryActorOID = (uint)OID.BlasterB, Contributors = "ffxiv-tc-port", Expansion = BossModuleInfo.Expansion.Heavensward, Category = BossModuleInfo.Category.Raid, GroupType = BossModuleInfo.GroupType.CFC, GroupID = 139u, NameID = 4699u, SortOrder = 3)]
public sealed class A8NBlasterB(WorldState ws, Actor primary) : A8NBruteJustice(ws, primary);

[ModuleInfo(BossModuleInfo.Maturity.WIP, StatesType = typeof(A8NBruteJusticeStates), PrimaryActorOID = (uint)OID.BruteJusticeA, Contributors = "ffxiv-tc-port", Expansion = BossModuleInfo.Expansion.Heavensward, Category = BossModuleInfo.Category.Raid, GroupType = BossModuleInfo.GroupType.CFC, GroupID = 139u, NameID = 4708u, SortOrder = 4)]
public sealed class A8NBruteJusticeA(WorldState ws, Actor primary) : A8NBruteJustice(ws, primary);

[ModuleInfo(BossModuleInfo.Maturity.WIP, StatesType = typeof(A8NBruteJusticeStates), PrimaryActorOID = (uint)OID.BruteJusticeB, Contributors = "ffxiv-tc-port", Expansion = BossModuleInfo.Expansion.Heavensward, Category = BossModuleInfo.Category.Raid, GroupType = BossModuleInfo.GroupType.CFC, GroupID = 139u, NameID = 4708u, SortOrder = 4)]
public sealed class A8NBruteJusticeB(WorldState ws, Actor primary) : A8NBruteJustice(ws, primary);
