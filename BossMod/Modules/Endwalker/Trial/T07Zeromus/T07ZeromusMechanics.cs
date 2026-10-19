namespace BossMod.Endwalker.Trial.T07Zeromus;

// 形狀與原點算法照極版模組（Ex7Zeromus.VisceralWhirl）：AOE2 的原點從施放者往側邊偏半個寬度
class VisceralWhirl(BossModule module) : Components.GenericAOEs(module)
{
    private readonly List<AOEInstance> _aoes = [];

    private static readonly AOEShapeRect _shapeNormal = new(29f, 14f);
    private static readonly AOEShapeRect _shapeOffset = new(60f, 14f);

    public override ReadOnlySpan<AOEInstance> ActiveAOEs(int slot, Actor actor) => CollectionsMarshal.AsSpan(_aoes);

    public override void OnCastStarted(Actor caster, ActorCastInfo spell)
    {
        switch (spell.Action.ID)
        {
            case (uint)AID.VisceralWhirlRAOE1:
            case (uint)AID.VisceralWhirlLAOE1:
                _aoes.Add(new(_shapeNormal, caster.Position, spell.Rotation, Module.CastFinishAt(spell), actorID: caster.InstanceID));
                break;
            case (uint)AID.VisceralWhirlRAOE2:
                _aoes.Add(new(_shapeOffset, caster.Position + _shapeOffset.HalfWidth * spell.Rotation.ToDirection().OrthoL(), spell.Rotation, Module.CastFinishAt(spell), actorID: caster.InstanceID));
                break;
            case (uint)AID.VisceralWhirlLAOE2:
                _aoes.Add(new(_shapeOffset, caster.Position + _shapeOffset.HalfWidth * spell.Rotation.ToDirection().OrthoR(), spell.Rotation, Module.CastFinishAt(spell), actorID: caster.InstanceID));
                break;
        }
    }

    public override void OnCastFinished(Actor caster, ActorCastInfo spell)
    {
        var id = caster.InstanceID;
        for (var i = _aoes.Count - 1; i >= 0; --i)
            if (_aoes[i].ActorID == id)
                _aoes.RemoveAt(i);
    }
}

// 首發有讀條，之後 20 發沒有讀條、每發轉 7 度；掃完或逾時就清掉
class FracturedEventide(BossModule module) : Components.GenericAOEs(module)
{
    private Actor? _source;
    private Angle _startingRotation;
    private Angle _increment;
    private DateTime _startingActivation;

    private static readonly AOEShapeRect _shape = new(60f, 4f);
    private const int _maxCasts = 21;
    private const double _interval = 0.46d;

    public override ReadOnlySpan<AOEInstance> ActiveAOEs(int slot, Actor actor)
    {
        if (_source == null || NumCasts >= _maxCasts)
            return [];

        var aoes = new AOEInstance[_maxCasts - NumCasts];
        var index = 0;
        for (var i = NumCasts + 1; i < _maxCasts; ++i)
            aoes[index++] = new(_shape, _source.Position, _startingRotation + i * _increment, _startingActivation.AddSeconds(_interval * i));
        aoes[index] = new(_shape, _source.Position, _startingRotation + NumCasts * _increment, _startingActivation.AddSeconds(_interval * NumCasts), Colors.Danger);
        return aoes;
    }

    public override void Update()
    {
        if (_source != null && (NumCasts >= _maxCasts || WorldState.CurrentTime > _startingActivation.AddSeconds(_interval * _maxCasts + 3d)))
            _source = null;
    }

    public override void OnCastStarted(Actor caster, ActorCastInfo spell)
    {
        if (spell.Action.ID is (uint)AID.FracturedEventideAOEFirstWE or (uint)AID.FracturedEventideAOEFirstEW)
        {
            _source = caster;
            NumCasts = 0;
            _startingRotation = spell.Rotation;
            _increment = _startingRotation.Rad > 0f ? -7f.Degrees() : 7f.Degrees();
            _startingActivation = Module.CastFinishAt(spell);
        }
    }

