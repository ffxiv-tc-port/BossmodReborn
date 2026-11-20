namespace BossMod.Heavensward.Trial.T07Zurvan;

public enum OID : uint
{
    Boss = 0x1996, // 第一階段，R7.44
    BossP2 = 0x1997, // 第二階段，R5.27；開場就在場上、不可選取
    FlamingHalberdVoidzone = 0x1EA2A4, // 焚身炎戟落點留下的火圈
    IcyVoidzone = 0x1EA2A5, // 冰與火之後首領腳下的冰圈
    SouthernCrossVoidzone = 0x1EA2A6, // 南十字星落點留下的冰圈
    FireTower = 0x1EA2A7, // 冰炎之紋的紅塔
    IceTower = 0x1EA2A8, // 冰炎之紋的白塔
}

// 只列有元件處理的技能；其餘讀條（耀星、南十字星、隕星、波動砲…）交給 UnclaimedCastAOEs 照沒有模組時的猜法。
public enum AID : uint
{
    MetalCutterP1 = 7744, // Boss->self, no cast, range 30+R 90-degree cone
    MetalCutterP2 = 7738, // BossP2->self, no cast, range 30+R 90-degree cone
    TwinSpiritFirst = 7714, // 0x1998->location, 3.7s cast, width 10 rect charge
    TwinSpiritSecond = 7715, // 0x1998->location, 3.7s cast, width 10 rect charge
    FlamingHalberd = 7716, // 0x199D->location, no cast, range 12 circle spread
    DemonicDive = 7717, // BossP2->location, no cast, range 7 circle stack
    Ciclicle = 7723, // BossP2->self, 4.7s cast, range 10-20 donut
    TyrfingFire = 7737, // BossP2->player, no cast, range 5 circle
}

public enum SID : uint
{
    FireMark = 1143, // 炎之刻印
    IceMark = 1144, // 冰之刻印
}

public enum IconID : uint
{
    FlamingHalberd = 44,
    DemonicDive = 62,
    Tyrfing = 381,
}

class MetalCutterP1(BossModule module) : Components.Cleave(module, (uint)AID.MetalCutterP1, new AOEShapeCone(37.44f, 45f.Degrees()), [(uint)OID.Boss]);
// 冰炎之紋的塔期間沒出現過金屬利刃，而白塔常落在首領正面：塔在場時不讓正面危險區把人擋在塔外
class MetalCutterP2(BossModule module) : Components.Cleave(module, (uint)AID.MetalCutterP2, new AOEShapeCone(35.27f, 45f.Degrees()), [(uint)OID.BossP2])
{
    public override void AddAIHints(int slot, Actor actor, PartyRolesConfig.Assignment assignment, AIHints hints)
    {
        if (Module.FindComponent<BrokenSealTowers>()?.Active ?? false)
            return;
        base.AddAIHints(slot, actor, assignment, hints);
    }
}
class FlamingHalberd(BossModule module) : Components.SpreadFromIcon(module, (uint)IconID.FlamingHalberd, (uint)AID.FlamingHalberd, 12f, 5.1d);
class DemonicDive(BossModule module) : Components.StackWithIcon(module, (uint)IconID.DemonicDive, (uint)AID.DemonicDive, 7f, 5.2d, PartyState.MaxPartySize, PartyState.MaxPartySize);
class Ciclicle(BossModule module) : Components.SimpleAOEs(module, (uint)AID.Ciclicle, new AOEShapeDonut(10f, 20f));

class TyrfingFire(BossModule module) : Components.BaitAwayIcon(module, 5f, (uint)IconID.Tyrfing, (uint)AID.TyrfingFire, 8.2d, tankbuster: true, damageType: AIHints.PredictedDamageType.Tankbuster)
{
    public override Actor? BaitSource(Actor target) => Module.Enemies((uint)OID.BossP2) is var b && b.Count != 0 ? b[0] : null;
}

// 場地物件播完（EventState 7）就不再傷人，之後幾秒才消失
static class ZurvanVoidzones
{
    public static IEnumerable<Actor> Active(BossModule module, uint oid) => module.Enemies(oid).Where(z => z.EventState != 7);
}
class FlamingHalberdVoidzone(BossModule module) : Components.Voidzone(module, 8f, m => ZurvanVoidzones.Active(m, (uint)OID.FlamingHalberdVoidzone));
class IcyVoidzone(BossModule module) : Components.Voidzone(module, 5f, m => ZurvanVoidzones.Active(m, (uint)OID.IcyVoidzone));
class SouthernCrossVoidzone(BossModule module) : Components.Voidzone(module, 6f, m => ZurvanVoidzones.Active(m, (uint)OID.SouthernCrossVoidzone));

