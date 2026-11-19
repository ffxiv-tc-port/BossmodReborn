namespace BossMod.RealmReborn.Alliance.A31AngraMainyu;

class DoubleVision(BossModule module) : Components.RaidwideCast(module, (uint)AID.DoubleVision);
// 判定的是 helper 與王同時開始、晚約 1.4 秒結束的 MortalGaze2（落點＝王的位置）；綁王的讀條會讓背對提示提早消失。
class MortalGaze(BossModule module) : Components.CastGaze(module, (uint)AID.MortalGaze2);
class Level100Flare1(BossModule module) : Components.SimpleAOEs(module, (uint)AID.Level100Flare1, 10);
class Level150Death1(BossModule module) : Components.SimpleAOEs(module, (uint)AID.Level150Death1, 10);

[ModuleInfo(BossModuleInfo.Maturity.WIP, Contributors = "The Combat Reborn Team", GroupType = BossModuleInfo.GroupType.CFC, GroupID = 111, NameID = 3231)]
public class A31AngraMainyu(WorldState ws, Actor primary) : BossModule(ws, primary, new(-145, 300), new ArenaBoundsCircle(30))
{
    protected override void DrawEnemies(int pcSlot, Actor pc)
    {
        Arena.Actor(PrimaryActor);
        Arena.Actors(Enemies(OID.FinalHourglass));
        Arena.Actors(Enemies(OID.GrimReaper));
        Arena.Actors(Enemies(OID.AngraMainyusDaewa));
    }
}
