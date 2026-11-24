namespace BossMod.Stormblood.Alliance.A33ThunderGod;

// 同一點的圓與環各自讀條、先後不固定，讀條也可能被中斷 ⇒ 每一發跟著自己的讀條出現與消失，同一點只顯示先到的那一發。
class HallowedBolt(BossModule module) : Components.GenericAOEs(module)
{
    private static readonly AOEShapeCircle _circle = new(15f);
    private static readonly AOEShapeDonut _donut = new(15f, 30f);
    private readonly List<(ulong Caster, AOEInstance AOE)> _casts = [];
    private readonly List<AOEInstance> _shown = [];

    public override ReadOnlySpan<AOEInstance> ActiveAOEs(int slot, Actor actor) => CollectionsMarshal.AsSpan(_shown);

    public override void OnCastStarted(Actor caster, ActorCastInfo spell)
    {
        AOEShape? shape = spell.Action.ID switch
        {
            (uint)AID.HallowedBolt1 => _circle,
            (uint)AID.HallowedBolt2 => _donut,
            _ => null
        };
        if (shape != null)
        {
            _casts.Add((caster.InstanceID, new(shape, spell.LocXZ, default, Module.CastFinishAt(spell))));
            Refresh();
        }
    }

    public override void OnCastFinished(Actor caster, ActorCastInfo spell)
    {
        if (spell.Action.ID is (uint)AID.HallowedBolt1 or (uint)AID.HallowedBolt2)
        {
            var id = caster.InstanceID;
            _casts.RemoveAll(c => c.Caster == id);
            Refresh();
        }
    }

    private void Refresh()
    {
        _shown.Clear();
        var count = _casts.Count;
        for (var i = 0; i < count; ++i)
        {
            var aoe = _casts[i].AOE;
            var first = true;
            for (var j = 0; j < count; ++j)
            {
                var other = _casts[j].AOE;
                if (j != i && other.Origin.AlmostEqual(aoe.Origin, 1f) && (other.Activation < aoe.Activation || other.Activation == aoe.Activation && j < i))
                {
                    first = false;
                    break;
                }
            }
            if (first)
                _shown.Add(aoe);
        }
    }
}

sealed class CrushWeapon(BossModule module) : Components.SimpleAOEs(module, (uint)AID.CrushWeapon2, 6f);
sealed class Duskblade(BossModule module) : Components.RaidwideCast(module, (uint)AID.Duskblade);

[ModuleInfo(BossModuleInfo.Maturity.WIP, Contributors = "The Combat Reborn Team", GroupType = BossModuleInfo.GroupType.CFC, GroupID = 636, NameID = 7899)] //7917
public class A33ThunderGod(WorldState ws, Actor primary) : BossModule(ws, primary, new(-600, -600), arena)
{
    private static readonly ArenaBoundsComplex arena = new([new Circle(new(-612.5f, -578.4f), 10), new Circle(new(-587.5f, -578.4f), 10), new Circle(new(-575, -600), 10), new Circle(new(-587.5f, -621.5f), 10), new Circle(new(-612.5f, -621.5f), 10), new Circle(new(-625, -600), 10), new Donut(new(-600, -600), 20, 27)]);

    protected override void DrawEnemies(int pcSlot, Actor pc)
    {
        Arena.Actor(PrimaryActor);
        Arena.Actors(Enemies((uint)OID.EphemeralKnight));
    }
}
