namespace BossMod.RealmReborn.Dungeon.D14Praetorium.D143Gaius;

public enum OID : uint
{
    Boss = 0x3875, // x1

    PhantomGaiusSide = 0x3876, // R1.65, untargetable
    PhantomGaiusAdd = 0x3877, // R1.65, adds that become targetable on low hp
    TerminusEst = 0x3878, // R=1.0
    Helper = 0x233C
}

public enum AID : uint
{
    AutoAttack = 870, // Boss->player, no cast, single-target

    Phantasmata = 28484, // Boss->self, 3.0s cast, single-target, visual (shows 'side' untargetable adds)
    PhantasmataShow = 28485, // PhantomGaiusSide/PhantomGaiusAdd->location, no cast, single-target (become visible)
    TerminusEstSummon = 28486, // Boss/PhantomGaiusSide->self, no cast, single-target, visual (show 'X' and spawn TerminusEst)
    TerminusEstTriple = 28487, // TerminusEst->self, 3.0s cast, range 40 width 4 rect, always three neighbouring casters
    TerminusEstQuintuple = 28488, // TerminusEst->self, 3.0s cast, range 40 width 4 rect, always five casters with gaps between
    TerminusEstVisual = 29779, // Helper->self, 6.0s cast, range 40 width 12 rect, no effect?..

    HandOfTheEmpire = 28491, // Boss/PhantomGaiusAdd->self, 3.0s cast, single-target, visual
    HandOfTheEmpireAOE = 28492, // Helper->player, 5.0s cast, range 5 circle spread
    FestinaLente = 28493, // Boss->player, 5.0s cast, range 6 circle stack
    Innocence = 28494, // Boss->player, 5.0s cast, single-target tankbuster
    HorridaBella = 28495, // Boss->self, 5.0s cast, raidwide
    Teleport = 28496, // Boss->location, no cast, single-target

    Ductus = 29051, // Boss/PhantomGaiusAdd->self, 3.0s cast, single-target, visual
    DuctusAOE = 29052, // Helper->location, 5.0s cast, range 8 circle aoe (pseudo exaflare)

    AddPhaseStart = 28497, // Boss->self, no cast, single-target, visual (enemy 'lb' gauge starts filling over 90 secs)
    Heirsbane = 28498, // PhantomGaiusAdd->player, 5.0s cast, single-target damage
    VeniVidiVici = 28499, // Boss->self, no cast, raidwide on last add death
    VeniVidiViciEnrage = 28500 // Boss->self, no cast, enrage (if adds aren't killed in 90s)
}

class TerminusEst(BossModule module) : Components.GenericAOEs(module)
{
    private readonly List<AOEInstance> _aoes = [];

    private static readonly AOEShapeRect rect = new(40f, 2f);

    public override ReadOnlySpan<AOEInstance> ActiveAOEs(int slot, Actor actor) => CollectionsMarshal.AsSpan(_aoes);

    public override void OnActorCreated(Actor actor)
    {
        // 6s is just a rough guess for the telegraph-only phase before the actor actually starts casting; once the real
        // cast is visible (see Update below) we switch to the server-reported time so pathfinding sees the true, possibly
        // staggered, per-caster resolve order instead of assuming they're all simultaneous
        if (actor.OID == (uint)OID.TerminusEst)
            _aoes.Add(new(rect, actor.Position.Quantized(), actor.Rotation, WorldState.FutureTime(6d), actorID: actor.InstanceID));
    }

    public override void Update()
    {
        var span = CollectionsMarshal.AsSpan(_aoes);
        for (var i = 0; i < span.Length; ++i)
        {
            ref var aoe = ref span[i];
            var caster = WorldState.Actors.Find(aoe.ActorID);
            if (caster?.CastInfo != null && (caster.CastInfo.IsSpell(AID.TerminusEstTriple) || caster.CastInfo.IsSpell(AID.TerminusEstQuintuple)))
                aoe.Activation = WorldState.FutureTime(caster.CastInfo.NPCRemainingTime);
        }
    }

