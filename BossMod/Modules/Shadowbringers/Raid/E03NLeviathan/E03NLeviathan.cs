namespace BossMod.Shadowbringers.Raid.E03NLeviathan;

sealed class TidalRoar(BossModule module) : Components.RaidwideCast(module, (uint)AID.TidalRoar);
sealed class Tsunami(BossModule module) : Components.RaidwideCast(module, (uint)AID.Tsunami);
// 頭上標記出現到判定的秒數沒有錄影可證，照前置讀條（5.0 秒）抓。
sealed class RipCurrent(BossModule module) : Components.BaitAwayIcon(module, new AOEShapeRect(50f, 3f), (uint)IconID.RipCurrent, (uint)AID.RipCurrent, 5.1d, tankbuster: true, damageType: AIHints.PredictedDamageType.Tankbuster);
sealed class FreakWave(BossModule module) : Components.SpreadFromCastTargets(module, (uint)AID.FreakWave, 5f);
// 全隊分攤：最少人數小於最多人數時，圈外的人會同時收到「進圈」與「別進圈」兩個禁區，所以兩者都設 8。
sealed class KillerWave(BossModule module) : Components.StackWithCastTargets(module, (uint)AID.KillerWave, 6f, 8, 8);

// 巨浪刻意不處理：擊退範圍（60×60）蓋住整個場地，交給後備猜法會畫成整個場地都是禁區；擊退距離沒有證據。
sealed class E03NLeviathanStates : StateMachineBuilder
{
    public E03NLeviathanStates(BossModule module) : base(module)
    {
        TrivialPhase()
            .ActivateOnEnter<TidalRoar>()
            .ActivateOnEnter<Tsunami>()
            .ActivateOnEnter<RipCurrent>()
            .ActivateOnEnter<FreakWave>()
            .ActivateOnEnter<KillerWave>()
            .ActivateOnEnter<Components.UnclaimedCastAOEs>();
    }
}

// 台服碰撞在場地處沒有地板（量不到）；海底地震的直線寬 20＋10＋10 推得場地寬 40，場外會掉下去。
// 沒有地板可驗，取內切圓再內縮 0.5 碼；海底地震打掉的區塊刻意不畫成場地切換（沒有地圖效果的證據）。
public abstract class E03NLeviathan(WorldState ws, Actor primary) : UnverifiedOidTrialModule(ws, primary, new(100f, 100f), new ArenaBoundsCircle(19.5f));

[ModuleInfo(BossModuleInfo.Maturity.WIP, StatesType = typeof(E03NLeviathanStates), PrimaryActorOID = (uint)OID.BossCandidateA, Contributors = "ffxiv-tc-port", Expansion = BossModuleInfo.Expansion.Shadowbringers, Category = BossModuleInfo.Category.Raid, GroupType = BossModuleInfo.GroupType.CFC, GroupID = 682u, NameID = 8486u, SortOrder = 1)]
public sealed class E03NLeviathanA(WorldState ws, Actor primary) : E03NLeviathan(ws, primary);

[ModuleInfo(BossModuleInfo.Maturity.WIP, StatesType = typeof(E03NLeviathanStates), PrimaryActorOID = (uint)OID.BossCandidateB, Contributors = "ffxiv-tc-port", Expansion = BossModuleInfo.Expansion.Shadowbringers, Category = BossModuleInfo.Category.Raid, GroupType = BossModuleInfo.GroupType.CFC, GroupID = 682u, NameID = 8486u, SortOrder = 1)]
public sealed class E03NLeviathanB(WorldState ws, Actor primary) : E03NLeviathan(ws, primary);
