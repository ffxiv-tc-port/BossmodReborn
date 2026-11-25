namespace BossMod.Endwalker.Raid.P2NHippokampos;

// 契約：列舉＝模組知道的技能；列舉以外的敵方讀條由 Components.UnclaimedCastAOEs 照沒有模組時的猜法畫。
public enum OID : uint
{
    Boss = 0x3599, // R9.002
    Helper = 0x233C,
    CataractHead = 0x359A, // R8.002
    DissociatedHead = 0x3869, // R9.002
}

public enum AID : uint
{
    AutoAttack = 872, // Boss->player, no cast, single-target
    SewageDeluge = 26614, // Boss->self, 5.0s cast, range 60 circle, raidwide
    SpokenCataractHead3 = 26615, // CataractHead->self, 7.0s cast, range 60 180-degree cone
    SpokenCataractHead1 = 26616, // CataractHead->self, 7.0s cast, range 60 180-degree cone
    SpokenCataractHead2 = 26617, // CataractHead->self, 7.0s cast, range 60 180-degree cone
    SpokenCataractBoss = 26621, // Boss->self, 7.0s cast, range 100 width 15 rect
    SpokenCataractBoss2 = 26622, // Boss->self, 7.0s cast, range 100 width 15 rect
    CoherenceFlare = 26624, // CataractHead->location, no cast, range 60 circle with falloff
    Coherence = 26625, // Boss->self, 5.0s cast, single-target
    CoherenceRay = 26626, // Boss->self, no cast, range 60 width 6 rect, shared
    CoherenceEnd = 26627, // Boss->self, no cast, single-target
    Dissociation = 26628, // Boss->self, 4.0s cast, single-target
    DissociationEnd = 26629, // Boss->self, no cast, single-target
    DissociationAOE = 26630, // DissociatedHead->self, 8.0s cast, range 50 width 20 rect
    Shockwave = 26631, // Boss->location, 6.0s cast, range 60 circle, knockback
    TaintedFlood = 26632, // Boss->self, 3.0s cast, single-target
    TaintedFloodAOE = 26633, // Helper->players, 5.2s cast, range 6 circle spread
    PredatorySight = 26634, // Boss->self, 4.0s cast, single-target
    PredatorySightMark = 26635, // Helper->player, 5.0s cast, single-target, pair up
    SewageEruptionVisual = 26636, // Boss->self, 4.0s cast, single-target
    SewageEruption = 26637, // Helper->location, 4.0s cast, range 6 circle
    DoubledImpact = 26638, // Boss->players, 5.0s cast, range 6 circle, shared tankbuster
    MurkyDepths = 26639, // Boss->self, 5.0s cast, range 60 circle, raidwide
    Teleport = 26677, // Boss->location, no cast, single-target
    CoherenceRayTarget = 27924, // Helper->player, no cast, single-target
}

public enum IconID : uint
{
    PredatorySight = 55, // player
    CoherenceFlare = 87, // player
    DoubledImpact = 259, // player
    TaintedFlood = 310, // player
}

sealed class SpokenCataractHead(BossModule module) : Components.SimpleAOEGroups(module, [(uint)AID.SpokenCataractHead1, (uint)AID.SpokenCataractHead2, (uint)AID.SpokenCataractHead3], new AOEShapeCone(60f, 90f.Degrees()));
// 讀條的落點在首領背後 50 碼、朝向首領，所以長 100 的直線從落點起算＝穿過首領前後各 50 碼。
sealed class SpokenCataractBoss(BossModule module) : Components.SimpleAOEGroups(module, [(uint)AID.SpokenCataractBoss, (uint)AID.SpokenCataractBoss2], new AOEShapeRect(100f, 7.5f));
sealed class DissociationAOE(BossModule module) : Components.SimpleAOEs(module, (uint)AID.DissociationAOE, new AOEShapeRect(50f, 10f));
sealed class TaintedFlood(BossModule module) : Components.SpreadFromCastTargets(module, (uint)AID.TaintedFloodAOE, 6f);
sealed class DoubledImpact(BossModule module) : Components.CastSharedTankbuster(module, (uint)AID.DoubledImpact, 6f);
sealed class SewageEruption(BossModule module) : Components.SimpleAOEs(module, (uint)AID.SewageEruption, 6f);
sealed class SewageDeluge(BossModule module) : Components.RaidwideCast(module, (uint)AID.SewageDeluge);
sealed class MurkyDepths(BossModule module) : Components.RaidwideCast(module, (uint)AID.MurkyDepths);

// 距離衰減的核爆，半徑是估計值。
sealed class CoherenceFlare(BossModule module) : Components.SpreadFromIcon(module, (uint)IconID.CoherenceFlare, (uint)AID.CoherenceFlare, 15f, 5.8d)
{
    public override void OnEventCast(Actor caster, ActorCastEvent spell)
    {
        if (spell.Action.ID == (uint)AID.CoherenceFlare)
        {
            Spreads.Clear();
        }
    }
}

// 核爆目標不可進直線分攤。
sealed class CoherenceRay(BossModule module) : Components.LineStack(module, aidMarker: (uint)AID.CoherenceRayTarget, (uint)AID.CoherenceRay, 8.3d, 60f, 3f)
{
    private ulong _flareTarget;

    public override void OnEventIcon(Actor actor, uint iconID, ulong targetID)
    {
        if (iconID == (uint)IconID.CoherenceFlare)
        {
            _flareTarget = actor.InstanceID;
        }
    }