    public override void OnEventCast(Actor caster, ActorCastEvent spell)
    {
        if (spell.Action.ID is (uint)AID.TerminusEstTriple or (uint)AID.TerminusEstQuintuple)
            _aoes.Clear();
    }
}

class HandOfTheEmpire(BossModule module) : Components.SpreadFromCastTargets(module, (uint)AID.HandOfTheEmpireAOE, 5f);
class FestinaLente(BossModule module) : Components.StackWithCastTargets(module, (uint)AID.FestinaLente, 6f, 4, 4);
class Innocence(BossModule module) : Components.SingleTargetCast(module, (uint)AID.Innocence);
class HorridaBella(BossModule module) : Components.RaidwideCast(module, (uint)AID.HorridaBella);
// 每輪 4 波、每波 3 發（每欄一發）：第一波落在南北兩端那一列，之後每約 2 秒往另一端前進一列。
// 看到第一波就把同欄後三波連同各自的生效時間一起交給 AI，一開始就能選對整輪的位置，不必每 2 秒重新轉向；
// 畫面只畫接下來兩波，最早那波用危險色，看得出先後。
class Ductus(BossModule module) : Components.GenericAOEs(module, (uint)AID.DuctusAOE)
{
    private const float RowStep = 11.5f;
    private const double WaveInterval = 2d;
    private static readonly AOEShapeCircle circle = new(8f);
    private const int ShownWaves = 2;
    private readonly List<AOEInstance> _aoes = new(12);
    private readonly List<AOEInstance> _shown = new(12);

    // AI 拿整輪（各自的生效時間）；畫面與文字提示只看 ShownAOEs
    public override ReadOnlySpan<AOEInstance> ActiveAOEs(int slot, Actor actor) => CollectionsMarshal.AsSpan(_aoes);

    // 只留最早一波（危險色）與之後 ShownWaves-1 波（一般色）；最早一波排在最後，畫在上面。
    // 同一波 3 發的生效時間差不到一幀、波距約 2 秒，所以用「比最早晚不到 1 秒」分波。
    public ReadOnlySpan<AOEInstance> ShownAOEs()
    {
        _shown.Clear();
        var aoes = CollectionsMarshal.AsSpan(_aoes);
        var len = aoes.Length;
        if (len == 0)
            return [];
        var first = aoes[0].Activation;
        for (var i = 1; i < len; ++i)
        {
            if (aoes[i].Activation < first)
                first = aoes[i].Activation;
        }
        var imminentLimit = first.AddSeconds(1d);
        var shownLimit = imminentLimit.AddSeconds((ShownWaves - 1) * WaveInterval);
        for (var i = 0; i < len; ++i)
        {
            ref readonly var a = ref aoes[i];
            if (a.Activation >= imminentLimit && a.Activation < shownLimit)
                _shown.Add(new(a.Shape, a.Origin, a.Rotation, a.Activation));
        }
        var danger = Colors.Danger;
        for (var i = 0; i < len; ++i)
        {
            ref readonly var a = ref aoes[i];
            if (a.Activation < imminentLimit)
                _shown.Add(new(a.Shape, a.Origin, a.Rotation, a.Activation, danger));
        }
        return CollectionsMarshal.AsSpan(_shown);
    }

    public override void AddHints(int slot, Actor actor, TextHints hints)
    {
        var aoes = ShownAOEs();
        for (var i = 0; i < aoes.Length; ++i)
        {
            if (aoes[i].Check(actor.Position))
            {
                hints.Add(Loc.T(WarningText));
                return;
            }
        }
    }

    public override void DrawArenaBackground(int pcSlot, Actor pc)
    {
        var aoes = ShownAOEs();
        for (var i = 0; i < aoes.Length; ++i)
        {
            ref readonly var c = ref aoes[i];
            c.Shape.Draw(Arena, c.Origin, c.Rotation, c.Color);
        }
    }

