namespace BossMod.Endwalker.Raid.P11NThemis;

sealed class Eunomia(BossModule module) : Components.RaidwideCast(module, (uint)AID.Eunomia);
sealed class Dike(BossModule module) : Components.SingleTargetCasts(module, [(uint)AID.DikeAOE1, (uint)AID.DikeAOE2]);

// 分歧判決：先中間一條、再兩側或更寬的中間一條；只畫最早的一組，避免兩組疊起來找不到安全處。
sealed class DivisiveOverruling(BossModule module) : Components.GenericAOEs(module)
{
    private readonly List<AOEInstance> _aoes = [];
    private static readonly AOEShapeRect _shapeNarrow = new(46f, 8f);
    private static readonly AOEShapeRect _shapeWide = new(46f, 13f);

    public override ReadOnlySpan<AOEInstance> ActiveAOEs(int slot, Actor actor)
    {
        var count = _aoes.Count;
        if (count == 0)
            return [];
        var aoes = CollectionsMarshal.AsSpan(_aoes);
        var deadline = aoes[0].Activation.AddSeconds(1d);
        var index = 0;
        while (index < count && aoes[index].Activation < deadline)
            ++index;
        return aoes[..index];
    }

    public override void OnCastStarted(Actor caster, ActorCastInfo spell)
    {
        switch (spell.Action.ID)
        {
            case (uint)AID.DivisiveOverrulingAOE:
            case (uint)AID.DivisiveRulingAOE:
            case (uint)AID.RipplesOfGloom1:
            case (uint)AID.RipplesOfGloom2:
            case (uint)AID.RipplesOfGloomClone1:
            case (uint)AID.RipplesOfGloomClone2:
                Add(_shapeNarrow, caster, spell);
                break;
            case (uint)AID.DivineRuination:
            case (uint)AID.DivineRuinationClone:
                Add(_shapeWide, caster, spell);
                break;
        }
    }

    private void Add(AOEShapeRect shape, Actor caster, ActorCastInfo spell)
    {
        _aoes.Add(new(shape, spell.LocXZ, spell.Rotation, Module.CastFinishAt(spell), actorID: caster.InstanceID));
        _aoes.Sort((a, b) => a.Activation.CompareTo(b.Activation));
    }

    public override void OnCastFinished(Actor caster, ActorCastInfo spell)
    {
        switch (spell.Action.ID)
        {
            case (uint)AID.DivisiveOverrulingAOE:
            case (uint)AID.DivisiveRulingAOE:
            case (uint)AID.RipplesOfGloom1:
            case (uint)AID.RipplesOfGloom2:
            case (uint)AID.RipplesOfGloomClone1:
            case (uint)AID.RipplesOfGloomClone2:
            case (uint)AID.DivineRuination:
            case (uint)AID.DivineRuinationClone:
                if (_aoes.Count != 0)
                    _aoes.RemoveAt(0);
                ++NumCasts;
                break;
        }
    }
}

// 維持判決：王先連線到目標（193＝全隊分攤、192＝範圍死刑），約 6.3 秒後判定。
sealed class UpheldOverruling(BossModule module) : Components.UniformStackSpread(module, 6f, 13f, 8, 8, alwaysShowSpreads: true)
{
    public override void OnTethered(Actor source, ActorTetherInfo tether)
    {
        if (source.OID != (uint)OID.Boss || WorldState.Actors.Find(tether.Target) is not Actor target)
            return;
        switch (tether.ID)
        {
            case (uint)TetherID.UpheldOverrulingLight:
                AddStack(target, WorldState.FutureTime(6.3d));
                break;
            case (uint)TetherID.UpheldOverrulingDark:
                AddSpread(target, WorldState.FutureTime(6.3d));
                break;
        }
    }

    public override void OnEventCast(Actor caster, ActorCastEvent spell)
    {
        switch (spell.Action.ID)
        {
            case (uint)AID.UpheldOverrulingAOELight:
                Stacks.Clear();
                break;
            case (uint)AID.UpheldOverrulingAOEDark:
                Spreads.Clear();
                break;
        }
    }
}

