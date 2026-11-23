namespace BossMod.Shadowbringers.Alliance.A11CommandModel;

sealed class ClangingBlow(BossModule module) : Components.SingleTargetCast(module, (uint)AID.ClangingBlow);
sealed class ForcefulImpact(BossModule module) : Components.RaidwideCast(module, (uint)AID.ForcefulImpact);
sealed class EnergyBombardment(BossModule module) : Components.SimpleAOEs(module, (uint)AID.EnergyBombardment, 4f);
sealed class SidestrikingSpin(BossModule module) : Components.SimpleAOEGroups(module, [(uint)AID.SidestrikingSpinL, (uint)AID.SidestrikingSpinR], new AOEShapeRect(30f, 6f));
sealed class CentrifugalSpin(BossModule module) : Components.SimpleAOEs(module, (uint)AID.CentrifugalSpin, new AOEShapeRect(30f, 4f));
sealed class HighCaliberLaser(BossModule module) : Components.SimpleAOEs(module, (uint)AID.HighCaliberLaser, new AOEShapeRect(70f, 12f));
sealed class Shockwave(BossModule module) : Components.RaidwideCast(module, (uint)AID.Shockwave);

// 能量球：士兵機在場邊讀條後，一顆球瞬移到落點往面向 4 碼處，停約 0.85 秒再以約 4 碼／秒直線穿越全場，
// 途中靠近玩家就對那個人爆炸（單體、沒有讀條），爆炸或撞到對邊停下就失效。
// 球不出發時停在場外待機點或上次停下的地方，所以只畫有出發紀錄的球；方向以球實際的移動為準。
sealed class EnergyOrb(BossModule module) : Components.GenericAOEs(module)
{
    private const float Radius = 3f, LookAhead = 4f, Speed = 4f, SpawnOffset = 4f;
    private const double MoveDelay = 0.85d;
    private static readonly AOEShapeCapsule capsule = new(Radius, LookAhead);

    private sealed class Run(WPos spawn, WDir dir, DateTime launch)
    {
        public readonly WPos Spawn = spawn;
        public WDir Dir = dir;
        public readonly DateTime Launch = launch; // 讀條結束＝球瞬移的時刻
        public ulong Orb;
        public WPos LastPos = spawn;
        public DateTime LastMoved;
    }

    private readonly List<Run> _runs = new(12);
    private readonly List<Actor> _orbs = module.Enemies((uint)OID.EnergyOrb);
    private readonly Dictionary<ulong, (WPos Pos, DateTime JumpedAt)> _orbPos = new(12);
    private readonly List<AOEInstance> _aoes = new(12);

    public override ReadOnlySpan<AOEInstance> ActiveAOEs(int slot, Actor actor) => CollectionsMarshal.AsSpan(_aoes);

    public override void OnCastStarted(Actor caster, ActorCastInfo spell)
    {
        if (spell.Action.ID == (uint)AID.EnergyOrbLaunch)
        {
            var dir = spell.Rotation.ToDirection();
            _runs.Add(new(spell.LocXZ + SpawnOffset * dir, dir, Module.CastFinishAt(spell)));
        }
    }

    public override void OnEventCast(Actor caster, ActorCastEvent spell)
    {
        if (spell.Action.ID != (uint)AID.EnergyBomb)
            return;
        var best = -1;
        var bestDist = 4f;
        var now = WorldState.CurrentTime;
        for (var i = 0; i < _runs.Count; ++i)
        {
            var r = _runs[i];
            if (r.Orb == caster.InstanceID)
            {
                best = i;
                break;
            }
            var d = r.Orb == 0 ? (Predicted(r, now) - caster.Position).Length() : float.MaxValue;
            if (d < bestDist)
            {
                bestDist = d;
                best = i;
            }
        }
        if (best >= 0)
            _runs.RemoveAt(best);
    }

