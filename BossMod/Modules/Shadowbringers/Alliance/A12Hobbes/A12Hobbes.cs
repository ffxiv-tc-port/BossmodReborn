namespace BossMod.Shadowbringers.Alliance.A12Hobbes;

sealed class LaserResistanceTest(BossModule module) : Components.RaidwideCast(module, (uint)AID.LaserResistanceTest);

// 只畫有 Omen（外 20／內 15）的最外圈；內側兩圈的內徑沒有證據，交給後備猜法（內徑 3，比實際寬）。
sealed class RingLaserOuter(BossModule module) : Components.SimpleAOEs(module, (uint)AID.RingLaserOuter, new AOEShapeDonut(15f, 20f));

sealed class ShockingDischarge(BossModule module) : Components.SimpleAOEs(module, (uint)AID.ShockingDischarge, 5f);
sealed class VariableCombatTestCircle(BossModule module) : Components.SimpleAOEGroups(module, [(uint)AID.VariableCombatTestCircleFirst, (uint)AID.VariableCombatTestCircle], 2f);
sealed class VariableCombatTestDonut(BossModule module) : Components.SimpleAOEGroups(module, [(uint)AID.VariableCombatTestDonutFirst, (uint)AID.VariableCombatTestDonut], new AOEShapeDonut(7f, 19f));
sealed class Impact(BossModule module) : Components.SimpleAOEs(module, (uint)AID.Impact, 20f);
sealed class ShortRangeMissile(BossModule module) : Components.SpreadFromCastTargets(module, (uint)AID.ShortRangeMissile, 8f);

// 場地＝三個聯盟各自的圓形平台（碰撞資料半徑約 19.75，內縮到 19.5），彼此不相連；平台之間不放進場地。
[ModuleInfo(BossModuleInfo.Maturity.WIP, Contributors = "The Combat Reborn Team, ffxiv-tc-port", GroupType = BossModuleInfo.GroupType.CFC, GroupID = 700, NameID = 9143)]
public sealed class A12Hobbes(WorldState ws, Actor primary) : BossModule(ws, primary, arena.Center, arena)
{
    private static readonly ArenaBoundsComplex arena = new([new Circle(new(-805f, -270f), 19.5f), new Circle(new(-779.02f, -225f), 19.5f), new Circle(new(-830.98f, -225f), 19.5f)]);
}
