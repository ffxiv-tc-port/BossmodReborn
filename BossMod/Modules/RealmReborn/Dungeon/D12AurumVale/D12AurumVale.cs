namespace BossMod.RealmReborn.Dungeon.D12AurumVale;

// 黃金毒瓦斯噴口：全副本都有、沒有讀條、各自每 6～18 秒隨機噴一次、看不出下一次是何時 ⇒ 一律畫成常駐危險區（不給生效時間）。
public static class GoldBilePlumes
{
    public const uint OIDCircle = 0x5CA; // R0.5; 黃金毒瓦斯 1042: range 2 circle, knockback 5
    public const uint OIDRect = 0x603; // R1.5; 黃金毒瓦斯 1043: range 3+R width 2 rect, along the vent's facing
    public static readonly AOEShapeCircle Circle = new(2f);
    public static readonly AOEShapeRect Rect = new(4.5f, 1f);
}

// 噴口離開物件表範圍會被移除、回來再出生 ⇒ 每幀從模組維護的即時清單重建，不在出生時累積。
sealed class GoldBilePlume(BossModule module) : Components.GenericAOEs(module)
{
    private readonly List<Actor> _circles = module.Enemies(GoldBilePlumes.OIDCircle);
    private readonly List<Actor> _rects = module.Enemies(GoldBilePlumes.OIDRect);
    private readonly List<AOEInstance> _aoes = new(16);

    public override ReadOnlySpan<AOEInstance> ActiveAOEs(int slot, Actor actor) => CollectionsMarshal.AsSpan(_aoes);

    public override void Update()
    {
        _aoes.Clear();
        var count = _circles.Count;
        for (var i = 0; i < count; ++i)
        {
            var a = _circles[i];
            _aoes.Add(new(GoldBilePlumes.Circle, a.Position.Quantized(), a.Rotation));
        }
        count = _rects.Count;
        for (var i = 0; i < count; ++i)
        {
            var a = _rects[i];
            _aoes.Add(new(GoldBilePlumes.Rect, a.Position.Quantized(), a.Rotation));
        }
    }
}

// 首領戰以外（含開打前與首領之間的通道）噴口照樣會噴；首領戰中由各首領模組的 GoldBilePlume 負責，這裡不重複加。
[ZoneModuleInfo(BossModuleInfo.Maturity.Contributed, 5)]
public sealed class D12AurumVale(WorldState ws) : ZoneModule(ws)
{
    public override void CalculateAIHints(int playerSlot, Actor player, AIHints hints)
    {
        foreach (var a in World.Actors)
        {
            if (a.OID == GoldBilePlumes.OIDCircle)
            {
                hints.AddForbiddenZone(GoldBilePlumes.Circle, a.Position.Quantized(), a.Rotation);
            }
            else if (a.OID == GoldBilePlumes.OIDRect)
            {
                hints.AddForbiddenZone(GoldBilePlumes.Rect, a.Position.Quantized(), a.Rotation);
            }
        }
    }
}
