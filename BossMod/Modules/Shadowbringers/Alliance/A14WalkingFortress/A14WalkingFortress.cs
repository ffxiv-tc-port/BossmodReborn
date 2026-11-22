namespace BossMod.Shadowbringers.Alliance.A14WalkingFortress;

sealed class Neutralization(BossModule module) : Components.SingleTargetCast(module, (uint)AID.Neutralization);
sealed class LaserSaturation(BossModule module) : Components.RaidwideCast(module, (uint)AID.LaserSaturation);
sealed class LaserTurret(BossModule module) : Components.SimpleAOEs(module, (uint)AID.LaserTurret, new AOEShapeRect(90f, 4f));
sealed class GroundToGroundMissile(BossModule module) : Components.SimpleAOEs(module, (uint)AID.GroundToGroundMissile, 6f);
sealed class BallisticImpact(BossModule module) : Components.SpreadFromCastTargets(module, (uint)AID.BallisticImpact, 6f);
sealed class MarxImpact(BossModule module) : Components.SimpleAOEs(module, (uint)AID.MarxImpact, 22f);
sealed class ConvenientSelfDestructionSmall(BossModule module) : Components.SimpleAOEs(module, (uint)AID.ConvenientSelfDestructionSmall, 22f);
sealed class ClangingBlow(BossModule module) : Components.SingleTargetCast(module, (uint)AID.ClangingBlow);
sealed class ShrapnelImpact(BossModule module) : Components.StackWithCastTargets(module, (uint)AID.ShrapnelImpact, 6f);
sealed class CentrifugalSpin(BossModule module) : Components.SimpleAOEs(module, (uint)AID.CentrifugalSpin, new AOEShapeRect(30f, 4f));
sealed class AnnihilationBombing(BossModule module) : Components.RaidwideCast(module, (uint)AID.AnnihilationBombing);

// 對人飛彈亂射：讀條期間每隔約 3 秒一組 8 個圖示標在 Helper 上，標記當下 Helper 的位置＝那一波 15×15 方格的中心；
// Helper 之後會瞬移，所以記座標、不跟人。各波在讀條結束後約 0.1、2.1、4.2 秒依標記順序落下（沒有讀條）。
// 方格會蓋滿大半個場地，只把最早一波當危險、下一波只預告。
sealed class MissileBarrage(BossModule module) : Components.GenericAOEs(module)
{
    private static readonly AOEShapeRect square = new(7.5f, 7.5f, 7.5f);
    private const double FirstWave = 0.1d, WaveInterval = 2.05d, NoCastFallback = 6d, Expire = 8d;

    private sealed class Wave(int index, DateTime marked)
    {
        public readonly int Index = index;
        public readonly DateTime Marked = marked;
        public readonly List<WPos> Centers = new(8);
    }

    private readonly List<Wave> _waves = new(3);
    private readonly List<AOEInstance> _aoes = new(16);
    private int _nextIndex;
    private DateTime _lastIcon, _castEnd;

    public override ReadOnlySpan<AOEInstance> ActiveAOEs(int slot, Actor actor) => CollectionsMarshal.AsSpan(_aoes);

    public override void OnEventIcon(Actor actor, uint iconID, ulong targetID)
    {
        if (iconID != (uint)IconID.MissileTarget)
            return;
        var now = WorldState.CurrentTime;
        if (_waves.Count == 0)
        {
            // 一輪的標記都在第一波落下前給完，所以沒有待落的波＝新的一輪；讀條可能比第一組標記早到。
            _nextIndex = 0;
            if (_castEnd < now)
                _castEnd = default;
        }
        if (_waves.Count == 0 || (now - _lastIcon).TotalSeconds > 1d)
            _waves.Add(new(_nextIndex++, now));
        _waves[^1].Centers.Add(actor.Position);
        _lastIcon = now;
    }

    public override void OnCastStarted(Actor caster, ActorCastInfo spell)
    {
        if (spell.Action.ID is (uint)AID.MissileBarrage5 or (uint)AID.MissileBarrage6 or (uint)AID.MissileBarrage9)
            _castEnd = Module.CastFinishAt(spell);
    }

    public override void OnEventCast(Actor caster, ActorCastEvent spell)
    {
        if (spell.Action.ID != (uint)AID.MissileImpact)
            return;
        var pos = caster.Position;
        for (var i = 0; i < _waves.Count; ++i)
        {
            var centers = _waves[i].Centers;
            for (var j = 0; j < centers.Count; ++j)
            {
                if ((centers[j] - pos).LengthSq() < 1f)
                {
                    centers.RemoveAt(j);
                    if (centers.Count == 0)
                        _waves.RemoveAt(i);
                    return;
                }
            }
        }
    }

