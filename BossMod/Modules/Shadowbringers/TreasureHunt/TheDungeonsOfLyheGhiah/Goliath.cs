namespace BossMod.Shadowbringers.TreasureHunt.DungeonsOfLyheGhiah.Goliath;

public enum OID : uint
{
    Boss = 0x2BA5, //R=5.25
    GoliathsJavelin = 0x2BA6, //R=2.1
    DungeonQueen = 0x2A0A, // R0.84, icon 5, needs to be killed in order from 1 to 5 for maximum rewards
    DungeonGarlic = 0x2A08, // R0.84, icon 3, needs to be killed in order from 1 to 5 for maximum rewards
    DungeonTomato = 0x2A09, // R0.84, icon 4, needs to be killed in order from 1 to 5 for maximum rewards
    DungeonOnion = 0x2A06, // R0.84, icon 1, needs to be killed in order from 1 to 5 for maximum rewards
    DungeonEgg = 0x2A07, // R0.84, icon 2, needs to be killed in order from 1 to 5 for maximum rewards
    KeeperOfKeys = 0x2A05, // R3.23
    WaterVoidzone = 0x1E9998, // R0.5
    Helper = 0x233C
}

public enum AID : uint
{
    AutoAttack = 872, // Boss/GoliathsJavelin/Mandragoras->player, no cast, single-target

    MechanicalBlow = 17873, // Boss->player, 5.0s cast, single-target
    Wellbore = 17874, // Boss->location, 7.0s cast, range 15 circle
    Fount = 17875, // Helper->location, 3.0s cast, range 4 circle
    Incinerate = 17876, // Boss->self, 5.0s cast, range 100 circle
    Accelerate = 17877, // Boss->players, 5.0s cast, range 6 circle
    Compress1 = 17879, // Boss->self, 2.5s cast, range 100 width 7 cross
    Compress2 = 17878, // GoliathsJavelin->self, 2.5s cast, range 100+R width 7 rect

    Pollen = 6452, // DungeonQueen->self, 3.5s cast, range 6+R circle
    TearyTwirl = 6448, // DungeonOnion->self, 3.5s cast, range 6+R circle
    HeirloomScream = 6451, // DungeonTomato->self, 3.5s cast, range 6+R circle
    PluckAndPrune = 6449, // DungeonEgg->self, 3.5s cast, range 6+R circle
    PungentPirouette = 6450, // DungeonGarlic->self, 3.5s cast, range 6+R circle
    Mash = 17852, // KeeperOfKeys->self, 2.5s cast, range 12+R width 4 rect
    Scoop = 17853, // KeeperOfKeys->self, 4.0s cast, range 15 120-degree cone
    Inhale = 17855, // KeeperOfKeys->self, no cast, range 20 120-degree cone, attract 25 between hitboxes, shortly before Spin
    Spin = 17854, // KeeperOfKeys->self, 2.5s cast, range 11 circle
    Telega = 9630 // BonusAdds->self, no cast, single-target, bonus adds disappear
}

class Wellbore(BossModule module) : Components.SimpleAOEs(module, (uint)AID.Wellbore, 15f);
// 半徑 8 是上游目測（水窪外觀約 7.5～8），離線沒有資料可對。
class WellboreVoidzone(BossModule module) : Components.Voidzone(module, 8f, GetVoidzones)
{
    private static Actor[] GetVoidzones(BossModule module)
    {
        var enemies = module.Enemies((uint)OID.WaterVoidzone);
        var count = enemies.Count;
        if (count == 0)
        {
            return [];
        }
        var voidzones = new Actor[count];
        var index = 0;
        for (var i = 0; i < count; ++i)
        {
            var z = enemies[i];
            if (z.EventState != 7)
            {
                voidzones[index++] = z;
            }
        }
        return voidzones[..index];
    }
}
class Compress1(BossModule module) : Components.SimpleAOEs(module, (uint)AID.Compress1, new AOEShapeCross(100, 3.5f));
class Compress2(BossModule module) : Components.SimpleAOEs(module, (uint)AID.Compress2, new AOEShapeRect(102.1f, 3.5f));
class Accelerate(BossModule module) : Components.StackWithCastTargets(module, (uint)AID.Accelerate, 6f, 8, 8);
class Incinerate(BossModule module) : Components.RaidwideCast(module, (uint)AID.Incinerate);
class Fount(BossModule module) : Components.SimpleAOEs(module, (uint)AID.Fount, 4f);
class MechanicalBlow(BossModule module) : Components.SingleTargetCast(module, (uint)AID.MechanicalBlow);

