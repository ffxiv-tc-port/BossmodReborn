namespace BossMod.RealmReborn.Alliance.A32FiveheadedDragon;

// 普攻是朝目標的無讀條扇形。
class AutoAttackCleave(BossModule module) : Components.Cleave(module, (uint)AID.AutoAttack1, new AOEShapeCone(14f, 60f.Degrees()));
class WhiteBreath(BossModule module) : Components.SimpleAOEs(module, (uint)AID.WhiteBreath, new AOEShapeCone(30, 60.Degrees()));
class BreathOfFire(BossModule module) : Components.SimpleAOEs(module, (uint)AID.BreathOfFire, 6);
class BreathOfLight(BossModule module) : Components.SimpleAOEs(module, (uint)AID.BreathOfLight, 6);
class BreathOfPoison(BossModule module) : Components.SimpleAOEs(module, (uint)AID.BreathOfPoison, 6);
class BreathOfIce(BossModule module) : Components.SimpleAOEs(module, (uint)AID.BreathOfIce, 6);
// 雷之吐息沒有讀條：標記 318 落在一人身上約 6.1 秒後，以他為中心半徑 6 分攤（錄影兩次 17、19 人一起吃）。
class BreathOfThunder(BossModule module) : Components.StackWithIcon(module, (uint)IconID.Icon318, (uint)AID.BreathOfThunder, 6f, 6.1d);

class Radiance(BossModule module) : Components.RaidwideCast(module, (uint)AID.Radiance);
class HeatWave(BossModule module) : Components.RaidwideCast(module, (uint)AID.HeatWave);
class Discordance(BossModule module) : Components.RaidwideCast(module, (uint)AID.Discordance);

[ModuleInfo(BossModuleInfo.Maturity.WIP, Contributors = "The Combat Reborn Team", GroupType = BossModuleInfo.GroupType.CFC, GroupID = 111, NameID = 3227)]
public class A32FiveheadedDragon(WorldState ws, Actor primary) : BossModule(ws, primary, new(200, 180), new ArenaBoundsCircle(30))
{
    protected override void DrawEnemies(int pcSlot, Actor pc)
    {
        Arena.Actor(PrimaryActor);
        Arena.Actors(Enemies(OID.HeadOfFire));
        Arena.Actors(Enemies(OID.HeadOfPoison));
        Arena.Actors(Enemies(OID.HeadOfThunder));
        Arena.Actors(Enemies(OID.HeadOfIce));
        Arena.Actors(Enemies(OID.Prominence));
        Arena.Actors(Enemies(OID.PoisonSlime));
        Arena.Actors(Enemies(OID.ToxicSlime));
        Arena.Actors(Enemies(OID.DragonfireFly));
    }
}
