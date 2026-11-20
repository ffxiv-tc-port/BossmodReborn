namespace BossMod.RealmReborn.Dungeon.D11DzemaelDarkhold.D111AllSeeingEye;

public enum OID : uint
{
    Boss = 0x605, // x1
    MoucheVolante = 0x606, // spawn during fight
    Amanuensis = 0x607, // spawn during fight
    Crystal = 0x1E8594 // x6, EventObj type
}

public enum AID : uint
{
    AutoAttack = 870, // Boss/Amanuensis->player, no cast, single-target

    CursedGaze = 512, // Boss->self, 2.5s cast, range 6+2.7 90-degree cone
    DreadGaze = 513, // Boss->self, 3.0s cast, range 6+2.7 90-degree cone
    EyesOnMe = 951, // Boss->self, no cast, raidwide

    AutoAttackAdd = 878, // MoucheVolante->player, no cast, single-target
    Thunderstrike = 1097, // MoucheVolante->self, 2.0s cast, range 10+1.2 width 3 rect
    Condemnation = 1100 // Amanuensis->self, 2.5s cast, range 6+1.3 90-degree cone
}

public enum SID : uint
{
    Invincibility = 325 // none->Boss, extra=0x0
}

abstract class Gaze(BossModule module, uint aid) : Components.SimpleAOEs(module, aid, new AOEShapeCone(8.7f, 45.Degrees()));
class CursedGaze(BossModule module) : Gaze(module, (uint)AID.CursedGaze);
class DreadGaze(BossModule module) : Gaze(module, (uint)AID.DreadGaze);

class Thunderstrike(BossModule module) : Components.SimpleAOEs(module, (uint)AID.Thunderstrike, new AOEShapeRect(11.2f, 1.5f));
class Condemnation(BossModule module) : Components.SimpleAOEs(module, (uint)AID.Condemnation, new AOEShapeCone(7.3f, 45.Degrees()));

// try to always stay in active crystal closest to boss
class Positioning(BossModule module) : BossComponent(module)
{
    public override void AddAIHints(int slot, Actor actor, PartyRolesConfig.Assignment assignment, AIHints hints)
    {
        if (Module.PrimaryActor.CastInfo == null) // do not restrict zone while boss is casting, to allow avoiding aoe, even if it means temporarily leaving crystal veil
        {
            var closestCrystal = Module.Enemies(OID.Crystal).Closest(Module.PrimaryActor.Position);
            if (closestCrystal != null)
                hints.AddForbiddenZone(ShapeDistance.InvertedCircle(closestCrystal.Position, 8)); // TODO: verify range
        }
    }
}

class D111AllSeeingEyeStates : StateMachineBuilder
{
    public D111AllSeeingEyeStates(BossModule module) : base(module)
    {
        TrivialPhase()
            .ActivateOnEnter<CursedGaze>()
            .ActivateOnEnter<DreadGaze>()
            .ActivateOnEnter<Thunderstrike>()
            .ActivateOnEnter<Condemnation>()
            .ActivateOnEnter<Positioning>();
    }
}

