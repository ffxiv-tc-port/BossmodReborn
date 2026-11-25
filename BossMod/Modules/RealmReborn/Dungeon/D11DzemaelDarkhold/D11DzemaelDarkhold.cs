namespace BossMod.RealmReborn.Dungeon.D11DzemaelDarkhold;

// 走廊遊蕩的全視之眼（0x604，無敵、會移動）：無讀條、沒有前兆，每 10.0 秒放一次「凶眼注目」（以自身為中心半徑 20+R）。
// 水晶附近有「水晶紗帳」（受到的傷害大幅降低）⇒ 只在下一發前 3 秒畫，水晶 6 碼內不算禁區。
// 第一次看到它出手才開始推算（進入視野的時間不等於它計時的起點）；錯過的那幾發照 10 秒往後推。只在沒有首領模組啟用時生效（ZoneModule 契約）。
[ZoneModuleInfo(BossModuleInfo.Maturity.Contributed, 13)]
public sealed class D11DzemaelDarkhold : ZoneModule
{
    private const uint EyeOID = 0x604; // R2.7
    private const uint CrystalOID = 0x1E8594;
    private const uint EvilEyeAID = 1114;
    private const float EyeRadius = 23.2f; // 20 + R 2.7 + 0.5 餘裕；錄影 24 碼外全沒中
    private const float VeilRadius = 6f; // 狀態得到時離水晶 p10 5.6、中位 7.9 碼，取保守值
    private const float HoldOverrideHpRatio = 0.5f; // 沒有紗帳時一發約最大 HP 的 43%
    private static readonly TimeSpan _period = TimeSpan.FromSeconds(10d);
    private static readonly TimeSpan _lead = TimeSpan.FromSeconds(3d);
    private static readonly TimeSpan _grace = TimeSpan.FromSeconds(0.5d);

    private readonly Dictionary<ulong, DateTime> _nextPulse = [];
    private readonly List<Func<WPos, float>> _crystals = [];
    private readonly EventSubscriptions _subscriptions;

    public D11DzemaelDarkhold(WorldState ws) : base(ws)
    {
        _subscriptions = new(
            ws.Actors.Removed.Subscribe(actor => _nextPulse.Remove(actor.InstanceID)),
            ws.Actors.CastEvent.Subscribe(OnCastEvent)
        );
    }

    protected override void Dispose(bool disposing)
    {
        _subscriptions.Dispose();
        base.Dispose(disposing);
    }

    private void OnCastEvent(Actor caster, ActorCastEvent ev)
    {
        if (caster.OID == EyeOID && ev.Action.ID == EvilEyeAID)
            _nextPulse[caster.InstanceID] = World.CurrentTime + _period;
    }

    public override void CalculateAIHints(int playerSlot, Actor player, AIHints hints)
    {
        if (_nextPulse.Count == 0)
            return;
        // 別的插件要求原地站定（例：AutoDuty 站魔導終端等點亮）時不畫：一發約 43% 血就會壓到站定租約的血量門檻以下，
        // 畫了會在下一發前把人拉離終端、整段重做；只在血量低到下一發可能致命時才照畫。
        if (ExternalMovementHold.HasLeases && player.HPMP.MaxHP > 0 && player.HPRatio > HoldOverrideHpRatio)
            return;
        var now = World.CurrentTime;
        var crystalsCollected = false;
        foreach (var (id, last) in _nextPulse)
        {
            var next = last;
            while (next + _grace < now)
                next += _period;
            if (now < next - _lead || World.Actors.Find(id) is not { IsDead: false } eye)
                continue;
            if (!crystalsCollected)
            {
                CollectCrystals();
                crystalsCollected = true;
            }
            var pulse = ShapeDistance.Circle(eye.Position.Quantized(), EyeRadius);
            if (_crystals.Count != 0)
            {
                Func<WPos, float>[] outsideVeil = [.. _crystals];
                pulse = ShapeDistance.Intersection(new[] { pulse, ShapeDistance.InvertedUnion(outsideVeil) });
            }
            hints.AddForbiddenZone(pulse, next, id);
        }
    }

    private void CollectCrystals()
    {
        _crystals.Clear();
        foreach (var a in World.Actors)
            if (a.OID == CrystalOID)
                _crystals.Add(ShapeDistance.Circle(a.Position, VeilRadius));
    }
}
