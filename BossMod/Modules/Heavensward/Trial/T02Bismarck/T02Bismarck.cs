namespace BossMod.Heavensward.Trial.T02Bismarck;

// 本檔的假設與契約：
// - 首領本體（俾斯麥）整場不可選取；可打的是各階段的小怪、角質甲殼與核心。
// - 對龍弩砲連線到首領期間，鯨背（甲殼／核心所在處）併入場地；連線消失就回到甲板。
// - 魔導結界產生器與對龍弩砲只提示、不代按（要搶時機的場地裝置留給使用者）。
public enum OID : uint
{
    Boss = 0xEF8, // R6.000，不可選取
    Helper = 0xEF9, // R0.500
    VaporBubble = 0xEFA, // R1.200
    LanMaiiVundu = 0xEFC, // R1.500
    VukMaiiVundu = 0xEFD, // R1.300
    SoSanuwa = 0xEFE, // R3.500
    FunnelCloud = 0xEFF, // R1.000
    UlSanuwa = 0xF00, // R3.500
    MagitekFieldGenerator = 0xF60, // R1.000
    ChitinCarapace = 0x1147, // R5.000, Part
    Corona = 0x1148, // R5.000, Part
    Dragonkiller = 0x12EB, // R1.000
    HelperB = 0x1214, // R0.500
    HelperC = 0x1303, // R1.000
    FieldGeneratorSwitch = 0x1E9A25, // EventObj
    DragonkillerNorth = 0x1E9A28, // EventObj
    DragonkillerSouth = 0x1E9A29, // EventObj
}

public enum AID : uint
{
    AutoAttackAdd = 872, // LanMaiiVundu/SoSanuwa/UlSanuwa->player, no cast, single-target
    BaleenBombVisual = 4011, // Boss->self, 3.0s cast, single-target
    LightningBoltVisual = 4016, // Boss->self, 3.0s cast, single-target
    MagickedBubble = 4017, // Boss->self, no cast, single-target
    WhaleSong = 4019, // Boss->self, no cast, single-target
    Windcaller = 4021, // Boss->self, 3.0s cast, single-target
    WaterCannon = 4024, // VaporBubble->self, no cast, range 3 circle
    Morrowmotes = 4026, // LanMaiiVundu->self, no cast, single-target
    PowerfulGust = 4029, // VukMaiiVundu->player, 1.0s cast, single-target
    DryFin = 4031, // SoSanuwa->self, no cast, range 9+R cone
    WetFin = 4033, // UlSanuwa->self, no cast, range 9+R cone
    Shrug = 4038, // Helper->player, no cast, single-target
    BismarckHit4039 = 4039, // Boss->self, no cast, single-target
    BismarckHit4040 = 4040, // Boss->self, no cast, single-target
    BismarckHit4041 = 4041, // Boss->self, no cast, single-target
    HardsilverChain = 4042, // Dragonkiller->self, no cast, single-target
    FunnelCloudHit = 4765, // Helper->self, no cast, range 4 circle
    GeneratorHit = 4778, // MagitekFieldGenerator->self, no cast, single-target
    ThunderheadVisual = 4906, // Boss->self, 3.0s cast, single-target
    CetaceanRage = 4918, // Boss->self, 6.0s cast, range 50 circle
    BreachBlast = 4925, // Boss->self, no cast, range 50 circle
    HowlingWing = 4928, // LanMaiiVundu->self, 2.5s cast, range 30 width 6 rect
    ExtremeWind = 4929, // VukMaiiVundu->location, 2.5s cast, range 5 circle
    BaleenBomb = 4932, // Helper->location, 3.5s cast, range 5 circle
    VacuumWave = 4933, // Helper->self, no cast, range 100 circle
    AtmosphericDisruption = 4934, // Helper->self, no cast
    Thunderhead = 4935, // Helper->player, 3.0s cast, range 5 circle
    LightningBolt = 4936, // Helper->player, 4.0s cast, range 4 circle
    BreachBlastRepeat = 5062, // Helper->self, no cast, range 50 circle
    AtmosphericDisruptionLand = 5064, // Helper->player, no cast, single-target
}

public enum SID : uint
{
    VulnerabilityDown = 350, // none->SoSanuwa/UlSanuwa
    Whaleback = 719, // none->player
}

public enum TetherID : uint
{
    SanuwaLink = 35, // SoSanuwa<->UlSanuwa
    DragonkillerA = 39, // Dragonkiller->Boss
    DragonkillerB = 40, // Dragonkiller->Boss
}

