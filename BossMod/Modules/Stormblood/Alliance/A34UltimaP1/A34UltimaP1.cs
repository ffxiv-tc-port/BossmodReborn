namespace BossMod.Stormblood.Alliance.A34UltimaP1;

class HolyIVBait(BossModule module) : Components.SimpleAOEs(module, (uint)AID.HolyIVBait, 6f);
class HolyIVSpread(BossModule module) : Components.SpreadFromCastTargets(module, (uint)AID.HolyIVSpread, 6f);
class AuralightAOE(BossModule module) : Components.SimpleAOEs(module, (uint)AID.AuralightAOE, 20f);
class AuralightRect(BossModule module) : Components.SimpleAOEs(module, (uint)AID.AuralightRect, new AOEShapeRect(70f, 5f));
class GrandCrossAOE(BossModule module) : Components.SimpleAOEs(module, (uint)AID.GrandCrossAOE, new AOEShapeCross(60f, 7.5f));
class Plummet(BossModule module) : Components.SimpleAOEs(module, (uint)AID.Plummet, new AOEShapeRect(15f, 7.5f));
class TimeEruption(BossModule module) : Components.SimpleAOEGroupsByTimewindow(module, [(uint)AID.TimeEruptionAOEFirst, (uint)AID.TimeEruptionAOESecond], new AOEShapeRect(20f, 10f), expectedNumCasters: 9);

class Eruption2(BossModule module) : Components.SimpleAOEs(module, (uint)AID.Eruption2, 8f);
class ControlTower2(BossModule module) : Components.SimpleAOEs(module, (uint)AID.ControlTower2, 6f);

abstract class ExtremeEdge(BossModule module, uint aid) : Components.SimpleAOEs(module, aid, new AOEShapeRect(60f, 18f));
class ExtremeEdge1(BossModule module) : ExtremeEdge(module, (uint)AID.ExtremeEdge1);
class ExtremeEdge2(BossModule module) : ExtremeEdge(module, (uint)AID.ExtremeEdge2);

class CrushWeapon(BossModule module) : Components.SimpleAOEs(module, (uint)AID.CrushWeapon, 6f);
class Searchlight(BossModule module) : Components.SimpleAOEs(module, (uint)AID.Searchlight, 6f);
class HallowedBolt(BossModule module) : Components.SimpleAOEs(module, (uint)AID.HallowedBolt, 6f);

// 錘擊只讀條 0.7 秒，但落點的地上物件在亞靈哈修馬利姆讀大地之錘時就出現（約早 16 秒）⇒ 從物件出現就畫。
// 傷害隨距離衰減：21 碼約九成血、30 碼約兩成 ⇒ 以 30 碼當危險範圍。
sealed class Hammerfall(BossModule module) : Components.GenericAOEs(module)
{
    private static readonly AOEShapeCircle circle = new(30f);
    private readonly List<AOEInstance> _aoes = new(1);

    public override ReadOnlySpan<AOEInstance> ActiveAOEs(int slot, Actor actor) => CollectionsMarshal.AsSpan(_aoes);

    public override void OnActorCreated(Actor actor)
    {
        if (actor.OID == (uint)OID.HammerfallMarker)
            _aoes.Add(new(circle, actor.Position.Quantized(), default, WorldState.FutureTime(16d)));
    }

    public override void OnCastStarted(Actor caster, ActorCastInfo spell)
    {
        if (spell.Action.ID == (uint)AID.Hammerfall)
        {
            var aoes = CollectionsMarshal.AsSpan(_aoes);
            for (var i = 0; i < aoes.Length; ++i)
                aoes[i].Activation = Module.CastFinishAt(spell);
        }
    }

    public override void OnEventCast(Actor caster, ActorCastEvent spell)
    {
        if (spell.Action.ID == (uint)AID.Hammerfall)
            _aoes.Clear();
    }

    public override void Update()
    {
        var limit = WorldState.FutureTime(-2d);
        _aoes.RemoveAll(a => a.Activation < limit);
    }
}

