namespace BossMod.Endwalker.Raid.P12NAthena;

// 超鏈理論：球／環連線到原點，約 7.8 秒後開始以約 3 碼／秒移動，到原點時原點放超鏈爆發（圓 7）或超鏈回環（甜甜圈 6～70）。
// 生效時間＝連線時刻＋8.9＋0.328×連線距離（錄影擬合）；只畫最近一批（1 秒內），因為先後兩個甜甜圈的安全區互斥。
// 連線目標有時晚於連線事件才出現，每幀重查。
sealed class SuperchainTheory(BossModule module) : Components.GenericAOEs(module)
{
    private static readonly AOEShapeCircle _circle = new(7f);
    private static readonly AOEShapeDonut _donut = new(6f, 70f);
    private readonly List<(Actor Sphere, DateTime TetheredAt)> _pending = [];
    private readonly List<AOEInstance> _aoes = [];

    public override ReadOnlySpan<AOEInstance> ActiveAOEs(int slot, Actor actor)
    {
        var count = _aoes.Count;
        if (count == 0)
            return [];
        var threshold = _aoes[0].Activation.AddSeconds(1d);
        var n = 1;
        while (n < count && _aoes[n].Activation < threshold)
            ++n;
        return CollectionsMarshal.AsSpan(_aoes)[..n];
    }

    public override void OnTethered(Actor source, ActorTetherInfo tether)
    {
        if (tether.ID is (uint)TetherID.SuperchainCircle or (uint)TetherID.SuperchainDonut)
            _pending.Add((source, WorldState.CurrentTime));
    }

    public override void Update()
    {
        for (var i = _pending.Count - 1; i >= 0; --i)
        {
            var (sphere, tetheredAt) = _pending[i];
            var tid = sphere.Tether.ID;
            if (tid is not (uint)TetherID.SuperchainCircle and not (uint)TetherID.SuperchainDonut)
            {
                _pending.RemoveAt(i);
                continue;
            }
            if (WorldState.Actors.Find(sphere.Tether.Target) is not Actor origin)
                continue;
            var distance = (sphere.Position - origin.Position).Length();
            AOEShape shape = tid == (uint)TetherID.SuperchainDonut ? _donut : _circle;
            _aoes.Add(new(shape, origin.Position.Quantized(), default, tetheredAt.AddSeconds(8.9d + 0.328d * distance)));
            _pending.RemoveAt(i);
            _aoes.Sort((a, b) => a.Activation.CompareTo(b.Activation));
        }
        var expire = WorldState.CurrentTime.AddSeconds(-2d);
        for (var i = _aoes.Count - 1; i >= 0; --i)
        {
            if (_aoes[i].Activation < expire)
                _aoes.RemoveAt(i);
        }
    }

    public override void OnEventCast(Actor caster, ActorCastEvent spell)
    {
        var id = spell.Action.ID;
        if (id is not (uint)AID.SuperchainBurst and not (uint)AID.SuperchainCoil)
            return;
        ++NumCasts;
        AOEShape shape = id == (uint)AID.SuperchainCoil ? _donut : _circle;
        var count = _aoes.Count;
        for (var i = 0; i < count; ++i)
        {
            var aoe = _aoes[i];
            if (aoe.Shape == shape && aoe.Origin.AlmostEqual(caster.Position, 1f))
            {
                _aoes.RemoveAt(i);
                return;
            }
        }
    }
}