    public override void Update()
    {
        var now = WorldState.CurrentTime;
        var count = _orbs.Count;
        for (var i = 0; i < count; ++i)
        {
            var o = _orbs[i];
            var pos = o.Position;
            // 瞬移＝一幀內位移超過 3 碼；用來把剛出發的球對上出發紀錄，避免誤綁停在同一點的舊球。
            if (_orbPos.TryGetValue(o.InstanceID, out var prev))
                _orbPos[o.InstanceID] = ((pos - prev.Pos).LengthSq() > 9f ? (pos, now) : (pos, prev.JumpedAt));
            else
                _orbPos[o.InstanceID] = (pos, default);
        }

        _aoes.Clear();
        for (var i = _runs.Count - 1; i >= 0; --i)
        {
            var r = _runs[i];
            if (r.Orb == 0 && now >= r.Launch)
                Bind(r, now);
            WPos origin;
            if (r.Orb != 0)
            {
                var orb = WorldState.Actors.Find(r.Orb);
                if (orb == null || orb.IsDestroyed)
                {
                    _runs.RemoveAt(i);
                    continue;
                }
                origin = orb.Position;
                var moved = origin - r.Spawn;
                if (moved.LengthSq() > 0.25f)
                    r.Dir = moved.Normalized();
                if ((origin - r.LastPos).LengthSq() > 0.0025f)
                {
                    r.LastPos = origin;
                    r.LastMoved = now;
                }
                else if (moved.LengthSq() > 1f && (now - r.LastMoved).TotalSeconds > 0.5d)
                {
                    _runs.RemoveAt(i); // 撞到對邊停下
                    continue;
                }
            }
            else
            {
                origin = Predicted(r, now);
                if ((origin - r.Spawn).LengthSq() > 2500f)
                {
                    _runs.RemoveAt(i);
                    continue;
                }
            }
            var move = r.Launch.AddSeconds(MoveDelay);
            _aoes.Add(new(capsule, origin, Angle.FromDirection(r.Dir), move > now ? move : now));
        }
    }

    private void Bind(Run r, DateTime now)
    {
        foreach (var (id, s) in _orbPos)
        {
            if (s.JumpedAt == default || (now - s.JumpedAt).TotalSeconds > 1d || (s.Pos - r.Spawn).LengthSq() > 2.25f)
                continue;
            var taken = false;
            for (var j = 0; j < _runs.Count; ++j)
                taken |= _runs[j].Orb == id;
            if (taken)
                continue;
            r.Orb = id;
            r.LastPos = s.Pos;
            r.LastMoved = now;
            return;
        }
    }

    private static WPos Predicted(Run r, DateTime now)
    {
        var t = (float)(now - r.Launch).TotalSeconds - (float)MoveDelay;
        return t > 0f ? r.Spawn + Speed * t * r.Dir : r.Spawn;
    }
}

// 對地能量彈：士兵機瞬發 19250 後約 7.3 秒起沿方框路線飛（約 2.85 碼／秒），約 12 秒後起每 1.07 秒在機體略前方炸半徑 5。
// 路線＝出發角所在象限的方框（中心離場地中心 ±12、半邊 4 或 10），從出發角順時針繞（+x 向右、+z 向上看）⇒ 畫接下來 4 發的落點。
// 認不出方框的機體照舊只畫前方一步（3.2 碼）的膠囊；兩種都是第一發前約 3 秒才交給 AI 閃，免得機體還沒開始炸就把 AI 推來推去。
sealed class GroundEnergyBomb(BossModule module) : Components.GenericAOEs(module)
{
    private const float Radius = 5f, Step = 3.2f, Speed = 2.85f, BombAhead = 1f;
    private const double Interval = 1.07d, FirstDelay = 12.2d, RiskyLead = 3d, MoveDelay = 7.35d;
    private const int PredictedBombs = 4;
    private static readonly WPos _center = new(-500f, default);
    private static readonly AOEShapeCapsule capsule = new(Radius, Step);
    private static readonly AOEShapeCircle circle = new(Radius);
    private readonly List<Actor> _models = new(8);
    private readonly List<Route?> _routes = new(8); // 與 _models 同索引
    private readonly List<AOEInstance> _aoes = new(8 * PredictedBombs);
    private DateTime _armed, _next, _last;