// 第一段衝鋒結束後，第二段一定從第一段的落點出發、穿過場地中心到對面；落點一出來就先畫，不等第二段讀條。
sealed class TwinSpirit(BossModule module) : Components.GenericAOEs(module)
{
    private const float HalfWidth = 5f;
    private const double SecondChargeDelay = 8.2d;
    private readonly List<AOEInstance> _aoes = new(4);

    public override ReadOnlySpan<AOEInstance> ActiveAOEs(int slot, Actor actor) => CollectionsMarshal.AsSpan(_aoes);

    public override void OnCastStarted(Actor caster, ActorCastInfo spell)
    {
        var id = spell.Action.ID;
        if (id is not ((uint)AID.TwinSpiritFirst or (uint)AID.TwinSpiritSecond))
            return;
        if (id == (uint)AID.TwinSpiritSecond)
            RemovePredictedFrom(caster.Position);
        Add(caster.Position, spell.LocXZ, Module.CastFinishAt(spell), caster.InstanceID);
    }

    public override void OnCastFinished(Actor caster, ActorCastInfo spell)
    {
        var id = spell.Action.ID;
        if (id is not ((uint)AID.TwinSpiritFirst or (uint)AID.TwinSpiritSecond))
            return;
        for (var i = _aoes.Count - 1; i >= 0; --i)
            if (_aoes[i].ActorID == caster.InstanceID)
                _aoes.RemoveAt(i);
        if (id == (uint)AID.TwinSpiritFirst)
        {
            var start = spell.LocXZ;
            Add(start, ZurvanFloor.Center + (ZurvanFloor.Center - start), WorldState.FutureTime(SecondChargeDelay), default);
        }
        else
            ++NumCasts;
    }

    public override void Update()
    {
        // 預測的那一段若始終沒等到讀條（滅團、換場）就自己過期
        var expired = WorldState.CurrentTime.AddSeconds(-2d);
        for (var i = _aoes.Count - 1; i >= 0; --i)
            if (_aoes[i].ActorID == default && _aoes[i].Activation < expired)
                _aoes.RemoveAt(i);
    }

    private void Add(WPos from, WPos to, DateTime activation, ulong actorID)
    {
        var dir = to - from;
        _aoes.Add(new(new AOEShapeRect(dir.Length(), HalfWidth), from, Angle.FromDirection(dir), activation, actorID: actorID));
    }

    private void RemovePredictedFrom(WPos origin)
    {
        for (var i = _aoes.Count - 1; i >= 0; --i)
            if (_aoes[i].ActorID == default && _aoes[i].Origin.InCircle(origin, 2f))
                _aoes.RemoveAt(i);
    }
}

// 冰炎之紋：炎之刻印踩紅塔、冰之刻印踩白塔（半徑 2）。同色塔沒人踩，或站進別色的塔都不算數，全隊受傷加重。
// 刻印只從狀態事件得知：模組中途才載入時看不到已上的刻印，此時所有塔都當成不能踩。
sealed class BrokenSealTowers(BossModule module) : BossComponent(module)
{
    private const float Radius = 2f;
    private const double ResolveDelay = 14.3d;
    private static readonly AOEShapeCircle _shape = new(Radius);
    private readonly List<Actor> _fire = module.Enemies((uint)OID.FireTower);
    private readonly List<Actor> _ice = module.Enemies((uint)OID.IceTower);
    private BitMask _fireMarked, _iceMarked;
    private DateTime _activation;

    public override void OnActorCreated(Actor actor)
    {
        if (actor.OID is (uint)OID.FireTower or (uint)OID.IceTower)
            _activation = WorldState.FutureTime(ResolveDelay);
    }

    public override void OnStatusGain(Actor actor, ActorStatus status) => SetMark(actor, status.ID, true);
    public override void OnStatusLose(Actor actor, ActorStatus status) => SetMark(actor, status.ID, false);

    public override void AddHints(int slot, Actor actor, TextHints hints)
    {
        var mine = MyTowers(slot);
        var inMine = false;
        var inOther = false;
        ForEachActive(t =>
        {
            if (!actor.Position.InCircle(t.Position, Radius))
                return;
            if (IsMine(mine, t))
                inMine = true;
            else
                inOther = true;
        });
        if (inOther)
            hints.Add(Loc.T("GTFO from tower!"));
        else if (!inMine && HasActive(mine))
            hints.Add(Loc.T("Soak the tower!"));
    }