    public override void OnCastStarted(Actor caster, ActorCastInfo spell)
    {
        if (spell.Action.ID != WatchedAction)
            return;
        var loc = spell.LocXZ;
        var finish = Module.CastFinishAt(spell);
        var idx = Find(loc);
        if (idx >= 0)
        {
            _aoes.Ref(idx).Activation = finish;
            return;
        }
        _aoes.Add(new(circle, loc, default, finish));
        // 只從端列往回推：中途才看到的那一發（例如模組晚建）不知道往哪走，不推
        var offset = loc.Z - Arena.Center.Z;
        if (Math.Abs(offset) > RowStep)
        {
            var step = new WDir(default, offset > 0f ? -RowStep : RowStep);
            for (var k = 1; k <= 3; ++k)
                _aoes.Add(new(circle, (loc + k * step).Quantized(), default, finish.AddSeconds(k * WaveInterval)));
        }
    }

    public override void OnEventCast(Actor caster, ActorCastEvent spell)
    {
        if (spell.Action.ID == WatchedAction)
        {
            ++NumCasts;
            var idx = Find(spell.TargetXZ);
            if (idx >= 0)
                _aoes.RemoveAt(idx);
        }
    }

    public override void Update()
    {
        // 推錯、始終沒等到讀條的預測，過了生效時間 1 秒就丟掉
        var limit = WorldState.CurrentTime.AddSeconds(-1d);
        for (var i = _aoes.Count - 1; i >= 0; --i)
        {
            if (_aoes[i].Activation < limit)
                _aoes.RemoveAt(i);
        }
    }

    private int Find(WPos pos)
    {
        var best = -1;
        var aoes = CollectionsMarshal.AsSpan(_aoes);
        for (var i = 0; i < aoes.Length; ++i)
        {
            ref readonly var a = ref aoes[i];
            if (a.Origin.InCircle(pos, 1f) && (best < 0 || a.Activation < aoes[best].Activation))
                best = i;
        }
        return best;
    }
}

class AddEnrage(BossModule module) : BossComponent(module)
{
    private DateTime _enrage;

    public override void AddGlobalHints(GlobalHints hints)
    {
        if (_enrage != default)
            hints.Add($"Enrage in {(_enrage - WorldState.CurrentTime).TotalSeconds:f1}s");
    }

    public override void Update()
    {
        var primary = Module.PrimaryActor.IsTargetable;
        var enrage = _enrage == default;
        if (enrage && !primary)
            _enrage = WorldState.FutureTime(92.4d);
        else if (!enrage && primary)
            _enrage = default;
    }
}

class Heirsbane(BossModule module) : Components.SingleTargetCast(module, (uint)AID.Heirsbane, "");

class D143GaiusStates : StateMachineBuilder
{
    public D143GaiusStates(BossModule module) : base(module)
    {
        TrivialPhase()
            .ActivateOnEnter<TerminusEst>()
            .ActivateOnEnter<HandOfTheEmpire>()
            .ActivateOnEnter<FestinaLente>()
            .ActivateOnEnter<Innocence>()
            .ActivateOnEnter<HorridaBella>()
            .ActivateOnEnter<Ductus>()
            .ActivateOnEnter<AddEnrage>()
            .ActivateOnEnter<Heirsbane>();
    }
}

[ModuleInfo(BossModuleInfo.Maturity.Verified, Contributors = "Malediktus", GroupType = BossModuleInfo.GroupType.CFC, GroupID = 16, NameID = 2136)]
public class D143Gaius(WorldState ws, Actor primary) : BossModule(ws, primary, new(-562f, 220f), new ArenaBoundsRect(14.5f, 19.5f))
{
    protected override void DrawEnemies(int pcSlot, Actor pc)
    {
        Arena.Actor(PrimaryActor);
        Arena.Actors(Enemies((uint)OID.PhantomGaiusAdd));
    }
}
