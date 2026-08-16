namespace BossMod.Stormblood.Trial.T07Byakko;

class StormPulse(BossModule module) : Components.RaidwideCast(module, (uint)AID.StormPulse);
class HeavenlyStrike(BossModule module) : Components.SingleTargetCast(module, (uint)AID.HeavenlyStrike);
class HeavenlyStrikeSpread(BossModule module) : Components.SpreadFromCastTargets(module, (uint)AID.HeavenlyStrike, 3f);
class SweepTheLeg1(BossModule module) : Components.SimpleAOEs(module, (uint)AID.SweepTheLeg1, new AOEShapeCone(28.5f, 135f.Degrees()));
class SweepTheLeg3(BossModule module) : Components.SimpleAOEs(module, (uint)AID.SweepTheLeg3, new AOEShapeDonut(5f, 30f));
class TheRoarOfThunder(BossModule module) : Components.RaidwideCast(module, (uint)AID.TheRoarOfThunder);
class ImperialGuard(BossModule module) : Components.SimpleAOEs(module, (uint)AID.ImperialGuard, new AOEShapeRect(44.75f, 2.5f));
class FireAndLightning(BossModule module) : Components.SimpleAOEGroups(module, [(uint)AID.FireAndLightning1, (uint)AID.FireAndLightning2], new AOEShapeRect(50f, 10f));

// Upstream had AOEShapeDonut(5f, 3f): outer smaller than inner. AOEShapeDonut.Check() forwards to
// InDonut(inner, outer), which can never be true when outer < inner, so the component was inert.
// Both radii are now measured from TC replays rather than guessed. Method: a CST! target list only
// contains actors the ability actually affected, so player-in-list vs not, against the player's distance
// from the boss, brackets the boundary. (Damage VALUES are useless here - the player ran a shield, see
// the notes in tools/bmr-replay.) Calibrated on SweepTheLeg3, whose declared inner radius is 5f: measured
// boundary fell in (5.71, 5.74], i.e. the method reproduces a known value with the expected sign.
// For DistantClap over 11 replays: closest HIT 4.54, furthest MISS below it 3.41 -> inner radius is in
// (3.41, 4.54]. The old 5f therefore promised a safe hole LARGER than reality, which matches the live
// report of being hit while standing where the module said it was safe. 4f is inside the measured
// bracket and is also what Ex6Byakko uses for the same mechanic. Outer 25f from the enum comment.
class DistantClap(BossModule module) : Components.SimpleAOEs(module, (uint)AID.DistantClap, new AOEShapeDonut(4f, 25f));

// 乾坤一擲: the boss throws the main-aggro player at a spot, and that spot needs to be shared.
// Upstream modelled this with StackWithIcon, which is for icons that land on PLAYERS. In the replays
// icon 62 lands on a HELPER actor (OID 0x18D6) positioned at the landing spot, which is why the marker
// was "not being removed properly" - it was the wrong component shape, not a bug in the removal.
// Rebuilt on GenericTowers following Ex6Byakko's HighestStakes, which already handles exactly this.
// Radius 6f from the enum comment for 10806; activation 5.9s measured from icon to resolution
// (37.76->43.66 and 479.95->485.87 in _WAR100_Lo_U_2026_08_16_22_45_12).
// Soaker counts: the replays are all SOLO, so the required number of soakers is NOT determinable from
// them. Ex6 uses 3/3, but this is the normal trial (lower damage, no instakill per the live report), so
// rather than copy an unverified number we use 1..8 - it still says "someone stand here" but can never
// wrongly tell a player to leave. Tighten once a full-party replay exists.
class HighestStakes(BossModule module) : Components.GenericTowers(module, (uint)AID.HighestStakes2)
{
    public override void OnEventIcon(Actor actor, uint iconID, ulong targetID)
    {
        if (iconID == (uint)IconID.Stackmarker)
            Towers.Add(new(actor.Position.Quantized(), 6f, 1, 8, default, WorldState.FutureTime(5.9d)));
    }

    public override void OnEventCast(Actor caster, ActorCastEvent spell)
    {
        if (spell.Action.ID == WatchedAction)
        {
            ++NumCasts;
            Towers.Clear();
        }
    }
}

// 荒彈, cast-bar variant: 10793 has a real 2.2s cast so a plain SimpleAOEs is enough.
class Aratama(BossModule module) : Components.SimpleAOEs(module, (uint)AID.Aratama1, new AOEShapeCircle(4f));

