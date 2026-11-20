namespace BossMod.Stormblood.Alliance.A12Hashmal;

sealed class QuakeIV(BossModule module) : Components.RaidwideCast(module, (uint)AID.QuakeIV);
sealed class JaggedEdge(BossModule module) : Components.SimpleAOEs(module, (uint)AID.JaggedEdge, 8f);
sealed class ControlTower(BossModule module) : Components.SimpleAOEs(module, (uint)AID.ControlTower, 6f);
sealed class CommandTower(BossModule module) : Components.SimpleAOEs(module, (uint)AID.CommandTower, 10f);
sealed class EarthShaker(BossModule module) : Components.SimpleAOEs(module, (uint)AID.EarthShaker, new AOEShapeCone(60.5f, 30f.Degrees()));
sealed class Towerfall(BossModule module) : Components.SimpleAOEs(module, (uint)AID.Towerfall, new AOEShapeRect(40.5f, 8f));

// 左右兩版 EXD 形狀相同（從施放者起算的矩形），一律照讀條位置與方向畫；王讀條時站在場邊，所以只蓋到一側。
sealed class ExtremeEdge(BossModule module) : Components.SimpleAOEGroups(module, [(uint)AID.ExtremeEdgeL, (uint)AID.ExtremeEdgeR], new AOEShapeRect(66f, 18f));

sealed class FallingBoulder(BossModule module) : Components.StackWithCastTargets(module, (uint)AID.FallingBoulder, 6f);
sealed class FallingRock(BossModule module) : Components.SpreadFromCastTargets(module, (uint)AID.FallingRock, 8f);

// 場地＝碰撞資料的圓形廣場（半徑約 29.8）；南北通道戰鬥中封閉，不放進場地。
[ModuleInfo(BossModuleInfo.Maturity.WIP, Contributors = "The Combat Reborn Team, ffxiv-tc-port", GroupType = BossModuleInfo.GroupType.CFC, GroupID = 281, NameID = 6922)]
public sealed class A12Hashmal(WorldState ws, Actor primary) : BossModule(ws, primary, new(-320f, -46f), new ArenaBoundsCircle(29.5f));