    public override void OnEventCast(Actor caster, ActorCastEvent spell)
    {
        if (spell.Action.ID is (uint)AID.FracturedEventideAOEFirstWE or (uint)AID.FracturedEventideAOEFirstEW or (uint)AID.FracturedEventideAOERest)
            ++NumCasts;
    }
}

class AccelerationBomb(BossModule module) : Components.StayMove(module, 3d)
{
    public override void OnStatusGain(Actor actor, ActorStatus status)
    {
        if (status.ID == (uint)SID.AccelerationBomb && Raid.FindSlot(actor.InstanceID) is var slot && slot >= 0)
            PlayerStates[slot] = new(Requirement.Stay, status.ExpireAt);
    }

    public override void OnStatusLose(Actor actor, ActorStatus status)
    {
        if (status.ID == (uint)SID.AccelerationBomb && Raid.FindSlot(actor.InstanceID) is var slot && slot >= 0)
            PlayerStates[slot] = default;
    }
}

// 塔判定後原地留 5 跳持續傷害，直到同一點的 FlareKill 讀完；沒等到 FlareKill 就逾時清掉
class FlareScald(BossModule module) : Components.GenericAOEs(module)
{
    private readonly List<AOEInstance> _aoes = [];

    private static readonly AOEShapeCircle _shape = new(5f);

    public override ReadOnlySpan<AOEInstance> ActiveAOEs(int slot, Actor actor) => CollectionsMarshal.AsSpan(_aoes);

    public override void Update()
    {
        if (_aoes.Count != 0 && WorldState.CurrentTime > _aoes[0].Activation.AddSeconds(8d))
            _aoes.Clear();
    }

    public override void OnEventCast(Actor caster, ActorCastEvent spell)
    {
        switch (spell.Action.ID)
        {
            case (uint)AID.FlareAOE:
                _aoes.Add(new(_shape, caster.Position, default, WorldState.FutureTime(2.1d)));
                break;
            case (uint)AID.FlareScald:
                ++NumCasts;
                break;
            case (uint)AID.FlareKill:
                _aoes.Clear();
                break;
        }
    }
}

// 泡泡往南飄（每秒 3 碼）；形狀照極版模組（Ex7Zeromus.VoidBio）
class VoidBio(BossModule module) : Components.GenericAOEs(module)
{
    private static readonly AOEShapeCapsule _shape = new(2f, 3f);
    private readonly List<AOEInstance> _aoes = [];

    public override ReadOnlySpan<AOEInstance> ActiveAOEs(int slot, Actor actor)
    {
        _aoes.Clear();
        var bubbles = Module.Enemies((uint)OID.ToxicBubble);
        var count = bubbles.Count;
        for (var i = 0; i < count; ++i)
        {
            var b = bubbles[i];
            if (!b.IsDeadOrDestroyed)
                _aoes.Add(new(_shape, b.Position, Angle.AnglesCardinals[1]));
        }
        return CollectionsMarshal.AsSpan(_aoes);
    }
}

class AkhRhaiSpread(BossModule module) : Components.UniformStackSpread(module, default, 5f)
{
    public override void OnEventIcon(Actor actor, uint iconID, ulong targetID)
    {
        if (iconID == (uint)IconID.AkhRhai)
            AddSpread(actor, WorldState.FutureTime(5.2d));
    }

    public override void OnCastStarted(Actor caster, ActorCastInfo spell)
    {
        if (spell.Action.ID == (uint)AID.AkhRhaiStart)
            Spreads.Clear();
    }
}

// 讀條開始時落點就定了，讀完後同一點再連打 10 下；數滿或逾時就清掉
class AkhRhai(BossModule module) : Components.GenericAOEs(module)
{
    private readonly List<AOEInstance> _aoes = [];

    private static readonly AOEShapeCircle _shape = new(5f);

    public override ReadOnlySpan<AOEInstance> ActiveAOEs(int slot, Actor actor) => CollectionsMarshal.AsSpan(_aoes);

