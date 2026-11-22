namespace BossMod.Endwalker.Raid.P12NAthena;

// 場地＝8 塊 20×10 平台；場地效果索引＝列×2＋欄（欄 0：x 90、欄 1：x 110；列 0～3：z 85、95、105、115）。
// 狀態 0x00020001（貪食）／0x00200001（女神的護佑橫斬）＝崩落，0x00080004＝復原；崩落的平台掉下去即死，畫成一直存在的危險區。
// 靈魂鏈連線（Wing→王）在 8.2 秒後崩落，先依連線畫。
sealed class Platforms(BossModule module) : Components.GenericAOEs(module, (uint)AID.Sample)
{
    private static readonly AOEShapeRect _shape = new(5f, 10f, 5f);
    private readonly List<AOEInstance> _aoes = [];

    public override ReadOnlySpan<AOEInstance> ActiveAOEs(int slot, Actor actor) => CollectionsMarshal.AsSpan(_aoes);

    private static WPos PlatformCenter(int index) => new(90f + 20f * (index & 1), 85f + 10f * (index >> 1));

    private int IndexOf(WPos center)
    {
        var count = _aoes.Count;
        for (var i = 0; i < count; ++i)
        {
            if (_aoes[i].Origin.AlmostEqual(center, 1f))
                return i;
        }
        return -1;
    }

    public override void OnTethered(Actor source, ActorTetherInfo tether)
    {
        if (tether.ID == (uint)TetherID.UnnaturalEnchainment)
        {
            var center = source.Position.Quantized();
            if (IndexOf(center) < 0)
                _aoes.Add(new(_shape, center, default, WorldState.FutureTime(8.2d)));
        }
    }

    public override void OnEventEnvControl(byte index, uint state)
    {
        if (index >= 8)
            return;
        var center = PlatformCenter(index);
        var i = IndexOf(center);
        switch (state)
        {
            case 0x00020001u:
            case 0x00200001u:
                if (i < 0)
                    _aoes.Add(new(_shape, center, default, WorldState.CurrentTime));
                break;
            case 0x00080004u:
                if (i >= 0)
                    _aoes.RemoveAt(i);
                break;
        }
    }
}
