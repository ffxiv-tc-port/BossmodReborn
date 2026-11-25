namespace BossMod.RealmReborn.Dungeon.D10StoneVigil;

// 通道牆上的小雙足飛龍（不可選取、位置與面向固定）：播放動作 7749 之後約 2 秒無讀條「熾熱彌漫」，沿自身面向的直線（長 15+R、寬 11）。
// 動作一出就畫、帶生效時間；噴過、死亡或消失就收掉，沒收到判定也在逾時後收掉。只在沒有首領模組啟用時生效（ZoneModule 契約）。
[ZoneModuleInfo(BossModuleInfo.Maturity.Contributed, 11)]
public sealed class D10StoneVigil : ZoneModule
{
    private const uint WyvernOID = 0x5B4; // R2.88
    private const uint BreathAID = 1021;
    private const ushort BreathWindupTimeline = 7749;

    private static readonly AOEShapeRect _breath = new(17.88f, 5.5f);
    private static readonly TimeSpan _breathDelay = TimeSpan.FromSeconds(2d);
    private static readonly TimeSpan _staleAfter = TimeSpan.FromSeconds(1.5d);

    private readonly Dictionary<ulong, DateTime> _pendingBreaths = [];
    private readonly List<ulong> _expired = [];
    private readonly EventSubscriptions _subscriptions;

    public D10StoneVigil(WorldState ws) : base(ws)
    {
        _subscriptions = new(
            ws.Actors.PlayActionTimelineEvent.Subscribe(OnPlayActionTimeline),
            ws.Actors.Removed.Subscribe(actor => _pendingBreaths.Remove(actor.InstanceID)),
            ws.Actors.CastEvent.Subscribe(OnCastEvent)
        );
    }

    protected override void Dispose(bool disposing)
    {
        _subscriptions.Dispose();
        base.Dispose(disposing);
    }

    private void OnPlayActionTimeline(Actor actor, ushort id)
    {
        if (actor.OID == WyvernOID && id == BreathWindupTimeline)
            _pendingBreaths[actor.InstanceID] = World.CurrentTime + _breathDelay;
    }

    private void OnCastEvent(Actor caster, ActorCastEvent ev)
    {
        if (caster.OID == WyvernOID && ev.Action.ID == BreathAID)
            _pendingBreaths.Remove(caster.InstanceID);
    }

    public override void Update()
    {
        if (_pendingBreaths.Count == 0)
            return;
        var now = World.CurrentTime;
        _expired.Clear();
        foreach (var (id, activation) in _pendingBreaths)
            if (now > activation + _staleAfter)
                _expired.Add(id);
        foreach (var id in _expired)
            _pendingBreaths.Remove(id);
    }

    public override void CalculateAIHints(int playerSlot, Actor player, AIHints hints)
    {
        foreach (var (id, activation) in _pendingBreaths)
        {
            if (World.Actors.Find(id) is not { IsDead: false } wyvern)
                continue;
            hints.AddForbiddenZone(_breath, wyvern.Position.Quantized(), wyvern.Rotation, activation, id);
        }
    }
}