class BaleenBomb(BossModule module) : Components.SimpleAOEs(module, (uint)AID.BaleenBomb, 5f);
// 判定落在目標「當下」的位置（讀條開始後跑開的人會把範圍帶走），所以當散開處理、不畫讀條開始時的落點。
class LightningBolt(BossModule module) : Components.SpreadFromCastTargets(module, (uint)AID.LightningBolt, 4f);
class Thunderhead(BossModule module) : Components.SpreadFromCastTargets(module, (uint)AID.Thunderhead, 5f);
class HowlingWing(BossModule module) : Components.SimpleAOEs(module, (uint)AID.HowlingWing, new AOEShapeRect(30f, 3f));
class ExtremeWind(BossModule module) : Components.SimpleAOEs(module, (uint)AID.ExtremeWind, 5f);
class CetaceanRage(BossModule module) : Components.RaidwideCast(module, (uint)AID.CetaceanRage);
class DryFin(BossModule module) : Components.Cleave(module, (uint)AID.DryFin, new AOEShapeCone(12.5f, 45f.Degrees()), [(uint)OID.SoSanuwa]);
class WetFin(BossModule module) : Components.Cleave(module, (uint)AID.WetFin, new AOEShapeCone(12.5f, 45f.Degrees()), [(uint)OID.UlSanuwa]);
class FunnelCloud(BossModule module) : Components.Voidzone(module, 4.5f, m => m.Enemies((uint)OID.FunnelCloud).Where(c => !c.IsDead));

// 兩隻騰雲蛇靠太近會連線並互給受傷減輕；拉著其中一隻的坦克要離另一隻 MinDistance 以上。
sealed class SanuwaSeparation(BossModule module) : BossComponent(module)
{
    private const float MinDistance = 15f;

    private Actor? Dry => Module.Enemies((uint)OID.SoSanuwa).FirstOrDefault(a => !a.IsDead);
    private Actor? Wet => Module.Enemies((uint)OID.UlSanuwa).FirstOrDefault(a => !a.IsDead);

    private static bool Linked(Actor dry, Actor wet)
        => dry.Tether.ID == (uint)TetherID.SanuwaLink || wet.Tether.ID == (uint)TetherID.SanuwaLink
        || dry.FindStatus((uint)SID.VulnerabilityDown) != null || wet.FindStatus((uint)SID.VulnerabilityDown) != null;

    private Actor? OtherSanuwa(Actor tank)
    {
        if (Dry is not { } dry || Wet is not { } wet)
            return null;
        return dry.TargetID == tank.InstanceID ? wet : wet.TargetID == tank.InstanceID ? dry : null;
    }

    public override void AddHints(int slot, Actor actor, TextHints hints)
    {
        if (OtherSanuwa(actor) is { } other && Dry is { } dry && Wet is { } wet && (Linked(dry, wet) || dry.Position.InCircle(wet.Position, MinDistance)))
            hints.Add(Loc.T("Pull the Sanuwas apart!"));
    }

    public override void AddGlobalHints(GlobalHints hints)
    {
        if (Dry is { } dry && Wet is { } wet)
            hints.Add(Linked(dry, wet) ? Loc.T("Sanuwas linked: they take less damage!") : Loc.T("Keep the Sanuwas apart"));
    }

    public override void AddAIHints(int slot, Actor actor, PartyRolesConfig.Assignment assignment, AIHints hints)
    {
        if (OtherSanuwa(actor) is not { } other)
            return;
        hints.AddForbiddenZone(ShapeDistance.Circle(other.Position, MinDistance));
        if (hints.FindEnemy(other) is { } e)
        {
            if (e.Priority > AIHints.Enemy.PriorityUndesirable)
                e.Priority = AIHints.Enemy.PriorityUndesirable;
            e.ShouldBeTanked = false;
        }
    }

    public override void DrawArenaForeground(int pcSlot, Actor pc)
    {
        if (OtherSanuwa(pc) is { } other)
            Arena.AddCircle(other.Position, MinDistance, Colors.Danger);
    }
}

// 甲殼／核心可選取時只有站在鯨背上（有鯨背狀態）的人打得到。
sealed class Whaleback(BossModule module) : BossComponent(module)
{
    private static readonly WPos _backFrom = new(1.5f, -4.5f), _backTo = new(8.5f, -4.5f);
    private const float BackHalfWidth = 2.5f;

    private bool ShellUp()
    {
        var shells = Module.Enemies([(uint)OID.ChitinCarapace, (uint)OID.Corona]);
        for (var i = 0; i < shells.Count; ++i)
            if (shells[i].IsTargetable && !shells[i].IsDead)
                return true;
        return false;
    }

    public override void AddHints(int slot, Actor actor, TextHints hints)
    {
        if (ShellUp() && actor.FindStatus((uint)SID.Whaleback) == null)
            hints.Add(Loc.T("Get on Bismarck's back to attack it!"));
    }

    public override void AddAIHints(int slot, Actor actor, PartyRolesConfig.Assignment assignment, AIHints hints)
    {
        if (ShellUp() && actor.FindStatus((uint)SID.Whaleback) == null)
            hints.AddForbiddenZone(ShapeDistance.InvertedRect(_backFrom, _backTo, BackHalfWidth));
    }
}

// 刻意只提示不代按：產生器要對準白鯨之怒詠唱的時機，弩砲兩座要兩個人各按一座。
sealed class Interactables(BossModule module) : BossComponent(module)
{
    private bool Usable(uint oid)
    {
        var list = Module.Enemies(oid);
        for (var i = 0; i < list.Count; ++i)
            if (list[i].IsTargetable)
                return true;
        return false;
    }