    public override void Update()
    {
        if (_aoes.Count != 0 && WorldState.CurrentTime > _aoes[0].Activation.AddSeconds(8d))
        {
            _aoes.Clear();
            NumCasts = 0;
        }
    }

    public override void OnCastStarted(Actor caster, ActorCastInfo spell)
    {
        if (spell.Action.ID == (uint)AID.AkhRhaiStart)
            _aoes.Add(new(_shape, spell.LocXZ, default, Module.CastFinishAt(spell)));
    }

    public override void OnEventCast(Actor caster, ActorCastEvent spell)
    {
        if (spell.Action.ID == (uint)AID.AkhRhaiAOE && ++NumCasts >= _aoes.Count * 10)
        {
            _aoes.Clear();
            NumCasts = 0;
        }
    }
}

// 五道扇形每 0.7 秒依序判定、合起來蓋滿全場；照極版模組一次只標最先的三道，留出可以站的地方
class ChasmicNails(BossModule module) : Components.GenericAOEs(module)
{
    private readonly List<AOEInstance> _aoes = new(5);

    private static readonly AOEShapeCone _shape = new(60f, 20f.Degrees());

    public override ReadOnlySpan<AOEInstance> ActiveAOEs(int slot, Actor actor)
    {
        var count = _aoes.Count;
        if (count == 0)
            return [];
        var max = count > 3 ? 3 : count;
        var aoes = CollectionsMarshal.AsSpan(_aoes)[..max];
        for (var i = 0; i < max; ++i)
            aoes[i].Color = i == 0 && count > 1 ? Colors.Danger : default;
        return aoes;
    }

    public override void OnCastStarted(Actor caster, ActorCastInfo spell)
    {
        if (spell.Action.ID is (uint)AID.ChasmicNailsAOE1 or (uint)AID.ChasmicNailsAOE2 or (uint)AID.ChasmicNailsAOE3 or (uint)AID.ChasmicNailsAOE4 or (uint)AID.ChasmicNailsAOE5)
        {
            _aoes.Add(new(_shape, spell.LocXZ, spell.Rotation, Module.CastFinishAt(spell), actorID: caster.InstanceID));
            _aoes.Sort((x, y) => x.Activation.CompareTo(y.Activation));
        }
    }

    public override void OnCastFinished(Actor caster, ActorCastInfo spell)
    {
        var id = caster.InstanceID;
        for (var i = _aoes.Count - 1; i >= 0; --i)
            if (_aoes[i].ActorID == id)
                _aoes.RemoveAt(i);
    }
}

class DarkMatter(BossModule module) : Components.BaitAwayIcon(module, 8f, (uint)IconID.DarkMatter, (uint)AID.DarkMatterAOE, 4.9d, tankbuster: true, damageType: AIHints.PredictedDamageType.Tankbuster)
{
    private int _hits;

    public override void OnEventIcon(Actor actor, uint iconID, ulong targetID)
    {
        if (iconID == IID)
            _hits = 0;
        base.OnEventIcon(actor, iconID, targetID);
    }

    public override void OnEventCast(Actor caster, ActorCastEvent spell)
    {
        if (spell.Action.ID != WatchedAction)
            return;
        ++NumCasts;
        if (++_hits >= 3 && CurrentBaits.Count != 0)
            CurrentBaits.RemoveAt(0);
    }
}

class Nox : Components.StandardChasingAOEs
{
    public Nox(BossModule module) : base(module, new AOEShapeCircle(10f), (uint)AID.NoxAOEFirst, (uint)AID.NoxAOERest, 2.5f, 1.6f, 3)
    {
        ExcludedTargets = Raid.WithSlot(true, true, true).Mask();
    }

    public override void OnEventIcon(Actor actor, uint iconID, ulong targetID)
    {
        if (iconID == (uint)IconID.Nox)
            ExcludedTargets[Raid.FindSlot(actor.InstanceID)] = false;
    }
}
