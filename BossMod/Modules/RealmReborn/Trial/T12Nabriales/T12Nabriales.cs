namespace BossMod.RealmReborn.Trial.T12Nabriales;

// 本檔假設與契約（只有一場錄影佐證，數字是近似）：
// - 第二階段首領被移走再重生（同一個實體 id、不可選取但在戰鬥中）⇒ 開打條件只看戰鬥中，重生後模組重新開打。
// - 第一、三階段場地在 (0,0)、第二階段在 (220,0)，都當成半徑 25 的圓；依錄影者所在位置切換中心。
public enum OID : uint
{
    Boss = 0xE41, // R1.0
    MeteorSource = 0x1B2, // 隕星的施放者（不可選取）
    DarkAetherSmall = 0xE43, // 暗乙太・壹
    DarkAetherLarge = 0xE44, // 暗乙太・貳
    DarkAetherMedium = 0xE45, // 暗乙太・三
    ShadowSprite = 0xE46, // 暗影元精
    GateOfDelay = 0xE47, // 延時之門
    DimensionalRift = 0xE48, // 次元裂縫
    MeteorMarker = 0x1E98A7, // 隕星落點（EventObj）
}

// 列舉以外的敵方讀條由 Components.UnclaimedCastAOEs 照沒有模組時的猜法畫。
public enum AID : uint
{
    SuperDarkWave = 3420, // Boss->self, no cast, raidwide
    DarkAetherBurstSmall = 3421, // DarkAetherSmall->self, no cast, range 5；碰到人才爆，刻意不處理：擋球是隊伍的工作，球走到中央首領會疊增傷
    DarkAetherBurstLarge = 3422, // DarkAetherLarge->self, no cast, range 10；同上
    DarkAetherBurstMedium = 3423, // DarkAetherMedium->self, no cast, range 8；同上
    MidDarkWave = 3424, // ShadowSprite->self, 2.2s cast, range 50 60-degree cone
    PoisonMist = 3426, // Boss->self, no cast, raidwide
    Attraction = 3427, // GateOfDelay->self, no cast, 吸引；不處理：吸到中央後傳送
    Meteor = 3429, // MeteorSource->self, no cast, 沒人擋就全隊大傷害
    Spark = 3432, // Boss->player, 2.7s cast, single-target
    ArmageddonFlame = 3434, // Boss->self, 2.2s cast, range 50 width 8 rect
    Quake = 3437, // Boss->self, 3.2s cast, raidwide
    GreatAttraction = 3502, // MeteorSource->self, no cast, 大吸引；不處理：第二階段結束的傳送
}

class MidDarkWave(BossModule module) : Components.SimpleAOEs(module, (uint)AID.MidDarkWave, new AOEShapeCone(50f, 30f.Degrees()));
class ArmageddonFlame(BossModule module) : Components.SimpleAOEs(module, (uint)AID.ArmageddonFlame, new AOEShapeRect(50f, 4f));
class Quake(BossModule module) : Components.RaidwideCast(module, (uint)AID.Quake);
class Spark(BossModule module) : Components.SingleTargetCast(module, (uint)AID.Spark);

// 隕星：落點物件出現後約 11.8 秒落下。2.4 碼內有人＝擋下（只擋的人受傷），3.4 碼外沒人＝全隊大傷害（擋的半徑近似）。
// 每顆只要一個人。落點每 6 秒一波、常同時有兩波：AI 挑「別人沒站上去、來得及走到」裡最先落下的那顆，
// 不用 GenericTowers：它把所有塔當同一個生效時間，會讓 AI 先去下一波的空落點、眼前這顆反而沒人擋。
sealed class Meteor(BossModule module) : BossComponent(module)
{
    private const float SoakRadius = 2f; // AI 要站進去的深度，比判定半徑多留餘裕
    private const float CoverRadius = 2.5f; // 別人站在這距離內就算已經有人擋
    private const float MoveSpeed = 6f;
    private readonly List<(WPos Position, DateTime Activation, ulong ActorID)> _meteors = [];

