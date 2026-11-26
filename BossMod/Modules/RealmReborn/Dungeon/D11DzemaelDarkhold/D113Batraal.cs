namespace BossMod.RealmReborn.Dungeon.D11DzemaelDarkhold.D113Batraal;

public enum OID : uint
{
    Boss = 0x60A, // x1
    CorruptedCrystal = 0x60C, // spawn during fight
    VoidPitch = 0x6B4 // spawn during fight
}

// 契約：列舉以外的敵方讀條由 Components.UnclaimedCastAOEs 照沒有模組時的猜法畫；列進來＝不再猜。
public enum AID : uint
{
    AutoAttack = 870, // Boss->player, no cast, single-target

    GrimCleaver = 620, // Boss->player, no cast, single-target, random
    GrimHalo = 621, // Boss->self, no cast, range 6+4.6 circle
    GrimFate = 624, // Boss->self, no cast, range 8+4.6 ?-degree cone cleave
    Desolation = 958, // Boss->self, 2.3s cast, range 55+4.6 width 6 rect aoe
    Hellssend = 1132, // Boss->self, no cast, damage up buff
    AetherialSurge = 1167, // CorruptedCrystal->self, 3.0s cast, range 5+1 circle aoe
    SeaOfPitch = 962 // VoidPitch->location, no cast, range 4 circle
}

public enum SID : uint
{
    Invincibility = 325 // none->Boss, extra=0x0
}

class GrimFate(BossModule module) : Components.Cleave(module, (uint)AID.GrimFate, new AOEShapeCone(12.6f, 60f.Degrees())) // TODO: verify angle
{
    // 刻意不給被盯上的人「別讓順劈掃到隊友」的禁區：那是朝每個隊友各一個 100 碼錐形，
    // 首領轉打遠程時會蓋掉大半個場地，閃荒蕪新星（直線打被盯上的人）只剩往首領身上跑一條路。
    public override void AddAIHints(int slot, Actor actor, PartyRolesConfig.Assignment assignment, AIHints hints)
    {
        var origins = OriginsAndTargets();
        var count = origins.Count;
        for (var i = 0; i < count; ++i)
        {
            var e = origins[i];
            if (actor != e.target)
                hints.AddForbiddenZone(Shape, e.origin.Position.Quantized(), e.angle, NextExpected);
        }
    }
}
// 無讀條、間隔不規律、半徑蓋過近戰距離：只在雷達畫外框，刻意不給 AI 禁區（否則近戰永遠貼不了王）。
class GrimHalo(BossModule module) : BossComponent(module)
{
    public override void DrawArenaForeground(int pcSlot, Actor pc)
    {
        var boss = Module.PrimaryActor;
        if (!boss.IsDead && boss.IsTargetable)
            Arena.AddCircle(boss.Position, 10.6f);
    }
}

class Desolation(BossModule module) : Components.SimpleAOEs(module, (uint)AID.Desolation, new AOEShapeRect(60f, 3f));
class AetherialSurge(BossModule module) : Components.SimpleAOEs(module, (uint)AID.AetherialSurge, 6f);

// note: actor 'dies' immediately after casting
class SeaOfPitch(BossModule module) : Components.Voidzone(module, 4f, GetEnemies)
{
    private static Actor[] GetEnemies(BossModule module)
    {
        var enemies = module.Enemies((uint)OID.VoidPitch);
        var count = enemies.Count;
        if (count == 0)
            return [];

        var voidzones = new Actor[count];
        var index = 0;
        for (var i = 0; i < count; ++i)
        {
            var z = enemies[i];
            if (!z.IsDead)
                voidzones[index++] = z;
        }
        return voidzones[..index];
    }
}

class D113BatraalStates : StateMachineBuilder
{
    public D113BatraalStates(BossModule module) : base(module)
    {
        TrivialPhase()
            .ActivateOnEnter<GrimFate>()
            .ActivateOnEnter<GrimHalo>()
            .ActivateOnEnter<Desolation>()
            .ActivateOnEnter<AetherialSurge>()
            .ActivateOnEnter<SeaOfPitch>()
            .ActivateOnEnter<Components.UnclaimedCastAOEs>();
    }
}

[ModuleInfo(BossModuleInfo.Maturity.Verified, GroupType = BossModuleInfo.GroupType.CFC, GroupID = 13, NameID = 1396)]
public class D113Batraal(WorldState ws, Actor primary) : BossModule(ws, primary, arena.Center, arena)
{
    private static readonly PolygonCustom[] shape = [new([new(69.7f, -150.8f), new(84.3f, -160.6f), new(100.8f, -150.9f),
    new(103.5f, -156.9f), new(102.8f, -163f), new(94.3f, -181.3f), new(84.6f, -178.8f),
    new(82.2f, -185.4f), new(81.7f, -202.9f), new(76.8f, -202.5f), new(72.2f, -203.4f), new(58.4f, -198.1f),
    new(57.6f, -193.1f), new(65.2f, -188.8f), new(73.1f, -183.7f), new(75.1f, -178.7f), new(70.9f, -173.1f),
    new(59.4f, -164.6f), new(60.8f, -159f), new(58.4f, -158.1f), new(56f, -154.1f)])];
    public static readonly ArenaBoundsComplex arena = new(shape);

    protected override void CalculateModuleAIHints(int slot, Actor actor, PartyRolesConfig.Assignment assignment, AIHints hints)
    {
        var invincible = PrimaryActor.FindStatus((uint)SID.Invincibility) != null;
        AIHints.Enemy? nearestCrystal = null;
        var nearestDist = float.MaxValue;
        var count = hints.PotentialTargets.Count;
        for (var i = 0; i < count; ++i)
        {
            var e = hints.PotentialTargets[i];
            e.Priority = e.Actor.OID switch
            {
                (uint)OID.CorruptedCrystal => 1,
                (uint)OID.Boss when invincible => AIHints.Enemy.PriorityInvincible,
                _ => 0
            };
            // 分隊號令：水晶一可選取就宣告，不等首領身上的無敵（無敵要晚幾秒才上、與水晶同時消失）；水晶死了分隊會自己回首領
            if (e.Actor.OID == (uint)OID.CorruptedCrystal)
            {
                // 首領無敵到水晶死為止：這段時間打首領完全沒用，標限時必殺讓外部自動化把玩家目標切過來
                e.UrgentKill = invincible;
                e.SquadronFocus = 1;
                var dist = (e.Actor.Position - actor.Position).LengthSq();
                if (dist < nearestDist)
                {
                    nearestCrystal = e;
                    nearestDist = dist;
                }
            }
        }
        if (nearestCrystal != null)
            nearestCrystal.SquadronFocus = 2;
    }
}
