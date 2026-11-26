namespace BossMod.RealmReborn.Alliance.A22Glasya;

class Aura(BossModule module) : Components.SimpleAOEs(module, (uint)AID.Aura, 8);
class VileUtterance(BossModule module) : Components.SimpleAOEs(module, (uint)AID.VileUtterance, new AOEShapeCone(30, 22.5f.Degrees()));
// 無讀條、朝主目標的 90 度順劈（8+R）。
class AstralPunch(BossModule module) : Components.Cleave(module, (uint)AID.AstralPunch, new AOEShapeCone(12f, 45f.Degrees()));

[ModuleInfo(BossModuleInfo.Maturity.WIP, Contributors = "The Combat Reborn Team", GroupType = BossModuleInfo.GroupType.CFC, GroupID = 102, NameID = 2815)]
public class A22Glasya(WorldState ws, Actor primary) : BossModule(ws, primary, new(0, -200), new ArenaBoundsCircle(35));
