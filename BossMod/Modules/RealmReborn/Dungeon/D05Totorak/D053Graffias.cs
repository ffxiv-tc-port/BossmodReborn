namespace BossMod.RealmReborn.Dungeon.D05Totorak.D053Graffias;

public enum OID : uint
{
    Boss = 0x103, // x1
    FleshyPod = 0x23C, // spawn during fight
    Comesmite = 0x104, // spawn during fight
    GraffiasTail = 0x10A, // spawn during fight
    PollenZone = 0x1E8614 // spawn during fight
}

public enum AID : uint
{
    AutoAttack = 870, // Boss/Comesmite->player, no cast
    RealmShaker = 697, // Boss->self, no cast, range 9 raidwide around boss
    Silkscreen = 701, // Boss->self, 2.5s cast, range 18 width 4 rect aoe
    StickyWeb = 698, // Boss->player, 1.5s cast, visual (spawns fleshy pod at target)
    PodBurst = 730, // FleshyPod->self, 3.0s cast, range 7.050 aoe
    TailMolt = 704, // Boss->self, no cast, visual (spawns tail)
    DeadlyThrust = 702 // Boss->self, 2.0s cast, visual (spawns pollen zone)
}

class Silkscreen(BossModule module) : Components.SimpleAOEs(module, (uint)AID.Silkscreen, new AOEShapeRect(18, 2));
class StickyWeb(BossModule module) : Components.CastHint(module, (uint)AID.StickyWeb, "Delayed AOE at target");
class PodBurst(BossModule module) : Components.SimpleAOEs(module, (uint)AID.PodBurst, 7.05f);
class DeadlyThrust(BossModule module) : Components.CastHint(module, (uint)AID.DeadlyThrust, "Persistent voidzone at target");
class PollenZone(BossModule module) : Components.Voidzone(module, 10, m => m.Enemies(OID.PollenZone));

// 分隊號令：伴星蛛蠍黏著玩家或分隊補師滿 2 秒就要分隊去打它（讓分隊坦去拉）；黏最久的優先。玩家照打首領，所以不標 UrgentKill。
class SquadronComesmite(BossModule module) : BossComponent(module)
{
    public const double HoldSeconds = 2;
    private readonly Dictionary<ulong, DateTime> _onSoftSince = [];

    public override void Update()
    {
        var player = Raid.Player();
        foreach (var c in Module.Enemies((uint)OID.Comesmite))
        {
            if (c.IsDead || !IsSoftTarget(c.TargetID, player))
                _onSoftSince.Remove(c.InstanceID);
            else
                _onSoftSince.TryAdd(c.InstanceID, WorldState.CurrentTime);
        }
    }

    public override void OnActorDestroyed(Actor actor) => _onSoftSince.Remove(actor.InstanceID);

    public override void AddAIHints(int slot, Actor actor, PartyRolesConfig.Assignment assignment, AIHints hints)
    {
        AIHints.Enemy? longest = null;
        DateTime longestSince = default;
        foreach (var e in hints.PotentialTargets)
        {
            if (e.Actor.OID != (uint)OID.Comesmite || !_onSoftSince.TryGetValue(e.Actor.InstanceID, out var since) || (WorldState.CurrentTime - since).TotalSeconds < HoldSeconds)
                continue;
            e.SquadronFocus = 1;
            if (longest == null || since < longestSince)
            {
                longest = e;
                longestSince = since;
            }
        }
        if (longest != null)
            longest.SquadronFocus = 2;
    }

    private bool IsSoftTarget(ulong target, Actor? player)
        => target != 0 && (target == player?.InstanceID || WorldState.Actors.Find(target) is { Type: ActorType.Buddy, Role: Role.Healer });
}

class D053GraffiasStates : StateMachineBuilder
{
    public D053GraffiasStates(BossModule module) : base(module)
    {
        TrivialPhase()
            .ActivateOnEnter<Silkscreen>()
            .ActivateOnEnter<StickyWeb>()
            .ActivateOnEnter<PodBurst>()
            .ActivateOnEnter<DeadlyThrust>()
            .ActivateOnEnter<PollenZone>()
            .ActivateOnEnter<SquadronComesmite>();
    }
}

[ModuleInfo(BossModuleInfo.Maturity.Verified, GroupType = BossModuleInfo.GroupType.CFC, GroupID = 1, NameID = 444)]
public class D053Graffias(WorldState ws, Actor primary) : BossModule(ws, primary, new(215, -145), new ArenaBoundsCircle(20))
{
    protected override void CalculateModuleAIHints(int slot, Actor actor, PartyRolesConfig.Assignment assignment, AIHints hints)
    {
        var haveTail = Enemies(OID.GraffiasTail).Count > 0;
        foreach (var e in hints.PotentialTargets)
        {
            e.Priority = (OID)e.Actor.OID switch
            {
                OID.GraffiasTail => 2,
                OID.Comesmite => haveTail ? 2 : 1,
                OID.Boss => 1,
                _ => 0,
            };
        }
    }
}