sealed class Lightburst(BossModule module) : Components.SimpleAOEs(module, (uint)AID.Lightburst, 13f);
sealed class DarkPerimeter(BossModule module) : Components.SimpleAOEs(module, (uint)AID.DarkPerimeter, new AOEShapeDonut(8f, 50f));

// 駁回判決：從場中央擊退 11 碼，場地半徑 20、場外即死 ⇒ 判定前待在中央 7.5 碼內（落點離邊緣留 1.5 碼）。
sealed class DismissalOverruling(BossModule module) : Components.SimpleKnockbackGroups(module, [(uint)AID.DismissalOverrulingLightAOE, (uint)AID.DismissalOverrulingDarkAOE], 11f)
{
    private static readonly AOEShapeCircle _safe = new(7.5f, true);

    public override void AddAIHints(int slot, Actor actor, PartyRolesConfig.Assignment assignment, AIHints hints)
    {
        if (Casters.Count == 0)
            return;
        ref readonly var kb = ref Casters.Ref(0);
        if (!IsImmune(slot, kb.Activation))
            hints.AddForbiddenZone(_safe, kb.Origin, default, kb.Activation);
    }
}

// 內光／外暗和擊退同時開始讀條；擊退結算前不畫，否則 AI 會為了躲內光站出擊退安全圈。
sealed class InnerLight(BossModule module) : Components.SimpleAOEs(module, (uint)AID.InnerLight, 13f)
{
    private readonly DismissalOverruling? _kb = module.FindComponent<DismissalOverruling>();
    public override ReadOnlySpan<AOEInstance> ActiveAOEs(int slot, Actor actor) => _kb?.Casters.Count > 0 ? [] : base.ActiveAOEs(slot, actor);
}

sealed class OuterDark(BossModule module) : Components.SimpleAOEs(module, (uint)AID.OuterDark, new AOEShapeDonut(8f, 50f))
{
    private readonly DismissalOverruling? _kb = module.FindComponent<DismissalOverruling>();
    public override ReadOnlySpan<AOEInstance> ActiveAOEs(int slot, Actor actor) => _kb?.Casters.Count > 0 ? [] : base.ActiveAOEs(slot, actor);
}

// 仇恨：分攤之後同一個目標再連打數下，分攤圈留到後續打完（或 2.5 秒沒有下一下）。
sealed class Styx(BossModule module) : Components.UniformStackSpread(module, 6f, default, 8, 8)
{
    private const int FollowUps = 4;
    private int _numFollowUps;
    private DateTime _lastHit;

    public override void OnCastStarted(Actor caster, ActorCastInfo spell)
    {
        if (spell.Action.ID == (uint)AID.Styx && WorldState.Actors.Find(spell.TargetID) is Actor target)
        {
            Stacks.Clear();
            AddStack(target, Module.CastFinishAt(spell));
            _numFollowUps = 0;
            _lastHit = default;
        }
    }

    public override void OnEventCast(Actor caster, ActorCastEvent spell)
    {
        if (spell.Action.ID is not ((uint)AID.Styx or (uint)AID.StyxAOE) || Stacks.Count == 0)
            return;
        if (spell.Action.ID == (uint)AID.StyxAOE && ++_numFollowUps >= FollowUps)
        {
            Stacks.Clear();
            return;
        }
        _lastHit = WorldState.CurrentTime;
        Stacks.Ref(0).Activation = WorldState.FutureTime(1.2d);
    }

    public override void Update()
    {
        base.Update();
        if (Stacks.Count != 0 && _lastHit != default && WorldState.CurrentTime > _lastHit.AddSeconds(2.5d))
            Stacks.Clear();
    }
}