    public override void OnEventCast(Actor caster, ActorCastEvent spell)
    {
        base.OnEventCast(caster, spell);
        if (spell.Action.ID == AidMarker && Raid.FindSlot(_flareTarget) is var slot && slot >= 0)
        {
            var baits = CollectionsMarshal.AsSpan(CurrentBaits);
            for (var i = 0; i < baits.Length; ++i)
            {
                baits[i].Forbidden.Set(slot);
            }
        }
    }
}

// 全員被標記，每人至少和另一人站在一起才不會吃易傷。
sealed class PredatorySight(BossModule module) : Components.StackTogether(module, (uint)IconID.PredatorySight, 4.9f, 3f);

// 擊退距離照錄影量（落地前站定的人離原點多了 13.0 碼）。
// 擊退途中穿過水區也會中水毒（錄影：落點在角落方塊內的人仍在半途中毒），所以整條擊退路徑都要在場內。
sealed class Shockwave(BossModule module) : Components.SimpleKnockbacks(module, (uint)AID.Shockwave, KnockDistance)
{
    private const float KnockDistance = 13f;
    private const int PathSamples = 8;

    public override void AddAIHints(int slot, Actor actor, PartyRolesConfig.Assignment assignment, AIHints hints)
    {
        if (Casters.Count == 0)
        {
            return;
        }
        ref var c = ref Casters.Ref(0);
        var act = c.Activation;
        if (IsImmune(slot, act))
        {
            return;
        }
        var origin = c.Origin;
        var bounds = Arena.Bounds;
        var center = Arena.Center;
        hints.AddForbiddenZone(p =>
        {
            var dir = p != origin ? (p - origin).Normalized() : default;
            for (var i = 1; i <= PathSamples; ++i)
            {
                if (!bounds.Contains(p + KnockDistance * i / PathSamples * dir - center))
                {
                    return default;
                }
            }
            return 1f;
        }, act);
    }
}

// 污水氾濫：場地狀態 0 號 0x00200010（預告，還不傷人）起，只剩四角方塊與相連的通道站得住，約 8 秒後（0x00020001）中央與外圍踩到會中水毒；0x00080004／0x00040004 復原。
// 水區另外當成危險區交給 AI：只換場地邊界時，人已經站在場外又沒有其他危險區的話尋路不會把人帶回場內。
sealed class SewageDelugeArena(BossModule module) : Components.GenericAOEs(module)
{
    private static readonly Rectangle[] _dry = BuildDry();
    private static readonly ArenaBoundsComplex _flooded = new(_dry);
    private static readonly AOEShapeCustom _water = new([new Rectangle(P2NHippokampos.ArenaCenter, 17.5f, 22.5f)], _dry);
    private AOEInstance[] _aoe = [];

    private static Rectangle[] BuildDry()
    {
        static Rectangle rect(float x, float z, float hx, float hz) => new(new(100f + x, 100f + z), hx, hz);
        return [
            rect(9.625f, 9.625f, 4.375f, 4.375f), rect(-9.625f, 9.625f, 4.375f, 4.375f), rect(9.625f, -9.625f, 4.375f, 4.375f), rect(-9.625f, -9.625f, 4.375f, 4.375f),
            rect(default, 9.5f, 5.5f, 2f), rect(default, -9.5f, 5.5f, 2f), rect(9.5f, default, 2f, 5.5f), rect(-9.5f, default, 2f, 5.5f)
        ];
    }

    public override ReadOnlySpan<AOEInstance> ActiveAOEs(int slot, Actor actor) => _aoe;

    public override void OnEventEnvControl(byte index, uint state)
    {
        if (index != 0)
        {
            return;
        }
        switch (state)
        {
            case 0x00200010u:
                Arena.Bounds = _flooded;
                Arena.Center = _flooded.Center;
                _aoe = [new(_water, P2NHippokampos.ArenaCenter, default, WorldState.FutureTime(8d))];
                break;
            case 0x00080004u:
            case 0x00040004u:
                Arena.Bounds = P2NHippokampos.DefaultBounds;
                Arena.Center = P2NHippokampos.ArenaCenter;
                _aoe = [];
                break;
        }
    }
}

sealed class P2NHippokamposStates : StateMachineBuilder
{
    public P2NHippokamposStates(BossModule module) : base(module)
    {
        TrivialPhase()
            .ActivateOnEnter<SpokenCataractHead>()
            .ActivateOnEnter<SpokenCataractBoss>()
            .ActivateOnEnter<DissociationAOE>()
            .ActivateOnEnter<TaintedFlood>()
            .ActivateOnEnter<DoubledImpact>()
            .ActivateOnEnter<CoherenceFlare>()
            .ActivateOnEnter<CoherenceRay>()
            .ActivateOnEnter<SewageDeluge>()
            .ActivateOnEnter<PredatorySight>()
            .ActivateOnEnter<Shockwave>()
            .ActivateOnEnter<SewageEruption>()
            .ActivateOnEnter<SewageDelugeArena>()
            .ActivateOnEnter<MurkyDepths>()
            .ActivateOnEnter<Components.UnclaimedCastAOEs>();
    }
}

[ModuleInfo(BossModuleInfo.Maturity.WIP, Contributors = "ffxiv-tc-port", GroupType = BossModuleInfo.GroupType.CFC, GroupID = 810u, NameID = 10348u)]
public sealed class P2NHippokampos(WorldState ws, Actor primary) : BossModule(ws, primary, ArenaCenter, DefaultBounds)
{
    public static readonly WPos ArenaCenter = new(100f, 100f);
    public static readonly ArenaBoundsRect DefaultBounds = new(17.5f, 22.5f);
}
