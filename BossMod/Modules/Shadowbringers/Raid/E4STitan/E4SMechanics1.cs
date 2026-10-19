namespace BossMod.Shadowbringers.Raid.E4STitan;

// ---- simple raidwides (no positioning requirement beyond "take the damage") ----
class VoiceOfTheLand(BossModule module) : Components.CastCounter(module, (uint)AID.VoiceOfTheLand)
{
    public override void AddAIHints(int slot, Actor actor, PartyRolesConfig.Assignment assignment, AIHints hints)
    {
        if (Module.PrimaryActor.CastInfo != null)
            hints.AddPredictedDamage(Raid.WithSlot().Mask(), Module.CastFinishAt(Module.PrimaryActor.CastInfo));
    }
}

class Tumult(BossModule module) : Components.CastCounter(module, (uint)AID.Tumult)
{
    private DateTime _nextExpected;
    public override void OnCastStarted(Actor caster, ActorCastInfo spell)
    {
        if (spell.Action.ID == WatchedAction)
            _nextExpected = Module.CastFinishAt(spell);
    }
    public override void AddAIHints(int slot, Actor actor, PartyRolesConfig.Assignment assignment, AIHints hints)
    {
        if (_nextExpected > WorldState.CurrentTime)
            hints.AddPredictedDamage(Raid.WithSlot().Mask(), _nextExpected);
    }
}

class EarthenFury(BossModule module) : Components.CastCounter(module, (uint)AID.EarthenFury)
{
    public override void AddAIHints(int slot, Actor actor, PartyRolesConfig.Assignment assignment, AIHints hints)
    {
        var c = ((E4STitan)Module).BossMaximum()?.CastInfo;
        if (c != null && (AID)c.Action.ID is AID.EarthenFury or AID.EarthenFuryBleed or AID.EarthenFuryEnrage)
            hints.AddPredictedDamage(Raid.WithSlot().Mask(), Module.CastFinishAt(c));
    }
}

class TectonicUplift(BossModule module) : Components.CastCounter(module, (uint)AID.TectonicUplift)
{
    public override void AddAIHints(int slot, Actor actor, PartyRolesConfig.Assignment assignment, AIHints hints)
    {
        var c = ((E4STitan)Module).BossMaximum()?.CastInfo;
        if (c != null && (AID)c.Action.ID == AID.TectonicUplift)
            hints.AddPredictedDamage(Raid.WithSlot().Mask(), Module.CastFinishAt(c));
    }
}

class SeismicWave(BossModule module) : Components.CastCounter(module, (uint)AID.SeismicWave)
{
    public override void AddAIHints(int slot, Actor actor, PartyRolesConfig.Assignment assignment, AIHints hints)
    {
        if (Module.PrimaryActor.CastInfo != null)
            hints.AddPredictedDamage(Raid.WithSlot().Mask(), Module.CastFinishAt(Module.PrimaryActor.CastInfo));
    }
}

// ---- headmarker spread / stack (icon-driven, resolved by the follow-up cast) ----
// radii are best-effort defaults (typical for this era of savage content) - tune if they feel wrong in practice
class PulseOfTheLand(BossModule module) : Components.SpreadFromIcon(module, (uint)IconID.PulseOfTheLandSpread, (uint)AID.PulseOfTheLand, 6, 5);
class ForceOfTheLand(BossModule module) : Components.StackWithIcon(module, (uint)IconID.ForceOfTheLandStack, (uint)AID.ForceOfTheLand, 6, 5);

// ---- Evil Earth: ground pattern markers, cactbot itself only tells players to "look for the marker"
// (no fixed safe zone data) - surfaced as a plain warning so at least the AI/player knows to be alert ----
class EvilEarth(BossModule module) : Components.CastCounter(module, (uint)AID.EvilEarth)
{
    public override void AddHints(int slot, Actor actor, TextHints hints)
    {
        if (NumCasts > 0 && Module.PrimaryActor.CastInfo != null && WorldState.CurrentTime < Module.CastFinishAt(Module.PrimaryActor.CastInfo).AddSeconds(3))
            hints.Add(Loc.T("Watch for Evil Earth ground markers!"), false);
    }
}

// ---- Geocrush: raidwide + knockback. The 15y push distance is an estimate. The knockback resolves
// from the spot the boss jumps to mid-cast, not from where it stood when the cast started; the cast
// info carries that destination up front (LocXZ), so the origin is known for the whole cast. Falls
// back to the caster's position if LocXZ is zero. The AI is kept within 2y of the origin so the push
// cannot carry it off the platform. ----
class Geocrush(BossModule module) : Components.GenericKnockback(module, (uint)AID.Geocrush)
{
    private WPos _origin;
    private DateTime _resolveAt;
    public override ReadOnlySpan<Knockback> ActiveKnockbacks(int slot, Actor actor)
        => _resolveAt > WorldState.CurrentTime ? new Knockback[] { new(_origin, 15, _resolveAt) } : [];