// 荒彈, the aerial-phase rain - the mechanic the live report was about.
// Behaviour (per the player): it locks onto someone and fires once every 2s.
// Structure measured across 11 TC replays / 117 telegraph-detonation pairs:
//   AratamaRainTelegraph (10817) lands at the target's current position, and Aratama2 (10818)
//   detonates that exact spot 6.07s later (observed 5.99-6.15); cadence 1.90-2.09s.
// The pairing is strictly FIFO. Matching each detonation to the NEAREST earlier telegraph instead
// produces a false ambiguity (candidate delays split between ~0.1s and ~2.0s), because the same
// point repeats every 2s whenever the locked player stands still - that artefact is what made this
// look undrawable on the first pass. FIFO pairing matched 117/117 with ZERO location mismatches.
// Radius 2f: players in a detonation's hit list sit 0.01-1.66 from its centre, matching the enum's
// "range 2 circle".
// Modelled as static circles rather than a chasing AOE on purpose: only the SPAWN point tracks the
// player; once spawned the puddle does not move, so there is nothing left to predict. It also avoids
// guessing who is locked - these cast events carry a sentinel MainTargetID that resolves to no actor,
// so a chasing component would have to infer the target from proximity instead of knowing it.
class AratamaRain(BossModule module) : Components.GenericAOEs(module, default, "Keep moving!")
{
    private static readonly AOEShapeCircle circle = new(2f);
    private readonly List<AOEInstance> _aoes = [];

    public override ReadOnlySpan<AOEInstance> ActiveAOEs(int slot, Actor actor) => CollectionsMarshal.AsSpan(_aoes);

    public override void OnEventCast(Actor caster, ActorCastEvent spell)
    {
        if (spell.Action.ID == (uint)AID.AratamaRainTelegraph)
            _aoes.Add(new(circle, spell.TargetXZ.Quantized(), default, WorldState.FutureTime(6.07d)));
        else if (spell.Action.ID == (uint)AID.Aratama2 && _aoes.Count != 0)
            _aoes.RemoveAt(0); // FIFO, matching the measured pairing
    }
}

class AratamaForce(BossModule module) : Components.Voidzone(module, 2f, GetVoidzones, 2)
{
    private static Actor[] GetVoidzones(BossModule module)
    {
        var enemies = module.Enemies((uint)OID.AratamaForce);
        var count = enemies.Count;
        if (count == 0)
            return [];

        var voidzones = new Actor[count];
        var index = 0;
        for (var i = 0; i < count; ++i)
        {
            var z = enemies[i];
            if (!z.IsDead)
                voidzones[index++] = z;
        }
        return voidzones[..index];
    }
}

// 百雷繚亂 exaflare. Every parameter below is measured from the TC replays rather than guessed:
// 4 helpers cast 10808 simultaneously (cardinal set at radius 5, then a diagonal set 3s later), each line
// then advances outward via 10809. Per volley each line explodes 4 times, the step is exactly 5.0 units,
// and the interval is 1.06-1.17s. Upstream's disabled version said 1d and 10 explosions - both wrong;
// a line that claims 10 explosions never reaches ExplosionsLeft == 0 within a volley, so stale lines
// accumulate instead of being removed, which is the likely cause of the "just not appearing" note.
// Also switched caster.Rotation -> spell.Rotation to match Ex6Byakko; in these replays the two agree
// exactly, so this is for robustness (actor facing can be stale), not a behaviour change.
class HundredfoldHavoc(BossModule module) : Components.Exaflare(module, 5f)
{
    public override void OnCastStarted(Actor caster, ActorCastInfo spell)
    {
        if (spell.Action.ID == (uint)AID.HundredfoldHavocFirst)
        {
            Lines.Add(new(caster.Position, 5f * spell.Rotation.ToDirection(), Module.CastFinishAt(spell), 1.1d, 4, 2));
        }
    }

    public override void OnEventCast(Actor caster, ActorCastEvent spell)
    {
        if (spell.Action.ID is (uint)AID.HundredfoldHavocFirst or (uint)AID.HundredfoldHavocRest)
        {
            var count = Lines.Count;
            var pos = caster.Position;
            for (var i = 0; i < count; ++i)
            {
                var line = Lines[i];
                if (line.Next.AlmostEqual(pos, 1f))
                {
                    AdvanceLine(line, pos);
                    if (line.ExplosionsLeft == 0)
                        Lines.RemoveAt(i);
                    return;
                }
            }
        }
    }
}

// NameID was 6221, which is Susano's BNpcName row (T01Susano declares the same value); it is only used by
// ModuleViewer to label the entry, so the module list showed this trial under the wrong boss name.
// 7092 is verified three ways: BNpcName row 7092 is 白虎 while 6221 is 須佐之男 in the TC sheet, the live
// TC replays report nameId 7092 on the primary actor (OID 0x20F7), and Ex6Byakko already uses 7092.
[ModuleInfo(BossModuleInfo.Maturity.WIP, Contributors = "The Combat Reborn Team", GroupType = BossModuleInfo.GroupType.CFC, GroupID = 290, NameID = 7092)]
public class T07Byakko(WorldState ws, Actor primary) : BossModule(ws, primary, default, new ArenaBoundsCircle(20f));