    public override void OnActorCreated(Actor actor)
    {
        if (actor.OID == (uint)OID.MeteorMarker)
            _meteors.Add((actor.Position, WorldState.FutureTime(11.8d), actor.InstanceID));
    }

    public override void OnActorDestroyed(Actor actor)
    {
        if (actor.OID == (uint)OID.MeteorMarker)
            _meteors.RemoveAll(m => m.ActorID == actor.InstanceID);
    }

    public override void OnEventCast(Actor caster, ActorCastEvent spell)
    {
        if (spell.Action.ID == (uint)AID.Meteor)
        {
            var impact = caster.Position;
            _meteors.RemoveAll(m => m.Position.InCircle(impact, 1f));
        }
    }

    private bool CoveredByOthers(WPos pos, Actor self)
    {
        var party = Raid.WithoutSlot();
        for (var i = 0; i < party.Length; ++i)
        {
            var p = party[i];
            if (p != self && p.Position.InCircle(pos, CoverRadius))
                return true;
        }
        return false;
    }

    private int Pick(Actor actor)
    {
        var count = _meteors.Count;
        for (var i = 0; i < count; ++i)
        {
            var m = _meteors[i];
            if (actor.Position.InCircle(m.Position, SoakRadius) && !CoveredByOthers(m.Position, actor))
                return i;
        }
        for (var i = 0; i < count; ++i)
        {
            var m = _meteors[i];
            if (CoveredByOthers(m.Position, actor))
                continue;
            var reach = (float)(m.Activation - WorldState.CurrentTime).TotalSeconds * MoveSpeed + SoakRadius;
            if ((m.Position - actor.Position).LengthSq() <= reach * reach)
                return i;
        }
        return -1;
    }

    public override void AddAIHints(int slot, Actor actor, PartyRolesConfig.Assignment assignment, AIHints hints)
    {
        var i = Pick(actor);
        if (i >= 0)
        {
            var m = _meteors[i];
            hints.AddForbiddenZone(ShapeDistance.InvertedCircle(m.Position, SoakRadius), m.Activation);
        }
    }

    public override void AddHints(int slot, Actor actor, TextHints hints)
    {
        var i = Pick(actor);
        if (i >= 0 && !actor.Position.InCircle(_meteors[i].Position, SoakRadius))
            hints.Add(Loc.T("Soak the tower!"));
    }

    public override void DrawArenaForeground(int pcSlot, Actor pc)
    {
        var pick = Pick(pc);
        var count = _meteors.Count;
        for (var i = 0; i < count; ++i)
            Arena.AddCircle(_meteors[i].Position, CoverRadius, i == pick ? Colors.Safe : default, 2f);
    }
}

class T12NabrialesStates : StateMachineBuilder
{
    public T12NabrialesStates(BossModule module) : base(module)
    {
        TrivialPhase()
            .ActivateOnEnter<MidDarkWave>()
            .ActivateOnEnter<ArmageddonFlame>()
            .ActivateOnEnter<Quake>()
            .ActivateOnEnter<Spark>()
            .ActivateOnEnter<Meteor>()
            .ActivateOnEnter<Components.UnclaimedCastAOEs>();
    }
}

[ModuleInfo(BossModuleInfo.Maturity.WIP, Contributors = "ffxiv-tc-port", GroupType = BossModuleInfo.GroupType.CFC, GroupID = 84, NameID = 3287)]
public class T12Nabriales(WorldState ws, Actor primary) : BossModule(ws, primary, CenterFor(primary.Position), new ArenaBoundsCircle(25f))
{
    private static readonly WPos MainCenter = new(0f, 0f);
    private static readonly WPos RiftCenter = new(220f, 0f);

    private static WPos CenterFor(WPos pos) => pos.X > 110f ? RiftCenter : MainCenter;

    protected override bool CheckPull() => PrimaryActor.InCombat;

    protected override void UpdateModule()
    {
        if (Raid.Player() is { } p)
        {
            var c = CenterFor(p.Position);
            if (Arena.Center != c)
                Arena.Center = c;
        }
    }
}
