namespace BossMod.Shadowbringers.Alliance.A13Engels;

class DemolishStructureArenaChange(BossModule module) : Components.GenericAOEs(module)
{
    private static readonly AOEShapeRect square = new(5, 5, 5, InvertForbiddenZone: true);
    private AOEInstance? _aoe;
    public override ReadOnlySpan<AOEInstance> ActiveAOEs(int slot, Actor actor) => Utils.ZeroOrOne(ref _aoe);

    public override void OnCastStarted(Actor caster, ActorCastInfo spell)
    {
        if ((AID)spell.Action.ID == AID.DemolishStructure2 && Arena.Bounds == A13MarxEngels.StartingBounds)
            _aoe = new(square, A13MarxEngels.TransitionSpot, color: Colors.SafeFromAOE);
    }

    public override void OnEventEnvControl(byte index, uint state)
    {
        if (state == 0x00020001 && index == 0x0B)
        {
            Arena.Center = A13MarxEngels.SecondArenaCenter;
            Arena.Bounds = A13MarxEngels.StartingBounds;
            _aoe = null; //
        }
    }
}

// 馬克思打擊／擴散燃燒飛彈：首領先讀 5.7 秒（燃燒飛彈 2.7 秒）的視覺讀條，結束後 Helper 才讀 0.2～1.3 秒的真判定。
// 從視覺讀條開始就照首領位置與面向畫出對應的判定（位置、時差都是相對首領量的）；Helper 讀條開始後改用它的實際位置與時刻。
// 同一個視覺讀條帶兩段判定時，只把先到的一段交給 AI、後一段只預告。
sealed class BossVisualAOEs(BossModule module) : Components.GenericAOEs(module)
{
    private static readonly AOEShapeRect half = new(60f, 15f), front = new(30f, 30f), back = new(35f, 30f), side = new(60f, 10f), saturation = new(30f, 30f);

    private readonly record struct Part(uint AID, AOEShape Shape, WDir Offset, float RotDeg, double Delay);

    private static Part[]? PartsFor(uint visual) => visual switch
    {
        (uint)AID.MarxSmashVisual1 => [new((uint)AID.MarxSmash1, half, new(15f, default), default, 1.9d)],
        (uint)AID.MarxSmashVisual2 => [new((uint)AID.MarxSmash2, half, new(-15f, default), default, 1.9d)],
        (uint)AID.MarxSmashVisual3 => [new((uint)AID.MarxSmash4, front, default, default, 1.85d), new((uint)AID.MarxSmash3, half, default, default, 6.65d)],
        (uint)AID.MarxSmashVisual5 => [new((uint)AID.MarxSmash5, back, new(default, 25f), default, 1.85d),
            new((uint)AID.MarxSmash6, side, new(-15f, 60f), 180f, 6.65d), new((uint)AID.MarxSmash7, side, new(15f, 60f), 180f, 6.65d)],
        (uint)AID.IncendiarySaturationBombing1 => [new((uint)AID.IncendiarySaturationBombing2, saturation, new(default, 30f), default, 6.3d)],
        _ => null
    };

    private static AOEShape? ShapeFor(uint aid) => aid switch
    {
        (uint)AID.MarxSmash1 or (uint)AID.MarxSmash2 or (uint)AID.MarxSmash3 => half,
        (uint)AID.MarxSmash4 => front,
        (uint)AID.MarxSmash5 => back,
        (uint)AID.MarxSmash6 or (uint)AID.MarxSmash7 => side,
        (uint)AID.IncendiarySaturationBombing2 => saturation,
        _ => null
    };

    private readonly List<(uint AID, AOEInstance AOE)> _pending = new(4);
    private readonly List<AOEInstance> _aoes = new(4);

    public override ReadOnlySpan<AOEInstance> ActiveAOEs(int slot, Actor actor) => CollectionsMarshal.AsSpan(_aoes);

    public override void OnCastStarted(Actor caster, ActorCastInfo spell)
    {
        var id = spell.Action.ID;
        if (PartsFor(id) is { } parts)
        {
            var finish = Module.CastFinishAt(spell);
            var origin = caster.Position;
            var rot = caster.Rotation;
            for (var i = 0; i < parts.Length; ++i)
            {
                ref readonly var p = ref parts[i];
                _pending.Add((p.AID, new(p.Shape, origin + p.Offset.Rotate(rot), rot + p.RotDeg.Degrees(), finish.AddSeconds(p.Delay))));
            }
        }
        else if (ShapeFor(id) is { } shape)
        {
            var aoe = new AOEInstance(shape, spell.LocXZ, spell.Rotation, Module.CastFinishAt(spell));
            var idx = _pending.FindIndex(e => e.AID == id);
            if (idx >= 0)
                _pending[idx] = (id, aoe);
            else
                _pending.Add((id, aoe));
        }
    }