// 激流：聖雲水瓶現身後先在原地連放 4 發 6 圓，之後沿面向每 0.8 秒前進約 4 碼直到場地另一邊 ⇒ 從現身讀條起照路線畫接下來幾發。
sealed class PrevailingCurrent(BossModule module) : Components.GenericAOEs(module)
{
    private const int Shown = 5;
    private const double Interval = 0.8d;
    private static readonly AOEShapeCircle circle = new(6f);
    private sealed class Lane(WPos head, WDir step, DateTime next)
    {
        public WPos Head = head; // 下一發的位置
        public readonly WDir Step = step;
        public DateTime Next = next;
        public int Stationary = 4; // 還要在 Head 原地放幾發（含下一發）
    }
    private readonly List<Lane> _lanes = [];
    private readonly List<AOEInstance> _aoes = [];

    public override ReadOnlySpan<AOEInstance> ActiveAOEs(int slot, Actor actor)
    {
        _aoes.Clear();
        var count = _lanes.Count;
        for (var i = 0; i < count; ++i)
        {
            var l = _lanes[i];
            var pos = l.Head;
            var time = l.Next;
            var stay = l.Stationary;
            for (var k = 0; k < Shown && InSquare(pos); ++k)
            {
                _aoes.Add(new(circle, pos.Quantized(), default, time, k == 0 ? Colors.Danger : default));
                if (--stay <= 0)
                    pos += l.Step;
                time = time.AddSeconds(Interval);
            }
        }
        return CollectionsMarshal.AsSpan(_aoes);
    }

    private static bool InSquare(WPos p) => Math.Abs(p.X - 600f) <= 30.5f && Math.Abs(p.Z + 600f) <= 30.5f;

    public override void OnCastStarted(Actor caster, ActorCastInfo spell)
    {
        if (spell.Action.ID == (uint)AID.Materialize)
            _lanes.Add(new(caster.Position, 4f * spell.Rotation.ToDirection(), WorldState.FutureTime(1.5d)));
    }

    public override void OnEventCast(Actor caster, ActorCastEvent spell)
    {
        if (spell.Action.ID != (uint)AID.PrevailingCurrent)
            return;
        var pos = caster.Position;
        Lane? best = null;
        var bestDistSq = 9f;
        var count = _lanes.Count;
        for (var i = 0; i < count; ++i)
        {
            var d = (_lanes[i].Head - pos).LengthSq();
            if (d < bestDistSq)
            {
                best = _lanes[i];
                bestDistSq = d;
            }
        }
        if (best == null)
            return;
        best.Next = WorldState.FutureTime(Interval);
        if (best.Stationary > 1)
        {
            --best.Stationary;
            best.Head = pos;
        }
        else
        {
            best.Stationary = 0;
            best.Head = pos + best.Step;
            if (!InSquare(best.Head))
                _lanes.Remove(best);
        }
    }

    public override void Update()
    {
        // 該放的那發超過 3 秒沒來＝這條已結束或事件缺漏，撤掉。
        var limit = WorldState.FutureTime(-3d);
        _lanes.RemoveAll(l => l.Next < limit);
    }
}

// 究極幻想讀條期間只有拉姆札的護盾圈內能站（兩份錄影玩家離圈心最遠 6.5 碼），護盾物件收起後恢復方形場地。
sealed class UltimateIllusionArena(BossModule module) : BossComponent(module)
{
    private static readonly WPos barrier = new(600f, -620f);
    private static readonly ArenaBoundsCircle bubble = new(6f);

    public override void OnCastStarted(Actor caster, ActorCastInfo spell)
    {
        if (spell.Action.ID == (uint)AID.UltimateIllusion1)
        {
            Arena.Bounds = bubble;
            Arena.Center = barrier;
        }
    }

    public override void OnActorEAnim(Actor actor, uint state)
    {
        if (state == 0x00040008u && actor.OID == (uint)OID.IllusionBarrier && actor.Position.AlmostEqual(barrier, 1f) && Arena.Bounds == bubble)
        {
            Arena.Bounds = A34UltimaP1.DefaultBounds;
            Arena.Center = A34UltimaP1.ArenaCenter;
        }
    }
}

[ModuleInfo(BossModuleInfo.Maturity.WIP, Contributors = "The Combat Reborn Team", GroupType = BossModuleInfo.GroupType.CFC, GroupID = 636, NameID = 7909)]
public class A34UltimaP1(WorldState ws, Actor primary) : BossModule(ws, primary, ArenaCenter, DefaultBounds)
{
    public static readonly WPos ArenaCenter = new(600f, -600f);
    public static readonly ArenaBoundsSquare DefaultBounds = new(30f);
}