// 光與暗的調停：光／暗標記要站在場地對應的一半，站錯每 3 秒吃一下錯判＋受傷加重；中線附近兩種都算失調。
// 哪一半是光照「拿到調和的人站在哪一邊」學，學到之前假設光在西（重播三輪都是）。
sealed class DarkAndLight(BossModule module) : BossComponent(module)
{
    private const float Margin = 5f;
    private static readonly AOEShapeRect _half = new(30f, 30f);
    private static readonly Angle _east = 90f.Degrees(), _west = -90f.Degrees();
    private bool _lightWest = true;

    public override void OnStatusGain(Actor actor, ActorStatus status)
    {
        var dx = actor.Position.X - Arena.Center.X;
        if (dx is > -2f and < 2f)
            return;
        if (status.ID == (uint)SID.LightHarmony)
            _lightWest = dx < 0f;
        else if (status.ID == (uint)SID.DarkHarmony)
            _lightWest = dx > 0f;
    }

    // 回傳 0＝沒有標記、1＝光、2＝暗
    private static int Mark(Actor actor, out bool harmony)
    {
        harmony = false;
        if (actor.FindStatus((uint)SID.LightHarmony) != null)
        {
            harmony = true;
            return 1;
        }
        if (actor.FindStatus((uint)SID.DarkHarmony) != null)
        {
            harmony = true;
            return 2;
        }
        if (actor.FindStatus((uint)SID.LightDiscord) != null)
            return 1;
        return actor.FindStatus((uint)SID.DarkDiscord) != null ? 2 : 0;
    }

    private bool ForbiddenHalf(Actor actor, out WPos origin, out Angle dir, out int mark, out bool harmony)
    {
        mark = Mark(actor, out harmony);
        if (mark == 0)
        {
            origin = default;
            dir = default;
            return false;
        }
        var goWest = (mark == 1) == _lightWest;
        origin = Arena.Center + new WDir(goWest ? -Margin : Margin, 0f);
        dir = goWest ? _east : _west;
        return true;
    }

    public override void AddHints(int slot, Actor actor, TextHints hints)
    {
        if (ForbiddenHalf(actor, out _, out _, out var mark, out var harmony))
            hints.Add(Loc.T(mark == 1 ? "Stand on the light side!" : "Stand on the dark side!"), !harmony);
    }

    public override void AddAIHints(int slot, Actor actor, PartyRolesConfig.Assignment assignment, AIHints hints)
    {
        if (ForbiddenHalf(actor, out var origin, out var dir, out _, out _))
            hints.AddForbiddenZone(_half, origin, dir, WorldState.FutureTime(3d));
    }

    public override void DrawArenaBackground(int pcSlot, Actor pc)
    {
        if (ForbiddenHalf(pc, out var origin, out var dir, out _, out _))
            _half.Draw(Arena, origin, dir, Colors.AOE);
    }
}

sealed class Vortex(BossModule module) : Components.SimpleAOEs(module, (uint)AID.VortexAOE, 4f);

sealed class P11NThemisStates : StateMachineBuilder
{
    public P11NThemisStates(BossModule module) : base(module)
    {
        TrivialPhase()
            .ActivateOnEnter<Eunomia>()
            .ActivateOnEnter<Dike>()
            .ActivateOnEnter<DivisiveOverruling>()
            .ActivateOnEnter<UpheldOverruling>()
            .ActivateOnEnter<Lightburst>()
            .ActivateOnEnter<DarkPerimeter>()
            .ActivateOnEnter<DismissalOverruling>()
            .ActivateOnEnter<InnerLight>()
            .ActivateOnEnter<OuterDark>()
            .ActivateOnEnter<Styx>()
            .ActivateOnEnter<DarkAndLight>()
            .ActivateOnEnter<Vortex>()
            .ActivateOnEnter<Components.UnclaimedCastAOEs>();
    }
}

[ModuleInfo(BossModuleInfo.Maturity.Contributed, Contributors = "ffxiv-tc-port", GroupType = BossModuleInfo.GroupType.CFC, GroupID = 940, NameID = 12388)]
public sealed class P11NThemis(WorldState ws, Actor primary) : BossModule(ws, primary, new(100f, 100f), new ArenaBoundsCircle(20f));
