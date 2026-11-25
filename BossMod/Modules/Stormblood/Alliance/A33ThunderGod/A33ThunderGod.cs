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

// 雷神式聖劍技：只有首領本體讀條（單體），判定在讀條結束後由 Helper 無讀條放出 ⇒ 依讀條 AID 從讀條開始就畫。
//   14153／14154：隔一座的三座平台各一發 15 圓（兩種 AID 各對應固定的三座，不看首領面向）；
//   14159：中央 26 圓；14160：中央 20～40 環；
//   14156／14157：從首領面向兩側的平台起、每 3 秒一座共三座的 16 圓（14156 往角度增加方向、14157 往減少方向）。
sealed class HolySword(BossModule module) : Components.GenericAOEs(module)
{
    private static readonly WPos center = new(-600f, -600f);
    private static readonly AOEShapeCircle bolt = new(15f), ruination = new(16f), northswain = new(26f);
    private static readonly AOEShapeDonut judgment = new(20f, 40f);
    private readonly List<(uint AID, AOEInstance AOE)> _aoes = new(3);
    private AOEInstance[] _shown = [];

    private static WPos Platform(float deg) => center + 25f * deg.Degrees().ToDirection();

    public override ReadOnlySpan<AOEInstance> ActiveAOEs(int slot, Actor actor)
    {
        var count = _aoes.Count;
        if (_shown.Length != count)
            _shown = new AOEInstance[count];
        var earliest = DateTime.MaxValue;
        for (var i = 0; i < count; ++i)
            if (_aoes[i].AOE.Activation < earliest)
                earliest = _aoes[i].AOE.Activation;
        for (var i = 0; i < count; ++i)
        {
            var aoe = _aoes[i].AOE;
            // 依序落下的那串只把最近一發畫成危險色；後面的仍照實際生效時間交給 AI。
            aoe.Color = aoe.Activation <= earliest.AddSeconds(1d) ? Colors.Danger : default;
            _shown[i] = aoe;
        }
        return _shown;
    }

    private void Add(uint aid, AOEShape shape, WPos origin, DateTime activation) => _aoes.Add((aid, new(shape, origin, default, activation)));

    public override void OnCastStarted(Actor caster, ActorCastInfo spell)
    {
        switch (spell.Action.ID)
        {
            case (uint)AID.TGHolySword1:
            case (uint)AID.TGHolySword2:
                var sign = spell.Action.ID == (uint)AID.TGHolySword1 ? 1f : -1f;
                Add((uint)AID.HallowedBoltAOE2, bolt, Platform(30f * sign), Module.CastFinishAt(spell, 1d));
                Add((uint)AID.HallowedBoltAOE2, bolt, Platform(-90f * sign), Module.CastFinishAt(spell, 1.65d));
                Add((uint)AID.HallowedBoltAOE2, bolt, Platform(150f * sign), Module.CastFinishAt(spell, 2d));
                break;
            case (uint)AID.TGHolySword5:
                Add((uint)AID.NorthswainsStrike, northswain, center, Module.CastFinishAt(spell, 0.1d));
                break;
            case (uint)AID.TGHolySword6:
                Add((uint)AID.JudgmentBlade, judgment, center, Module.CastFinishAt(spell, 0.3d));
                break;
            case (uint)AID.TGHolySword3:
            case (uint)AID.TGHolySword4:
                var step = spell.Action.ID == (uint)AID.TGHolySword3 ? 60f : -60f;
                var start = spell.Rotation.Deg - step * 0.5f;
                for (var i = 0; i < 3; ++i)
                    Add((uint)AID.DivineRuination1, ruination, Platform(start + step * i), Module.CastFinishAt(spell, 0.9d + 3.1d * i));
                break;
        }
    }

    public override void OnEventCast(Actor caster, ActorCastEvent spell)
    {
        var id = spell.Action.ID;
        if (id is not ((uint)AID.HallowedBoltAOE2 or (uint)AID.NorthswainsStrike or (uint)AID.JudgmentBlade or (uint)AID.DivineRuination1))
            return;
        var pos = caster.Position;
        var best = -1;
        var bestDistSq = 9f;
        var count = _aoes.Count;
        for (var i = 0; i < count; ++i)
        {
            var (aid, aoe) = _aoes[i];
            var d = (aoe.Origin - pos).LengthSq();
            if (aid == id && d < bestDistSq)
            {
                best = i;
                bestDistSq = d;
            }
        }
        if (best >= 0)
            _aoes.RemoveAt(best);
        else if (id == (uint)AID.DivineRuination1)
            _aoes.RemoveAll(a => a.AID == id); // 落點對不上推算＝規則不成立，整串撤掉，不留錯的危險區
    }

