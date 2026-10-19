namespace BossMod.Heavensward.Trial.T07Zurvan;

// 普通版與極版共用（兩版的場地物件 OID 相同）。場地中心 (0,0)。本檔的假設與契約：
// - 第一階段是環：中央半徑 5 以內是牆、外牆半徑 20；地板分東／北／西／南四片，各佔 90 度。
// - 某一片的場地物件播 0x00040008＝那一片約 5 秒後塌；播 0x00100020＝已經塌了，站上去會掉下去。
// - 第二階段地板的場地物件狀態變成 4＝換場地：中央可以走，地板到半徑 23，其中 20 以外那一圈站著會持續扣血。
// - 模組中途才載入時看不到先前的事件，已塌的片會被當成還在（與沒有模組時相同）。
public abstract class ZurvanModule(WorldState ws, Actor primary, uint phase2BossOID) : BossModule(ws, primary, ZurvanFloor.Center, ZurvanFloor.Phase1Bounds)
{
    protected override void DrawEnemies(int pcSlot, Actor pc)
    {
        // 第一階段的首領站在環中央的牆裡；照一般畫法會被夾到場地邊緣
        if (PrimaryActor.IsTargetable && !PrimaryActor.IsDead)
            Arena.ActorInsideBounds(PrimaryActor.Position, PrimaryActor.Rotation, Colors.Enemy);
        Arena.Actors(Enemies(phase2BossOID));
    }
}

sealed class ZurvanFloor(BossModule module) : Components.GenericAOEs(module)
{
    public static readonly WPos Center = default;
    public const float InnerRadius = 5f, OuterRadius = 20f, Phase2Radius = 23f;
    public static readonly ArenaBoundsComplex Phase1Bounds = new([new Donut(Center, InnerRadius, OuterRadius)]);
    public static readonly ArenaBoundsCircle Phase2Bounds = new(Phase2Radius);

    private const uint FloorPhase2 = 0x1EA28Eu;
    private const uint AnimWarning = 0x00040008u, AnimCollapsed = 0x00100020u;
    private const ushort StateShown = 4;
    private const double CollapseDelay = 5d;

    // 刻意不含南邊那一片（0x1EA289）：沒有它會塌的佐證
    private static readonly uint[] _floorOIDs = [0x1EA28Au, 0x1EA28Bu, 0x1EA28Cu];
    private static readonly Angle[] _floorDirs = [90f.Degrees(), 180f.Degrees(), -90f.Degrees()];
    private static readonly Angle _halfAngle = 45f.Degrees();
    private static readonly AOEShapeCone _shape = new(OuterRadius, _halfAngle);
    // 做成常駐危險區而不是場地外：只是在場地外的話，沒有別的危險區時 AI 不會主動走回來
    private static readonly AOEShapeDonut _damageRing = new(OuterRadius, Phase2Radius + 2f);

    private readonly List<AOEInstance> _aoes = new(3);
    private int _collapsed; // 位元 i＝_floorOIDs[i] 那一片已塌
    private bool _phase2;

    public override ReadOnlySpan<AOEInstance> ActiveAOEs(int slot, Actor actor) => CollectionsMarshal.AsSpan(_aoes);

    public override void OnActorEAnim(Actor actor, uint state)
    {
        var index = Array.IndexOf(_floorOIDs, actor.OID);
        if (index < 0 || _phase2)
            return;
        if (state == AnimWarning)
        {
            RemoveWarning(actor.InstanceID);
            if ((_collapsed & (1 << index)) == 0)
                _aoes.Add(new(_shape, Center, _floorDirs[index], WorldState.FutureTime(CollapseDelay), actorID: actor.InstanceID));
        }
        else if (state == AnimCollapsed)
        {
            RemoveWarning(actor.InstanceID);
            _collapsed |= 1 << index;
            var holes = new List<Shape>(3);
            for (var i = 0; i < _floorOIDs.Length; ++i)
                if ((_collapsed & (1 << i)) != 0)
                    holes.Add(new ConeHA(Center, OuterRadius + 2f, _floorDirs[i], _halfAngle));
            var bounds = new ArenaBoundsComplex([new Donut(Center, InnerRadius, OuterRadius)], [.. holes]);
            Arena.Bounds = bounds;
            Arena.Center = bounds.Center;
        }
    }

    public override void OnActorEState(Actor actor, ushort state)
    {
        if (actor.OID != FloorPhase2 || state != StateShown)
            return;
        _phase2 = true;
        _aoes.Clear();
        _aoes.Add(new(_damageRing, Center));
        Arena.Bounds = Phase2Bounds;
        Arena.Center = Center;
    }

    private void RemoveWarning(ulong floorInstanceID)
    {
        for (var i = _aoes.Count - 1; i >= 0; --i)
            if (_aoes[i].ActorID == floorInstanceID)
                _aoes.RemoveAt(i);
    }
}