    public override void AddAIHints(int slot, Actor actor, PartyRolesConfig.Assignment assignment, AIHints hints)
    {
        hints.AddPredictedDamage(Raid.WithSlot().Mask(), _resolveAt);
        if (_resolveAt > WorldState.CurrentTime)
        {
            // stay within 2y of the origin: the 15y push distance and the 20y arena half-width are both
            // estimates, so keep as much margin as possible
            hints.AddForbiddenZone(ShapeDistance.InvertedCircle(_origin, 2), _resolveAt);
        }
    }
    public override void OnCastStarted(Actor caster, ActorCastInfo spell)
    {
        if (spell.Action.ID == WatchedAction)
        {
            _origin = spell.LocXZ != default ? spell.LocXZ : caster.Position;
            _resolveAt = Module.CastFinishAt(spell);
            // log only (never chat): records whether spell.LocXZ carried a real location or we fell back to caster.Position
            Service.Log($"[E4S debug] action {spell.Action.ID:X} origin captured: {_origin} (from {(spell.LocXZ != default ? "spell.LocXZ" : "caster.Position fallback")}), caster currently at {caster.Position}, resolves in {(_resolveAt - WorldState.CurrentTime).TotalSeconds:F1}s");
        }
    }
}

// ---- Dual Earthen Fists: raidwide + knockback, same treatment as Geocrush (push distance estimated,
// origin taken from the cast's LocXZ the same way). ----
class DualEarthenFists(BossModule module) : Components.GenericKnockback(module, (uint)AID.DualEarthenFists)
{
    private WPos _origin;
    private DateTime _resolveAt;
    public override ReadOnlySpan<Knockback> ActiveKnockbacks(int slot, Actor actor)
        => _resolveAt > WorldState.CurrentTime ? new Knockback[] { new(_origin, 15, _resolveAt) } : [];

    public override void AddAIHints(int slot, Actor actor, PartyRolesConfig.Assignment assignment, AIHints hints)
    {
        hints.AddPredictedDamage(Raid.WithSlot().Mask(), _resolveAt);
        if (_resolveAt > WorldState.CurrentTime)
            hints.AddForbiddenZone(ShapeDistance.InvertedCircle(_origin, 2), _resolveAt);
    }
    public override void OnCastStarted(Actor caster, ActorCastInfo spell)
    {
        if (spell.Action.ID == WatchedAction)
        {
            _origin = spell.LocXZ != default ? spell.LocXZ : caster.Position;
            _resolveAt = Module.CastFinishAt(spell);
            // log only (never chat): records whether spell.LocXZ carried a real location or we fell back to caster.Position
            Service.Log($"[E4S debug] action {spell.Action.ID:X} origin captured: {_origin} (from {(spell.LocXZ != default ? "spell.LocXZ" : "caster.Position fallback")}), caster currently at {caster.Position}, resolves in {(_resolveAt - WorldState.CurrentTime).TotalSeconds:F1}s");
        }
    }
}

// ---- Magnitude 5.0: "get under the boss" - donut safe zone hugging the boss hitbox, raidwide outside it.
// Radius numbers are estimated (typical for this style of mechanic); verify in practice. ----
class MagnitudeFive(BossModule module) : Components.CastCounter(module, (uint)AID.MagnitudeFive)
{
    private const float _outerRadius = 15;
    private const float _innerRadius = 4;

    // every hint is gated on the boss actually casting Magnitude 5.0; the component itself stays active for the whole phase
    private ActorCastInfo? ActiveCast() => Module.PrimaryActor.CastInfo is { } c && c.Action.ID == WatchedAction ? c : null;

    public override void AddHints(int slot, Actor actor, TextHints hints)
    {
        if (ActiveCast() is null)
            return;
        if (!actor.Position.InCircle(Module.PrimaryActor.Position, _outerRadius))
            hints.Add(Loc.T("Move closer to the boss!"));
        else if (!actor.Position.InCircle(Module.PrimaryActor.Position, _innerRadius))
            hints.Add(Loc.T("Move closer to the boss!"), false);
    }

    public override void AddAIHints(int slot, Actor actor, PartyRolesConfig.Assignment assignment, AIHints hints)
    {
        if (ActiveCast() is { } cast)
        {
            hints.AddForbiddenZone(ShapeDistance.InvertedCircle(Module.PrimaryActor.Position, _innerRadius), Module.CastFinishAt(cast));
            hints.AddPredictedDamage(Raid.WithSlot().Mask(), Module.CastFinishAt(cast));
        }
    }

    public override void DrawArenaBackground(int pcSlot, Actor pc)
    {
        if (ActiveCast() is not null)
        {
            Arena.ZoneCircle(Module.PrimaryActor.Position, _outerRadius, Colors.AOE);
            Arena.ZoneCircle(Module.PrimaryActor.Position, _innerRadius, Colors.SafeFromAOE);
        }
    }
}