    public override void OnEventCast(Actor caster, ActorCastEvent spell)
    {
        var id = spell.Action.ID;
        if (ShapeFor(id) == null)
            return;
        var idx = _pending.FindIndex(e => e.AID == id);
        if (idx >= 0)
            _pending.RemoveAt(idx);
    }

    public override void Update()
    {
        _aoes.Clear();
        var now = WorldState.CurrentTime;
        for (var i = _pending.Count - 1; i >= 0; --i)
            if ((now - _pending[i].AOE.Activation).TotalSeconds > 1.5d)
                _pending.RemoveAt(i); // 判定沒來（首領被打斷、換場）就收回
        var count = _pending.Count;
        if (count == 0)
            return;
        var first = DateTime.MaxValue;
        for (var i = 0; i < count; ++i)
            if (_pending[i].AOE.Activation < first)
                first = _pending[i].AOE.Activation;
        var hasLater = false;
        for (var i = 0; i < count; ++i)
            hasLater |= (_pending[i].AOE.Activation - first).TotalSeconds > 1d;
        for (var i = 0; i < count; ++i)
        {
            var aoe = _pending[i].AOE;
            var imminent = (aoe.Activation - first).TotalSeconds <= 1d;
            aoe.Risky = imminent;
            aoe.Color = imminent && hasLater ? Colors.Danger : default;
            _aoes.Add(aoe);
        }
    }
}
sealed class ArmLaser(BossModule module) : Components.SimpleAOEs(module, (uint)AID.ArmLaser, new AOEShapeCone(30f, 45f.Degrees()));
sealed class MarxThrust(BossModule module) : Components.SimpleAOEs(module, (uint)AID.MarxThrust2, new AOEShapeRect(30f, 10f));

// 廣擴散雷射：首領瞬發 18240 約 2.1 秒後 Helper 放全場的 18241（兩者都沒有讀條）。
sealed class WideAngleDiffuseLaser(BossModule module) : Components.RaidwideInstant(module, (uint)AID.WideAngleDiffuseLaser2, 2.1d)
{
    protected override uint PredictedSourceAction => WatchedAction;

    public override void OnEventCast(Actor caster, ActorCastEvent spell)
    {
        if (spell.Action.ID == (uint)AID.WideAngleDiffuseLaser1)
            Activation = WorldState.FutureTime(Delay);
        else
            base.OnEventCast(caster, spell);
    }
}

class MarxCrush(BossModule module) : Components.SimpleAOEs(module, (uint)AID.MarxCrush, new AOEShapeRect(15, 15));

class PrecisionGuidedMissile2(BossModule module) : Components.SpreadFromCastTargets(module, (uint)AID.PrecisionGuidedMissile2, 6);
class LaserSight1(BossModule module) : Components.SimpleAOEs(module, (uint)AID.LaserSight1, new AOEShapeRect(100, 10));
class GuidedMissile2(BossModule module) : Components.SimpleAOEs(module, (uint)AID.GuidedMissile2, 6);
class IncendiaryBombing2(BossModule module) : Components.SimpleAOEs(module, (uint)AID.IncendiaryBombing2, 8);
class IncendiaryBombing1(BossModule module) : Components.SpreadFromIcon(module, (uint)IconID.Spreadmarker, (uint)AID.IncendiaryBombing1, 8, 5);
class DiffuseLaser(BossModule module) : Components.RaidwideCast(module, (uint)AID.DiffuseLaser);
class SurfaceMissile2(BossModule module) : Components.SimpleAOEs(module, (uint)AID.SurfaceMissile2, 6);

class GuidedMissile(BossModule module) : Components.StandardChasingAOEs(module, new AOEShapeCircle(6), (uint)AID.GuidedMissile2, (uint)AID.GuidedMissile3, 5.5f, 1, 4, true);

[ModuleInfo(BossModuleInfo.Maturity.WIP, Contributors = "The Combat Reborn Team", GroupType = BossModuleInfo.GroupType.CFC, GroupID = 700, NameID = 9147)]
public class A13MarxEngels(WorldState ws, Actor primary) : BossModule(ws, primary, StartingArenaCenter, StartingBounds)
{
    public static readonly WPos TransitionSpot = new(900, 697);
    public static readonly WPos StartingArenaCenter = new(900, 670);
    public static readonly WPos SecondArenaCenter = new(900, 785);
    public static readonly ArenaBoundsSquare StartingBounds = new(30);
}
