namespace BossMod.Stormblood.Dungeon.D10HellsLid.D102Kamaitachi;

public enum OID : uint
{
    Boss = 0x20BD, // 鐮鼬 R4.2
    ScytheWind = 0x20F2, // 大鐮風，野鐮衝鋒的端點
    WindOrb = 0x20F1 // 玉風 R0.6
}

// 契約：這個列舉＝模組知道的技能。列舉以外的敵方讀條由 Components.UnclaimedCastAOEs 照沒有模組時的猜法處理，
// 所以刻意不把其他技能列進來（列了就不再被猜）。
public enum AID : uint
{
    CirclingWinds = 10184, // Boss->self, 5.0s cast, range 10-20 donut
    LateHarvest = 10188, // Boss->location, no cast, width 10 rect charge
    WindOrbBurst = 10189 // WindOrb->self, no cast, range 4+R circle
}

public enum TetherID : uint
{
    LateHarvest = 72 // ScytheWind->ScytheWind
}

// 疾風衝是 CastType 10，後備猜法的甜甜圈內徑一律 3（AI 會以為只有王腳下安全）；預兆 gl_sircle_2010 ＝外 20／內 10。
sealed class CirclingWinds(BossModule module) : Components.SimpleAOEs(module, (uint)AID.CirclingWinds, new AOEShapeDonut(10f, 20f));

// 野鐮：大鐮風之間的連線＝接下來三段衝鋒的路線，起點是沒有被連進來的那一隻（王所在處）。
// 連線出現後約 6.05 秒衝第一段、之後每 0.61 秒一段；每段衝完移除。
sealed class LateHarvest(BossModule module) : Components.GenericAOEs(module)
{
    private const float HalfWidth = 5f;
    private readonly Dictionary<ulong, ulong> _edges = [];
    private readonly List<AOEInstance> _aoes = new(3);
    private DateTime _tetheredAt;
    private bool _frozen;

    public override ReadOnlySpan<AOEInstance> ActiveAOEs(int slot, Actor actor) => CollectionsMarshal.AsSpan(_aoes);

    public override void OnTethered(Actor source, ActorTetherInfo tether)
    {
        if (tether.ID != (uint)TetherID.LateHarvest)
            return;
        if (_frozen || (WorldState.CurrentTime - _tetheredAt).TotalSeconds > 1d)
        {
            _edges.Clear();
            _aoes.Clear();
            _frozen = false;
            _tetheredAt = WorldState.CurrentTime;
        }
        _edges[source.InstanceID] = tether.Target;
    }

    // 連線還在時大鐮風仍在微調位置 ⇒ 每幀照當下位置重建；連線消失（衝鋒前約 1 秒）就凍結。
    public override void OnUntethered(Actor source, ActorTetherInfo tether)
    {
        if (tether.ID == (uint)TetherID.LateHarvest && _edges.Count != 0)
            _frozen = true;
    }

    public override void Update()
    {
        if (_edges.Count == 0)
            return;
        if ((WorldState.CurrentTime - _tetheredAt).TotalSeconds > 12d)
        {
            _edges.Clear();
            _aoes.Clear();
            return;
        }
        if (!_frozen)
            Rebuild();
    }

    private void Rebuild()
    {
        _aoes.Clear();
        ulong start = 0;
        foreach (var src in _edges.Keys)
        {
            if (!_edges.ContainsValue(src))
            {
                start = src;
                break;
            }
        }
        var cur = start;
        for (var i = 0; i < _edges.Count && _edges.TryGetValue(cur, out var next); ++i)
        {
            if (WorldState.Actors.Find(cur) is not Actor a || WorldState.Actors.Find(next) is not Actor b)
                break;
            var dir = b.Position - a.Position;
            _aoes.Add(new(new AOEShapeRect(dir.Length(), HalfWidth), a.Position, Angle.FromDirection(dir), _tetheredAt.AddSeconds(6.05d + 0.61d * i)));
            cur = next;
        }
    }

    public override void OnEventCast(Actor caster, ActorCastEvent spell)
    {
        if (spell.Action.ID == (uint)AID.LateHarvest && _aoes.Count != 0)
        {
            _aoes.RemoveAt(0);
            if (_aoes.Count == 0)
                _edges.Clear();
        }
    }
}

// 玉風：出現後約 37 秒自爆（只量到一輪，近似）。只在自爆前 8 秒列進危險區，免得整段都把場中央封掉。
sealed class WindOrbBurst(BossModule module) : Components.GenericAOEs(module)
{
    private static readonly AOEShapeCircle circle = new(4.6f);
    private readonly List<(Actor Orb, DateTime Burst)> _orbs = new(5);
    private readonly List<AOEInstance> _aoes = new(5);

    public override ReadOnlySpan<AOEInstance> ActiveAOEs(int slot, Actor actor) => CollectionsMarshal.AsSpan(_aoes);

    public override void OnActorCreated(Actor actor)
    {
        if (actor.OID == (uint)OID.WindOrb)
            _orbs.Add((actor, WorldState.FutureTime(37d)));
    }

    public override void OnActorDestroyed(Actor actor)
    {
        if (actor.OID == (uint)OID.WindOrb)
            Remove(actor);
    }

    public override void OnEventCast(Actor caster, ActorCastEvent spell)
    {
        if (spell.Action.ID == (uint)AID.WindOrbBurst)
            Remove(caster);
    }

    private void Remove(Actor orb)
    {
        for (var i = _orbs.Count - 1; i >= 0; --i)
            if (_orbs[i].Orb == orb)
                _orbs.RemoveAt(i);
    }

    public override void Update()
    {
        _aoes.Clear();
        var now = WorldState.CurrentTime;
        var count = _orbs.Count;
        for (var i = 0; i < count; ++i)
        {
            var (orb, burst) = _orbs[i];
            if ((burst - now).TotalSeconds <= 8d)
                _aoes.Add(new(circle, orb.Position, default, burst));
        }
    }
}

sealed class D102KamaitachiStates : StateMachineBuilder
{
    public D102KamaitachiStates(BossModule module) : base(module)
    {
        TrivialPhase()
            .ActivateOnEnter<CirclingWinds>()
            .ActivateOnEnter<LateHarvest>()
            .ActivateOnEnter<WindOrbBurst>()
            .ActivateOnEnter<Components.UnclaimedCastAOEs>();
    }
}

// 場地與尋路沿用沒有模組時的做法（跟著玩家走的方框）：這個副本沒有障礙物圖。
// 刻意不做鐮風（距離衰減，錄影裡 35 碼外仍吃到傷害、沒有安全距離）、鞭鼬（沒有讀條的死刑）。
[ModuleInfo(BossModuleInfo.Maturity.WIP, Contributors = "ffxiv-tc-port", GroupType = BossModuleInfo.GroupType.CFC, GroupID = 284, NameID = 6995)]
public sealed class D102Kamaitachi(WorldState ws, Actor primary) : SimpleBossModule(ws, primary);
