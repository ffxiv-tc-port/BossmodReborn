namespace BossMod.RealmReborn.Dungeon.D10StoneVigil.D102Koshchei;

public enum OID : uint
{
    Boss = 0x38C7, // R2.88
    MaelstromVisual = 0x38C8, // R0.8
    MaelstromHelper = 0x38D0
}

public enum AID : uint
{
    AutoAttack = 870, // Boss->player, no cast, single-target

    SpikedTail = 28732, // Boss->player, 5.0s cast, single-target, tankbuster
    SonicStorm = 29053, // Boss->location, 3.0s cast, range 6 circle
    Typhoon = 28730, // Boss->self, 3.0s cast, single-target, visual
    TyphoonAOE = 28731 // MaelstromHelper->self, no cast, range 3 circle
}

class SpikedTail(BossModule module) : Components.SingleTargetCast(module, (uint)AID.SpikedTail);
class SonicStorm(BossModule module) : Components.SimpleAOEs(module, (uint)AID.SonicStorm, 6f);

// 每道龍捲從 X=57 動身後共炸 17 次：每次往西 1.745 碼炸到 30.8，最後兩次停在西牆 X=30.4。
// 旋風本體在最後一發之前就倒下，所以線不跟著本體收，炸滿 17 次才收。
class Typhoon(BossModule module) : Components.Exaflare(module, 3f)
{
    private const float WestWallX = 30.4f;
    private const int MovingExplosions = 17;
    private readonly List<Actor> _maelstroms = module.Enemies((uint)OID.MaelstromVisual);
    private readonly Dictionary<ulong, Line> _lineOf = [];

    public override void Update()
    {
        var count = _maelstroms.Count;
        if (count == 0)
            return;
        for (var i = 0; i < count; ++i)
        {
            var m = _maelstroms[i];
            if (!_lineOf.TryGetValue(m.InstanceID, out var line))
            {
                if (!m.IsDead)
                {
                    line = new(m.Position, new(-1.745f, default), default, 0.6d, 4, 4);
                    Lines.Add(line);
                    _lineOf[m.InstanceID] = line;
                }
            }
            // 還沒動身就倒下（首領先倒），或倒下後超過一秒沒再炸：收掉，不留殘影
            else if (m.IsDead && (line.MaxShownExplosions <= 4 || WorldState.CurrentTime > line.NextExplosion.AddSeconds(1d)))
                Lines.Remove(line);
        }
    }

    public override void OnEventCast(Actor caster, ActorCastEvent spell)
    {
        if (spell.Action.ID == (uint)AID.TyphoonAOE && caster.Position.X < 56f)
        {
            var line = FindLine(caster.Position.Z);
            if (line == null)
            {
                ReportError($"Failed to find entry for {caster.InstanceID:X} @ {caster.Position}");
                return;
            }

            if (line.MaxShownExplosions <= 4)
            {
                // first move
                line.MaxShownExplosions = 10;
                line.ExplosionsLeft = MovingExplosions;
            }
            AdvanceLine(line, caster.Position);
            if (line.Next.X < WestWallX)
                line.Next = new(WestWallX, line.Next.Z);
            if (line.ExplosionsLeft <= 0)
                Lines.Remove(line);
        }
    }

    private Line? FindLine(float z) => Lines.Find(l => Math.Abs(l.Next.Z - z) < 1f);
}

class D102KoshcheiStates : StateMachineBuilder
{
    public D102KoshcheiStates(BossModule module) : base(module)
    {
        TrivialPhase()
            .ActivateOnEnter<SpikedTail>()
            .ActivateOnEnter<SonicStorm>()
            .ActivateOnEnter<Typhoon>();
    }
}

[ModuleInfo(BossModuleInfo.Maturity.Verified, Contributors = "Malediktus", GroupType = BossModuleInfo.GroupType.CFC, GroupID = 11, NameID = 1678)]
public class D102Koshchei(WorldState ws, Actor primary) : BossModule(ws, primary, arena.Center, arena)
{
    private static readonly Shape[] union = [new Rectangle(new(44f, -80f), 13.5f, 10.5f), new Rectangle(new(30.1f, -80), 0.4f, 4.5f),
    new Square(new(30.4f, -75.4f), 0.2f), new Square(new(30.4f, -84.6f), 0.2f)];
    public static readonly ArenaBoundsComplex arena = new(union);
}