    private sealed class Route(WPos[] corners, float side, DateTime start)
    {
        public readonly DateTime Start = start;

        public static Route? Create(WPos corner, DateTime start)
        {
            var d = corner - _center;
            var q = new WDir(d.X >= 0f ? 12f : -12f, d.Z >= 0f ? 12f : -12f);
            var r = d - q;
            var h = Math.Abs(r.X);
            if (h < 1f || Math.Abs(Math.Abs(r.Z) - h) > 1f)
                return null;
            int sx = r.X > 0f ? 1 : -1, sz = r.Z > 0f ? 1 : -1;
            var corners = new WPos[4];
            for (var i = 0; i < 4; ++i)
            {
                corners[i] = _center + q + new WDir(sx * h, sz * h);
                if (sx == sz)
                    sz = -sz;
                else
                    sx = -sx;
            }
            return new(corners, 2f * h, start);
        }

        // 路線上離 p 最近的點，回傳從出發角起算的路程
        public float Project(WPos p)
        {
            float best = float.MaxValue, s = 0f;
            for (var i = 0; i < 4; ++i)
            {
                var a = corners[i];
                var e = (corners[(i + 1) & 3] - a) / side;
                var u = Math.Clamp((p - a).Dot(e), 0f, side);
                var dist = (p - (a + e * u)).LengthSq();
                if (dist < best)
                {
                    best = dist;
                    s = i * side + u;
                }
            }
            return s;
        }

        public WPos At(float s)
        {
            var loop = 4f * side;
            s %= loop;
            if (s < 0f)
                s += loop;
            var i = Math.Min((int)(s / side), 3);
            var a = corners[i];
            return a + (corners[(i + 1) & 3] - a) / side * (s - i * side);
        }
    }

    public override ReadOnlySpan<AOEInstance> ActiveAOEs(int slot, Actor actor) => CollectionsMarshal.AsSpan(_aoes);

    public override void OnEventCast(Actor caster, ActorCastEvent spell)
    {
        switch (spell.Action.ID)
        {
            case (uint)AID.GroundBombingStart:
                if (_models.Count == 0)
                {
                    _armed = WorldState.CurrentTime;
                    _next = _armed.AddSeconds(FirstDelay);
                    _last = default;
                }
                if (!_models.Contains(caster))
                {
                    _models.Add(caster);
                    _routes.Add(Route.Create(caster.Position, WorldState.CurrentTime));
                }
                break;
            case (uint)AID.GroundEnergyBomb:
                _last = WorldState.CurrentTime;
                _next = _last.AddSeconds(Interval);
                break;
        }
    }

    public override void Update()
    {
        _aoes.Clear();
        if (_models.Count == 0)
            return;
        var now = WorldState.CurrentTime;
        if (_last != default && (now - _last).TotalSeconds > 2d || (now - _armed).TotalSeconds > 40d)
        {
            _models.Clear();
            _routes.Clear();
            return;
        }
        var lead = (_next - now).TotalSeconds;
        var risky = lead < RiskyLead;
        const double lookahead = (PredictedBombs - 1) * Interval + 0.1d;
        for (var i = 0; i < _models.Count; ++i)
        {
            var m = _models[i];
            if (m.IsDestroyed)
                continue;
            if (_routes[i] is not { } route || lead > lookahead)
            {
                _aoes.Add(new(capsule, m.Position, m.Rotation, _next, risky: risky));
                continue;
            }
            // 起飛前機體停在出發角（路程 0）：飛行時間從起飛那一刻才開始算
            var s = route.Project(m.Position);
            var flownUntil = Math.Max((now - route.Start).TotalSeconds, MoveDelay);
            for (var k = 0; k < PredictedBombs; ++k)
            {
                var at = _next.AddSeconds(k * Interval);
                var travel = Math.Max(0d, (at - route.Start).TotalSeconds - flownUntil);
                _aoes.Add(new(circle, route.At(s + (float)travel * Speed + BombAhead), default, at));
            }
        }
    }
}