    public override void Update()
    {
        _aoes.Clear();
        if (_waves.Count == 0)
            return;
        var now = WorldState.CurrentTime;
        var expire = _castEnd != default ? _castEnd.AddSeconds(Expire) : _lastIcon.AddSeconds(Expire + NoCastFallback);
        if (now > expire)
        {
            _waves.Clear();
            return;
        }
        var max = _waves.Count > 2 ? 2 : _waves.Count;
        for (var i = 0; i < max; ++i)
        {
            var w = _waves[i];
            var activation = _castEnd != default ? _castEnd.AddSeconds(FirstWave + WaveInterval * w.Index) : w.Marked.AddSeconds(NoCastFallback);
            var color = i == 0 && max > 1 ? Colors.Danger : default;
            for (var j = 0; j < w.Centers.Count; ++j)
                _aoes.Add(new(square, w.Centers[j], default, activation, color, i == 0));
        }
    }
}

// 前後齊射＝前後兩道、側面齊射＝左右兩道扇形，正側面／正前後安全；角度沒有資料，保守畫 150 度（不到 180＝安全方向仍留空）。
sealed class LaserSuppression(BossModule module) : Components.SimpleAOEs(module, (uint)AID.LaserSuppression, new AOEShapeCone(60f, 75f.Degrees()));

// 被駭的戰車自爆可以躲在另一台戰車後面擋掉；擋的只算施放者以外的戰車。
sealed class ConvenientSelfDestructionLarge(BossModule module) : Components.CastLineOfSightAOE(module, (uint)AID.ConvenientSelfDestructionLarge, 85f)
{
    public override ReadOnlySpan<Actor> BlockerActors()
    {
        var tanks = Module.Enemies((uint)OID.GoliathTank);
        var caster = ActiveCaster;
        var blockers = new List<Actor>(tanks.Count);
        for (var i = 0; i < tanks.Count; ++i)
            if (tanks[i] != caster)
                blockers.Add(tanks[i]);
        return CollectionsMarshal.AsSpan(blockers);
    }
}

// 三道俯衝同時開始讀條、依序結算；三道可能蓋滿全場，所以只把最先結算的當危險、下一道只預告。
sealed class Divebomb(BossModule module) : Components.GenericAOEs(module)
{
    private static readonly AOEShapeRect rect = new(60f, 10f);
    private readonly List<AOEInstance> _aoes = new(3);

    private static bool IsLane(uint aid) => aid is (uint)AID.DivebombLane1 or (uint)AID.DivebombLane2 or (uint)AID.DivebombLane3;

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
        if (IsLane(spell.Action.ID))
        {
            _aoes.Add(new(rect, spell.LocXZ, spell.Rotation, Module.CastFinishAt(spell), actorID: caster.InstanceID));
            _aoes.Sort((a, b) => a.Activation.CompareTo(b.Activation));
        }
    }

    public override void OnCastFinished(Actor caster, ActorCastInfo spell)
    {
        if (!IsLane(spell.Action.ID))
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

// 首領的無敵來自大型戰車的連線、戰車全倒才解除，所以首領無敵期間先打戰車；首領本身由 UnclaimedCastAOEs 降成無敵優先度。
// 只把預設優先度的戰車拉高：被判成不該打（沒在跟我方戰鬥等）的照舊。
sealed class GoliathTankPriority(BossModule module) : BossComponent(module)
{
    private readonly List<Actor> _tanks = module.Enemies((uint)OID.GoliathTank);

    public override void AddAIHints(int slot, Actor actor, PartyRolesConfig.Assignment assignment, AIHints hints)
    {
        if (Module.PrimaryActor.FindStatus((uint)SID.Invincibility) == null)
            return;
        var count = _tanks.Count;
        for (var i = 0; i < count; ++i)
        {
            var t = _tanks[i];
            if (hints.FindEnemy(t) is { } e && e.Actor == t && e.Priority == 0)
                e.Priority = 1;
        }
    }
}

// 場地＝碰撞資料的方形甲板（x 870.25～930、z 397.25～457），內縮到半邊 29.5；南側通道戰鬥中封閉，不放進場地。
[ModuleInfo(BossModuleInfo.Maturity.WIP, Contributors = "The Combat Reborn Team, ffxiv-tc-port", GroupType = BossModuleInfo.GroupType.CFC, GroupID = 700, NameID = 9153)]
public sealed class A14WalkingFortress(WorldState ws, Actor primary) : BossModule(ws, primary, new(900f, 427f), new ArenaBoundsSquare(29.5f));