    public override void AddAIHints(int slot, Actor actor, PartyRolesConfig.Assignment assignment, AIHints hints)
    {
        var mine = MyTowers(slot);
        ForEachActive(t =>
        {
            if (!IsMine(mine, t))
                hints.AddForbiddenZone(ShapeDistance.Circle(t.Position, Radius), _activation);
        });
        if (mine == null || !HasActive(mine))
            return;
        // 優先去還沒有同色隊友的塔（自己已站著的也算）；都有人了就任選一座同色塔
        var empty = new List<Func<WPos, float>>(2);
        var any = new List<Func<WPos, float>>(2);
        var marks = mine == _fire ? _fireMarked : _iceMarked;
        for (var i = 0; i < mine.Count; ++i)
        {
            var t = mine[i];
            if (t.EventState == 7)
                continue;
            var goal = ShapeDistance.InvertedCircle(t.Position, Radius);
            any.Add(goal);
            if (actor.Position.InCircle(t.Position, Radius) || !OtherMarkedInside(slot, marks, t.Position))
                empty.Add(goal);
        }
        hints.AddForbiddenZone(ShapeDistance.Intersection(empty.Count != 0 ? empty : any), _activation);
    }

    public override void DrawArenaBackground(int pcSlot, Actor pc)
    {
        var mine = MyTowers(pcSlot);
        ForEachActive(t =>
        {
            if (!IsMine(mine, t))
                _shape.Draw(Arena, t.Position);
        });
    }

    public override void DrawArenaForeground(int pcSlot, Actor pc)
    {
        var mine = MyTowers(pcSlot);
        ForEachActive(t => Arena.AddCircle(t.Position, Radius, IsMine(mine, t) ? Colors.Safe : default, 2f));
    }

    public bool Active => HasActive(_fire) || HasActive(_ice);

    private List<Actor>? MyTowers(int slot) => _fireMarked[slot] ? _fire : _iceMarked[slot] ? _ice : null;
    private static bool IsMine(List<Actor>? mine, Actor tower) => mine != null && mine.Contains(tower);

    private static bool HasActive(List<Actor>? towers)
    {
        if (towers == null)
            return false;
        for (var i = 0; i < towers.Count; ++i)
            if (towers[i].EventState != 7)
                return true;
        return false;
    }

    private void ForEachActive(Action<Actor> action)
    {
        for (var i = 0; i < _fire.Count; ++i)
            if (_fire[i].EventState != 7)
                action(_fire[i]);
        for (var i = 0; i < _ice.Count; ++i)
            if (_ice[i].EventState != 7)
                action(_ice[i]);
    }

    private bool OtherMarkedInside(int slot, BitMask marks, WPos tower)
    {
        var party = Raid.WithSlot();
        for (var i = 0; i < party.Length; ++i)
        {
            var (s, a) = party[i];
            if (s != slot && marks[s] && !a.IsDead && a.Position.InCircle(tower, Radius))
                return true;
        }
        return false;
    }

    private void SetMark(Actor actor, uint sid, bool value)
    {
        if (sid is not ((uint)SID.FireMark or (uint)SID.IceMark))
            return;
        var slot = Raid.FindSlot(actor.InstanceID);
        if (slot < 0)
            return;
        ref var mask = ref sid == (uint)SID.FireMark ? ref _fireMarked : ref _iceMarked;
        mask[slot] = value;
    }
}

// 狀態機不排時間軸：每個元件都由事件驅動，整場掛著不會卡在某個預期時間點。
class T07ZurvanStates : StateMachineBuilder
{
    public T07ZurvanStates(BossModule module) : base(module)
    {
        TrivialPhase()
            .ActivateOnEnter<ZurvanFloor>()
            .ActivateOnEnter<MetalCutterP1>()
            .ActivateOnEnter<MetalCutterP2>()
            .ActivateOnEnter<TwinSpirit>()
            .ActivateOnEnter<FlamingHalberd>()
            .ActivateOnEnter<FlamingHalberdVoidzone>()
            .ActivateOnEnter<DemonicDive>()
            .ActivateOnEnter<IcyVoidzone>()
            .ActivateOnEnter<SouthernCrossVoidzone>()
            .ActivateOnEnter<Ciclicle>()
            .ActivateOnEnter<TyrfingFire>()
            .ActivateOnEnter<BrokenSealTowers>()
            .ActivateOnEnter<Components.UnclaimedCastAOEs>();
    }
}

[ModuleInfo(BossModuleInfo.Maturity.Contributed, Contributors = "ffxiv-tc-port", GroupType = BossModuleInfo.GroupType.CFC, GroupID = 223, NameID = 5567)]
public class T07Zurvan(WorldState ws, Actor primary) : ZurvanModule(ws, primary, (uint)OID.BossP2);