[ModuleInfo(BossModuleInfo.Maturity.Verified, GroupType = BossModuleInfo.GroupType.CFC, GroupID = 13, NameID = 1397)]
public class D111AllSeeingEye(WorldState ws, Actor primary) : BossModule(ws, primary, arena.Center, arena)
{
    // 原本的正方形只涵蓋洞窟北半：王常被拉到東側、南側的水晶旁打，Positioning 要站的水晶光圈也會落在正方形外。
    // 補上的是台服碰撞資料算出的同一層可走範圍（往內縮 1 碼），正方形原樣保留。
    // 放在 AdditionalShapes 另行聯集：跟正方形擺在同一組時兩者繞向相反，非零規則會把重疊處挖空。
    private static readonly Square[] square = [new Square(new(40f, 70f), 30f)];
    private static readonly PolygonCustom[] extension = [new PolygonCustom([
        new(43.88f, 118.50f), new(41.62f, 117.00f), new(39.12f, 117.00f), new(38.12f, 116.00f), new(36.12f, 116.50f), new(33.12f, 114.00f),
        new(29.12f, 113.75f), new(28.25f, 111.62f), new(29.38f, 110.75f), new(34.38f, 111.25f), new(39.12f, 115.00f), new(41.25f, 113.88f),
        new(39.88f, 111.75f), new(37.12f, 111.00f), new(33.38f, 107.00f), new(27.88f, 107.25f), new(27.50f, 105.38f), new(24.62f, 102.00f),
        new(23.50f, 103.12f), new(24.00f, 104.62f), new(22.50f, 107.12f), new(23.62f, 108.50f), new(25.25f, 107.38f), new(24.00f, 112.12f),
        new(19.62f, 113.25f), new(17.12f, 113.25f), new(15.50f, 112.12f), new(15.00f, 110.12f), new(15.88f, 108.25f), new(17.12f, 109.25f),
        new(18.62f, 107.50f), new(20.12f, 108.50f), new(21.25f, 107.38f), new(19.62f, 106.00f), new(18.50f, 106.38f), new(19.75f, 104.62f),
        new(18.62f, 103.50f), new(17.50f, 104.12f), new(17.25f, 101.62f), new(19.88f, 101.00f), new(21.38f, 99.50f), new(22.88f, 100.75f),
        new(25.00f, 98.62f), new(23.38f, 96.75f), new(21.00f, 98.62f), new(20.75f, 89.38f), new(15.25f, 81.38f), new(16.50f, 78.38f),
        new(15.25f, 76.88f), new(15.00f, 75.12f), new(16.12f, 75.00f), new(17.88f, 78.00f), new(20.12f, 78.25f), new(24.00f, 75.12f),
        new(27.00f, 70.88f), new(25.75f, 68.12f), new(23.12f, 66.00f), new(19.25f, 67.12f), new(21.50f, 61.12f), new(22.88f, 61.25f),
        new(24.00f, 59.88f), new(22.38f, 58.50f), new(21.12f, 58.50f), new(19.62f, 56.00f), new(18.38f, 57.25f), new(16.62f, 55.25f),
        new(14.88f, 56.00f), new(13.25f, 51.62f), new(11.12f, 49.50f), new(9.88f, 49.25f), new(8.62f, 50.50f), new(3.38f, 51.75f),
        new(2.50f, 46.38f), new(3.38f, 45.00f), new(5.88f, 44.75f), new(7.12f, 45.50f), new(11.38f, 43.75f), new(13.62f, 46.50f),
        new(15.50f, 47.38f), new(14.75f, 48.62f), new(15.88f, 49.75f), new(17.38f, 49.00f), new(18.88f, 50.50f), new(19.62f, 49.75f),
        new(21.38f, 52.25f), new(23.12f, 50.50f), new(27.12f, 50.00f), new(29.50f, 47.88f), new(29.62f, 46.75f), new(31.12f, 46.00f),
        new(33.38f, 46.25f), new(36.38f, 43.75f), new(39.88f, 44.00f), new(43.12f, 45.50f), new(44.88f, 44.50f), new(47.38f, 45.25f),
        new(48.38f, 44.25f), new(49.88f, 44.25f), new(52.88f, 47.00f), new(54.38f, 46.75f), new(56.88f, 49.00f), new(59.12f, 48.50f),
        new(62.12f, 45.00f), new(65.88f, 46.75f), new(67.88f, 46.25f), new(69.25f, 47.38f), new(69.75f, 49.12f), new(72.00f, 50.88f),
        new(72.25f, 52.88f), new(73.38f, 54.00f), new(74.50f, 52.88f), new(74.50f, 50.88f), new(79.75f, 47.12f), new(80.00f, 49.12f),
        new(82.25f, 52.88f), new(81.50f, 53.88f), new(82.00f, 55.88f), new(80.75f, 57.88f), new(82.00f, 64.88f), new(79.38f, 68.25f),
        new(78.50f, 62.12f), new(79.00f, 59.12f), new(77.62f, 57.75f), new(76.12f, 58.25f), new(71.88f, 55.00f), new(69.50f, 52.38f),
        new(69.25f, 50.38f), new(67.12f, 48.25f), new(66.12f, 49.25f), new(63.12f, 48.75f), new(61.38f, 49.75f), new(60.00f, 51.12f),
        new(59.00f, 54.62f), new(56.00f, 56.62f), new(57.12f, 57.75f), new(59.38f, 57.50f), new(62.25f, 54.88f), new(63.12f, 52.50f),
        new(66.12f, 53.25f), new(66.62f, 52.75f), new(69.88f, 56.50f), new(70.88f, 56.50f), new(76.25f, 60.88f), new(75.00f, 62.62f),
        new(75.25f, 65.62f), new(74.12f, 64.75f), new(73.00f, 65.88f), new(74.12f, 67.00f), new(75.12f, 66.00f), new(75.00f, 67.12f),
        new(72.00f, 69.12f), new(72.00f, 70.88f), new(73.12f, 72.00f), new(75.62f, 69.25f), new(76.00f, 69.62f), new(73.00f, 73.12f),
        new(74.50f, 74.88f), new(73.50f, 78.38f), new(75.12f, 79.75f), new(79.62f, 81.00f), new(81.00f, 84.88f), new(79.88f, 85.00f),
        new(79.25f, 83.12f), new(76.62f, 81.50f), new(73.88f, 82.50f), new(71.88f, 85.00f), new(68.00f, 87.12f), new(70.00f, 91.62f),
        new(72.88f, 94.00f), new(76.38f, 93.00f), new(78.75f, 89.88f), new(78.75f, 91.62f), new(76.00f, 94.12f), new(74.50f, 98.88f),
        new(73.12f, 98.75f), new(72.00f, 99.88f), new(73.12f, 101.25f), new(74.88f, 101.50f), new(76.38f, 104.00f), new(77.50f, 102.88f),
        new(78.00f, 98.88f), new(79.25f, 97.38f), new(79.00f, 94.88f), new(81.62f, 92.00f), new(84.75f, 91.62f), new(81.75f, 95.38f),
        new(82.00f, 97.88f), new(80.50f, 99.88f), new(81.00f, 102.62f), new(79.50f, 103.62f), new(80.75f, 104.88f), new(79.75f, 105.88f),
        new(79.75f, 107.12f), new(81.12f, 108.50f), new(82.25f, 107.38f), new(82.12f, 106.50f), new(83.25f, 109.12f), new(86.12f, 110.75f),
        new(87.38f, 109.50f), new(88.38f, 109.75f), new(89.88f, 108.75f), new(91.75f, 110.88f), new(91.75f, 112.88f), new(89.88f, 115.00f),
        new(88.88f, 114.50f), new(84.62f, 116.25f), new(82.38f, 113.50f), new(77.50f, 110.38f), new(78.25f, 109.12f), new(77.12f, 108.00f),
        new(76.12f, 108.50f), new(73.62f, 107.00f), new(70.88f, 110.50f), new(68.38f, 110.25f), new(66.50f, 112.12f), new(66.50f, 113.12f),
        new(65.12f, 113.75f), new(61.12f, 113.25f), new(60.12f, 112.25f), new(56.62f, 113.75f), new(54.62f, 112.25f), new(53.38f, 112.50f),
        new(52.12f, 110.25f), new(50.25f, 112.38f), new(50.25f, 113.38f), new(48.25f, 114.88f), new(48.50f, 116.12f)])];
    private static readonly ArenaBoundsComplex arena = new(square, AdditionalShapes: extension);

    protected override void CalculateModuleAIHints(int slot, Actor actor, PartyRolesConfig.Assignment assignment, AIHints hints)
    {
        if (PrimaryActor.FindStatus(SID.Invincibility) != null)
            hints.PotentialTargets.RemoveAll(e => e.Actor == PrimaryActor);
    }
}
