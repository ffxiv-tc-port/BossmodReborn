namespace BossMod.Stormblood.Raid.O6NDemonChadarnook;

sealed class DemonicShear(BossModule module) : Components.SingleTargetCast(module, (uint)AID.DemonicShear);
sealed class DemonicHowl(BossModule module) : Components.RaidwideCast(module, (uint)AID.DemonicHowl);
// 只畫第一發；之後追人的無讀條判定間隔與次數沒有證據。
sealed class DemonicStone(BossModule module) : Components.SimpleAOEs(module, (uint)AID.DemonicStone, 3f);

// 畫作附身（火／土／水／風）要用陸行鳥之筆畫畫布、拿抗性或坐騎或擋浪的岩石，AI 做不到，刻意不處理；
// 惡魔痛的連線拉開距離沒有證據、閃光風分攤的標記沒有證據，也不處理。
sealed class O6NDemonChadarnookStates : StateMachineBuilder
{
    public O6NDemonChadarnookStates(BossModule module) : base(module)
    {
        TrivialPhase()
            .ActivateOnEnter<DemonicShear>()
            .ActivateOnEnter<DemonicHowl>()
            .ActivateOnEnter<DemonicStone>()
            .ActivateOnEnter<Components.UnclaimedCastAOEs>();
    }
}

// 場地＝台服碰撞在 (0,0) 的八角形地板（45×30、四角各切掉約 7 碼放畫作），內縮 0.5 碼。
public abstract class O6NDemonChadarnook(WorldState ws, Actor primary) : UnverifiedOidTrialModule(ws, primary, arena.Center, arena)
{
    private static readonly WPos[] vertices = [new(22f, 7.8f), new(15.3f, 14.5f), new(-15.3f, 14.5f), new(-22f, 7.8f),
        new(-22f, -7.8f), new(-15.3f, -14.5f), new(15.3f, -14.5f), new(22f, -7.8f)];
    private static readonly ArenaBoundsComplex arena = new([new PolygonCustom(vertices)]);
}

[ModuleInfo(BossModuleInfo.Maturity.WIP, StatesType = typeof(O6NDemonChadarnookStates), PrimaryActorOID = (uint)OID.BossCandidateA, Contributors = "ffxiv-tc-port", Expansion = BossModuleInfo.Expansion.Stormblood, Category = BossModuleInfo.Category.Raid, GroupType = BossModuleInfo.GroupType.CFC, GroupID = 287u, NameID = 7097u, SortOrder = 1)]
public sealed class O6NDemonChadarnookA(WorldState ws, Actor primary) : O6NDemonChadarnook(ws, primary);

[ModuleInfo(BossModuleInfo.Maturity.WIP, StatesType = typeof(O6NDemonChadarnookStates), PrimaryActorOID = (uint)OID.BossCandidateB, Contributors = "ffxiv-tc-port", Expansion = BossModuleInfo.Expansion.Stormblood, Category = BossModuleInfo.Category.Raid, GroupType = BossModuleInfo.GroupType.CFC, GroupID = 287u, NameID = 7097u, SortOrder = 1)]
public sealed class O6NDemonChadarnookB(WorldState ws, Actor primary) : O6NDemonChadarnook(ws, primary);