// 王轉向隨機玩家後從正面持續放扇形，到王的收尾技能為止；扇形角度沒有資料，保守畫正面半圓（cactbot：繞到背後）。
sealed class EnergyAssault(BossModule module) : Components.GenericAOEs(module)
{
    private static readonly AOEShapeCone cone = new(30f, 90f.Degrees());
    private AOEInstance? _aoe;
    private DateTime _activation;
    private DateTime _expire;

    public override ReadOnlySpan<AOEInstance> ActiveAOEs(int slot, Actor actor) => Utils.ZeroOrOne(ref _aoe);

    public override void Update()
    {
        if (_activation == default)
            return;
        if (WorldState.CurrentTime > _expire)
        {
            _activation = default;
            _aoe = null;
            return;
        }
        var boss = Module.PrimaryActor;
        _aoe = new(cone, boss.Position.Quantized(), boss.Rotation, _activation);
    }

    public override void OnCastStarted(Actor caster, ActorCastInfo spell)
    {
        if (spell.Action.ID == (uint)AID.EnergyAssault)
        {
            _activation = Module.CastFinishAt(spell);
            // 收尾技能沒看到時的上限：時間軸上讀條結束到收尾約 7 秒。
            _expire = _activation.AddSeconds(10d);
        }
    }

    public override void OnEventCast(Actor caster, ActorCastEvent spell)
    {
        if (spell.Action.ID == (uint)AID.EnergyAssaultEnd)
        {
            _activation = default;
            _aoe = null;
        }
    }
}

// 圓與三圈甜甜圈依序結算（內外徑取自台服 Omen），只把最先結算的一個當危險、下一個只預告：AI 每次只需小幅移動。
sealed class EnergyRing(BossModule module) : Components.GenericAOEs(module)
{
    private readonly List<AOEInstance> _aoes = new(4);

    private static AOEShape? ShapeFor(uint aid) => aid switch
    {
        (uint)AID.EnergyRingCircle => new AOEShapeCircle(12f),
        (uint)AID.EnergyRingDonut1 => new AOEShapeDonut(12f, 24f),
        (uint)AID.EnergyRingDonut2 => new AOEShapeDonut(24f, 36f),
        (uint)AID.EnergyRingDonut3 => new AOEShapeDonut(36f, 48f),
        _ => null
    };

    public override ReadOnlySpan<AOEInstance> ActiveAOEs(int slot, Actor actor)
    {
        var count = _aoes.Count;
        if (count == 0)
            return [];
        var max = count > 2 ? 2 : count;
        var aoes = CollectionsMarshal.AsSpan(_aoes);
        for (var i = 0; i < max; ++i)
        {
            ref var aoe = ref aoes[i];
            aoe.Risky = i == 0;
            aoe.Color = i == 0 && max > 1 ? Colors.Danger : default;
        }
        return aoes[..max];
    }

    public override void OnCastStarted(Actor caster, ActorCastInfo spell)
    {
        if (ShapeFor(spell.Action.ID) is AOEShape shape)
        {
            _aoes.Add(new(shape, spell.LocXZ, spell.Rotation, Module.CastFinishAt(spell), actorID: caster.InstanceID));
            _aoes.Sort((a, b) => a.Activation.CompareTo(b.Activation));
        }
    }

    public override void OnCastFinished(Actor caster, ActorCastInfo spell)
    {
        if (ShapeFor(spell.Action.ID) == null)
            return;
        var count = _aoes.Count;
        for (var i = 0; i < count; ++i)
        {
            if (_aoes[i].ActorID == caster.InstanceID)
            {
                _aoes.RemoveAt(i);
                return;
            }
        }
    }
}

// 場地＝碰撞資料的方形倉庫（中心 (-500,0)、半邊 24），內縮 0.5；北側通道戰鬥中封閉，不放進場地。
[ModuleInfo(BossModuleInfo.Maturity.WIP, Contributors = "The Combat Reborn Team, ffxiv-tc-port", GroupType = BossModuleInfo.GroupType.CFC, GroupID = 700, NameID = 9141)]
public sealed class A11CommandModel(WorldState ws, Actor primary) : BossModule(ws, primary, new(-500f, default), new ArenaBoundsSquare(23.5f));