class Spin(BossModule module) : Components.SimpleAOEs(module, (uint)AID.Spin, 11);
class Mash(BossModule module) : Components.SimpleAOEs(module, (uint)AID.Mash, new AOEShapeRect(15.23f, 2f));
class Scoop(BossModule module) : Components.SimpleAOEs(module, (uint)AID.Scoop, new AOEShapeCone(15f, 60f.Degrees()));

class MandragoraAOEs(BossModule module) : Components.SimpleAOEGroups(module, [(uint)AID.PluckAndPrune, (uint)AID.TearyTwirl,
(uint)AID.HeirloomScream, (uint)AID.PungentPirouette, (uint)AID.Pollen], 6.84f);

class GoliathStates : StateMachineBuilder
{
    public GoliathStates(BossModule module) : base(module)
    {
        TrivialPhase()
            .ActivateOnEnter<Wellbore>()
            .ActivateOnEnter<WellboreVoidzone>()
            .ActivateOnEnter<Fount>()
            .ActivateOnEnter<Compress1>()
            .ActivateOnEnter<Compress2>()
            .ActivateOnEnter<Accelerate>()
            .ActivateOnEnter<Incinerate>()
            .ActivateOnEnter<MechanicalBlow>()
            .ActivateOnEnter<Spin>()
            .ActivateOnEnter<Mash>()
            .ActivateOnEnter<Scoop>()
            .ActivateOnEnter<MandragoraAOEs>()
            .Raw.Update = () =>
            {
                var enemies = module.Enemies(Goliath.All);
                var count = enemies.Count;
                for (var i = 0; i < count; ++i)
                {
                    if (!enemies[i].IsDeadOrDestroyed)
                        return false;
                }
                return true;
            };
    }
}

[ModuleInfo(BossModuleInfo.Maturity.Verified, Contributors = "Malediktus", GroupType = BossModuleInfo.GroupType.CFC, GroupID = 688, NameID = 8953)]
public class Goliath(WorldState ws, Actor primary) : DungeonsOfLyheGhiahRoom5(ws, primary)
{
    private static readonly uint[] bonusAdds = [(uint)OID.DungeonEgg, (uint)OID.DungeonGarlic, (uint)OID.DungeonTomato, (uint)OID.DungeonOnion,
    (uint)OID.DungeonQueen, (uint)OID.KeeperOfKeys];
    public static readonly uint[] All = [(uint)OID.Boss, (uint)OID.GoliathsJavelin, .. bonusAdds];

    protected override void DrawEnemies(int pcSlot, Actor pc)
    {
        Arena.Actor(PrimaryActor);
        Arena.Actors(Enemies((uint)OID.GoliathsJavelin));
        Arena.Actors(Enemies(bonusAdds), Colors.Vulnerable);
    }

    protected override void CalculateModuleAIHints(int slot, Actor actor, PartyRolesConfig.Assignment assignment, AIHints hints)
    {
        var count = hints.PotentialTargets.Count;
        for (var i = 0; i < count; ++i)
        {
            var e = hints.PotentialTargets[i];
            e.Priority = e.Actor.OID switch
            {
                (uint)OID.DungeonOnion => 6,
                (uint)OID.DungeonEgg => 5,
                (uint)OID.DungeonGarlic => 4,
                (uint)OID.DungeonTomato => 3,
                (uint)OID.DungeonQueen or (uint)OID.KeeperOfKeys => 2,
                (uint)OID.GoliathsJavelin => 1,
                _ => 0
            };
        }
    }
}