    public override void Update()
    {
        // 判定事件沒來（讀條被打斷等）時不讓範圍一直留著。
        var limit = WorldState.FutureTime(-2d);
        _aoes.RemoveAll(a => a.AOE.Activation < limit);
    }
}

// 劍鬥技場（瞬發）把場地換成一整片圓形競技場（玩家約 1 秒後被抬上去）；平衡崩壞第二段判定後回到花形平台。
sealed class ColosseumArena(BossModule module) : BossComponent(module)
{
    private static readonly ArenaBoundsCircle colosseum = new(34.5f);

    public override void OnEventCast(Actor caster, ActorCastEvent spell)
    {
        if (spell.Action.ID == (uint)AID.Colosseum)
            Arena.Bounds = colosseum;
        else if (spell.Action.ID == (uint)AID.BalanceAsunder2)
            Arena.Bounds = A33ThunderGod.DefaultArena;
    }
}

// 平衡崩壞：首領瞬發第一段，約 10 秒後 Helper 放全場傷害。
sealed class BalanceAsunder(BossModule module) : Components.RaidwideInstant(module, (uint)AID.BalanceAsunder2, 10.1d)
{
    public override void OnEventCast(Actor caster, ActorCastEvent spell)
    {
        if (spell.Action.ID == (uint)AID.BalanceAsunder1)
            Activation = WorldState.FutureTime(Delay);
        base.OnEventCast(caster, spell);
    }
}

// 冥界恐叫打：先在目標腳下讀條一發 6 圓，之後無讀條追同一人再打三發（每發約 5 碼、0.8 秒）。
// 追誰以頭上標記（197）為準：只在被點名的人裡挑離讀條點最近的，不會被剛好站旁邊的隊友搶走。
sealed class CrushWeapon(BossModule module) : Components.StandardChasingAOEs(module, 6f, (uint)AID.CrushWeapon2, (uint)AID.CrushWeapon3, 5f, 0.8f, 4, true)
{
    private BitMask _marked;

    public override void OnEventIcon(Actor actor, uint iconID, ulong targetID)
    {
        base.OnEventIcon(actor, iconID, targetID);
        if (iconID == (uint)IconID.Nox && Raid.FindSlot(actor.InstanceID) is var slot && slot >= 0)
        {
            _marked.Set(slot);
            ExcludedTargets = ~_marked;
        }
    }

    public override void OnEventCast(Actor caster, ActorCastEvent spell)
    {
        base.OnEventCast(caster, spell);
        if (Chasers.Count == 0 && spell.Action.ID is (uint)AID.CrushWeapon2 or (uint)AID.CrushWeapon3)
            _marked = default;
    }
}
sealed class Duskblade(BossModule module) : Components.RaidwideCast(module, (uint)AID.Duskblade);
sealed class HallowedBoltStack(BossModule module) : Components.StackWithIcon(module, (uint)IconID.Icon62, (uint)AID.HallowedBolt5, 6f, 5.1d);
sealed class Shadowblade(BossModule module) : Components.SpreadFromIcon(module, (uint)IconID.Icon170, (uint)AID.Shadowblade2, 5f, 8.2d);

[ModuleInfo(BossModuleInfo.Maturity.WIP, Contributors = "The Combat Reborn Team", GroupType = BossModuleInfo.GroupType.CFC, GroupID = 636, NameID = 7899)] //7917
public class A33ThunderGod(WorldState ws, Actor primary) : BossModule(ws, primary, new(-600, -600), DefaultArena)
{
    public static readonly ArenaBoundsComplex DefaultArena = new([new Circle(new(-612.5f, -578.4f), 10), new Circle(new(-587.5f, -578.4f), 10), new Circle(new(-575, -600), 10), new Circle(new(-587.5f, -621.5f), 10), new Circle(new(-612.5f, -621.5f), 10), new Circle(new(-625, -600), 10), new Donut(new(-600, -600), 20, 27)]);

    protected override void DrawEnemies(int pcSlot, Actor pc)
    {
        Arena.Actor(PrimaryActor);
        Arena.Actors(Enemies((uint)OID.EphemeralKnight));
    }
}
