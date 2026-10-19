namespace BossMod.Stormblood.Trial.T07Byakko;

class StormPulse(BossModule module) : Components.RaidwideCast(module, (uint)AID.StormPulse);
class HeavenlyStrike(BossModule module) : Components.SingleTargetCast(module, (uint)AID.HeavenlyStrike);
class HeavenlyStrikeSpread(BossModule module) : Components.SpreadFromCastTargets(module, (uint)AID.HeavenlyStrike, 3f);
class SweepTheLeg1(BossModule module) : Components.SimpleAOEs(module, (uint)AID.SweepTheLeg1, new AOEShapeCone(28.5f, 135f.Degrees()));
class SweepTheLeg3(BossModule module) : Components.SimpleAOEs(module, (uint)AID.SweepTheLeg3, new AOEShapeDonut(5f, 30f));
class TheRoarOfThunder(BossModule module) : Components.RaidwideCast(module, (uint)AID.TheRoarOfThunder);
class ImperialGuard(BossModule module) : Components.SimpleAOEs(module, (uint)AID.ImperialGuard, new AOEShapeRect(44.75f, 2.5f));
class FireAndLightning(BossModule module) : Components.SimpleAOEGroups(module, [(uint)AID.FireAndLightning1, (uint)AID.FireAndLightning2], new AOEShapeRect(50f, 10f));

// 白帝的鐵爪斬：落在空中階段的白帝段，玩家正在近身打白帝；沒有這個元件就沒有禁區、不會閃。
// 半徑＝列舉註解的「range 13+R」加白帝（Hakutei1）受擊半徑 4.75，與 Ex6Byakko 的宣告值一致；
// 角度沿用 Ex6 的 60 度（列舉註解是「?-degree」，台服無實測依據）。
// 沿用 Cleave 預設的 activeForUntargetable=false：重播裡鐵爪斬當下白帝都是可鎖定的。
class SteelClaw(BossModule module) : Components.Cleave(module, (uint)AID.SteelClaw, new AOEShapeCone(17.75f, 60f.Degrees()), [(uint)OID.Hakutei1]);

// Inner radius measured from TC replays (hit/miss target lists against distance to the boss): it lies
// in (3.41, 4.54], so 4f - also what Ex6Byakko uses. Upstream's Donut(5f, 3f) has outer < inner and
// never matches anything. Outer 25f is from the enum comment.
class DistantClap(BossModule module) : Components.SimpleAOEs(module, (uint)AID.DistantClap, new AOEShapeDonut(4f, 25f));

// 乾坤一擲: the boss throws the main-aggro player at a spot that then has to be soaked. Icon 62 lands on
// a helper actor at the landing spot, not on a player, so this is a tower (like Ex6Byakko's
// HighestStakes) rather than StackWithIcon. Radius 6f is from the enum comment; 5.9s is the measured
// icon-to-resolution delay. Soaker count is unknown (only solo replays exist), hence 1..8: it marks
// the spot but never tells anyone to leave.
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

// 荒彈, cast-bar variant (10793): a plain SimpleAOEs is enough.
class Aratama(BossModule module) : Components.SimpleAOEs(module, (uint)AID.Aratama1, new AOEShapeCircle(4f));

// 荒彈, the aerial-phase rain: locks onto a player and fires about every 2s. AratamaRainTelegraph
// (10817) lands at the target's position and Aratama2 (10818) detonates that exact spot 6.07s later.
// Pairing is strictly FIFO; nearest-earlier matching is ambiguous whenever the target stands still.
// Static circles on purpose: only the spawn point tracks the player, and the cast events carry no
// resolvable target, so a chasing component would have to guess who is locked.
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

// 百雷繚亂 exaflare, parameters measured from TC replays: 4 helpers cast 10808 at once (a cardinal set,
// then a diagonal set 3s later) and each line advances via 10809 - 4 explosions per line, step 5.0,
// interval 1.06-1.17s. The explosion count must be exact: a line that claims more never reaches
// ExplosionsLeft == 0 and is never removed. spell.Rotation rather than caster.Rotation (actor facing
// can be stale), as in Ex6Byakko.
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

// 空中階段場地會縮小。切換依據是 ArenaFeatures 的 EventObj 動畫事件（0x00040008 縮小／0x00100020 還原），
// 狀態值與 Ex6Byakko 的 Intermission 元件相同。
// 刻意不用「王不可鎖定」當判準：縮小窗只是不可鎖定窗的子區間，階段兩端場地還是大的。
class ArenaChange(BossModule module) : BossComponent(module)
{
    public override void OnActorEAnim(Actor actor, uint state)
    {
        if (actor.OID != (uint)OID.ArenaFeatures)
            return;
        if (state == 0x00040008u)
            Module.Arena.Bounds = T07Byakko.IntermissionBounds;
        else if (state == 0x00100020u)
            Module.Arena.Bounds = T07Byakko.NormalBounds;
    }
}

// NameID only labels the entry in ModuleViewer. 7092 is 白虎 in the TC BNpcName sheet and what Ex6Byakko
// uses; upstream's 6221 is Susano's row.
[ModuleInfo(BossModuleInfo.Maturity.WIP, Contributors = "The Combat Reborn Team", GroupType = BossModuleInfo.GroupType.CFC, GroupID = 290, NameID = 7092)]
public class T07Byakko(WorldState ws, Actor primary) : BossModule(ws, primary, default, NormalBounds)
{
    // 邊界用量測值：上游的 ArenaBoundsCircle(20) 比真實地板大（地面階段 0.5 碼、空中階段 5 碼），
    // AI 以模組宣告的邊界當可走範圍，會把人帶出地板摔死。
    // 台服重播量到玩家中心最遠 19.498（全場地）與 14.990（縮小後），落在這兩個多邊形的可達範圍內，
    // 數值與 Ex6Byakko 的宣告一致。
    public static readonly ArenaBoundsComplex NormalBounds = new([new Polygon(default, 19.5f, 48)]);
    public static readonly ArenaBoundsComplex IntermissionBounds = new([new Polygon(default, 15f, 48)]);
}
