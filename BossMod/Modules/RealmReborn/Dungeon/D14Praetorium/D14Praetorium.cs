namespace BossMod.RealmReborn.Dungeon.D14Praetorium;

// 走廊的「究極武器原型機械臂」出生後最快 1.75 秒無讀條「橫掃」，掃左側（29185）或右側（29186），出生時分不出哪一側 ⇒ 兩側一起畫。
// 形狀＝以機械臂面向的左右正側為中心、半角 65°、半徑 10 的兩片扇形，加上中心 2 碼內（正前、正後 25° 以內的 2～10 碼與 10 碼外安全）。
// 揮過、死亡或消失就收掉；只在沒有首領模組啟用時生效（ZoneModule 契約）。
[ZoneModuleInfo(BossModuleInfo.Maturity.Contributed, 16)]
public sealed class D14Praetorium : ZoneModule
{
    private const uint PrototypeArmOID = 0x38E6;
    private const uint SweepLeftAID = 29185;
    private const uint SweepRightAID = 29186;

    private static readonly AOEShapeCone _sweepLeft = new(10f, 65f.Degrees(), -90f.Degrees());
    private static readonly AOEShapeCone _sweepRight = new(10f, 65f.Degrees(), 90f.Degrees());
    private static readonly AOEShapeCircle _sweepCore = new(2f);
    private static readonly TimeSpan _sweepDelay = TimeSpan.FromSeconds(1.75d);

    private readonly Dictionary<ulong, DateTime> _pendingSweeps = [];
    private readonly EventSubscriptions _subscriptions;

    public D14Praetorium(WorldState ws) : base(ws)
    {
        _subscriptions = new(
            ws.Actors.Added.Subscribe(OnActorAdded),
            ws.Actors.Removed.Subscribe(actor => _pendingSweeps.Remove(actor.InstanceID)),
            ws.Actors.CastEvent.Subscribe(OnCastEvent)
        );
    }

    protected override void Dispose(bool disposing)
    {
        _subscriptions.Dispose();
        base.Dispose(disposing);
    }

    private void OnActorAdded(Actor actor)
    {
        if (actor.OID == PrototypeArmOID)
            _pendingSweeps[actor.InstanceID] = World.CurrentTime + _sweepDelay;
    }

    private void OnCastEvent(Actor caster, ActorCastEvent ev)
    {
        if (caster.OID == PrototypeArmOID && ev.Action.ID is SweepLeftAID or SweepRightAID)
            _pendingSweeps.Remove(caster.InstanceID);
    }

    public override void CalculateAIHints(int playerSlot, Actor player, AIHints hints)
    {
        foreach (var (id, activation) in _pendingSweeps)
        {
            if (World.Actors.Find(id) is not { IsDead: false } arm)
                continue;
            hints.AddForbiddenZone(_sweepLeft, arm.Position, arm.Rotation, activation, id);
            hints.AddForbiddenZone(_sweepRight, arm.Position, arm.Rotation, activation, id);
            hints.AddForbiddenZone(_sweepCore, arm.Position, default, activation, id);
        }
    }
}