    public override void AddGlobalHints(GlobalHints hints)
    {
        if (Usable((uint)OID.FieldGeneratorSwitch))
            hints.Add(Module.PrimaryActor.CastInfo?.IsSpell(AID.CetaceanRage) ?? false
                ? Loc.T("Use the field generator now!")
                : Loc.T("Field generator: use it while Bismarck casts Cetacean Rage"));
        if (Usable((uint)OID.DragonkillerNorth) || Usable((uint)OID.DragonkillerSouth))
            hints.Add(Loc.T("Fire the Dragonkiller cannons!"));
    }
}

sealed class DeckArena(BossModule module) : BossComponent(module)
{
    private static readonly WPos[] _deck = [
        new(-17.5f, -18f), new(-12.5f, -18f), new(-11.5f, -14.5f), new(-10f, -13.5f), new(-8f, -12.5f), new(-4.5f, -11.5f), new(-4f, -7.5f), new(-3.5f, -6f),
        new(-0.5f, -3.5f), new(-0.5f, -1.5f), new(-2.5f, default), new(-3.5f, 1.5f), new(-1f, 4.5f), new(default, 6.5f), new(-2f, 10f), new(-8f, 12f),
        new(-10f, 14f), new(-11.5f, 14.5f), new(-11.5f, 18.5f), new(-17f, 19.5f), new(-19f, 18f), new(-22f, 16f), new(-24f, 13.5f), new(-26.5f, 11f),
        new(-27f, 7.5f), new(-28f, 5f), new(-28f, 1.5f), new(-26.5f, default), new(-26.5f, -7f), new(-25.5f, -11.5f), new(-21f, -12.5f), new(-20.5f, -14.5f),
        new(-19f, -15.5f)];
    // 鯨背：東緣與南側走上去的坡比上游的矩形各放寬一點，免得人還在坡上就被當成場外。
    private static readonly WPos[] _shell = [new(-6f, -10.5f), new(10f, -10.5f), new(10f, 2.5f), new(-6f, 2.5f)];

    public static readonly ArenaBoundsComplex DeckBounds = new([new PolygonCustom(_deck)]);
    public static readonly ArenaBoundsComplex ShellBounds = new([new PolygonCustom(_deck), new PolygonCustom(_shell)]);

    private bool _shellUp;

    public override void Update()
    {
        var shellUp = false;
        var cannons = Module.Enemies((uint)OID.Dragonkiller);
        for (var i = 0; i < cannons.Count; ++i)
            if (cannons[i].Tether.ID != 0)
                shellUp = true;
        if (shellUp == _shellUp)
            return;
        _shellUp = shellUp;
        var bounds = shellUp ? ShellBounds : DeckBounds;
        Arena.Bounds = bounds;
        Arena.Center = bounds.Center;
    }
}

class BismarckAdds(BossModule module) : Components.AddsMulti(module, [(uint)OID.LanMaiiVundu, (uint)OID.VukMaiiVundu, (uint)OID.SoSanuwa, (uint)OID.UlSanuwa, (uint)OID.VaporBubble, (uint)OID.ChitinCarapace, (uint)OID.Corona], 1);

// 狀態機不排時間軸：每個元件都由事件驅動，整場掛著不會卡在某個預期時間點。
class T02BismarckStates : StateMachineBuilder
{
    public T02BismarckStates(BossModule module) : base(module)
    {
        TrivialPhase()
            .ActivateOnEnter<DeckArena>()
            .ActivateOnEnter<BaleenBomb>()
            .ActivateOnEnter<LightningBolt>()
            .ActivateOnEnter<Thunderhead>()
            .ActivateOnEnter<HowlingWing>()
            .ActivateOnEnter<ExtremeWind>()
            .ActivateOnEnter<CetaceanRage>()
            .ActivateOnEnter<DryFin>()
            .ActivateOnEnter<WetFin>()
            .ActivateOnEnter<SanuwaSeparation>()
            .ActivateOnEnter<FunnelCloud>()
            .ActivateOnEnter<Interactables>()
            .ActivateOnEnter<Whaleback>()
            .ActivateOnEnter<BismarckAdds>()
            .ActivateOnEnter<Components.UnclaimedCastAOEs>();
    }
}

[ModuleInfo(BossModuleInfo.Maturity.Contributed, Contributors = "Kagekazu, ffxiv-tc-port", GroupType = BossModuleInfo.GroupType.CFC, GroupID = 88, NameID = 3649)]
public class T02Bismarck(WorldState ws, Actor primary) : BossModule(ws, primary, DeckArena.DeckBounds.Center, DeckArena.DeckBounds)
{
    // 首領整場不可選取，預設的「首領可選取且戰鬥中」永遠不成立
    protected override bool CheckPull()
    {
        var party = Raid.WithoutSlot();
        for (var i = 0; i < party.Length; ++i)
            if (party[i].InCombat)
                return true;
        return false;
    }
}
