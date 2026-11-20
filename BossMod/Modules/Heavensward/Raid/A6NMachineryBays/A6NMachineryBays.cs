namespace BossMod.Heavensward.Raid.A6NMachineryBays;

sealed class BioArithmeticks(BossModule module) : Components.RaidwideCast(module, (uint)AID.BioArithmeticks);
// 擊退距離沒有證據，只當全體傷害處理。
sealed class SuperCyclone(BossModule module) : Components.RaidwideCast(module, (uint)AID.SuperCyclone);

// 標記上的球數＝圈裡要剛好幾個人；人數不對才會受傷，所以正確分攤時不算全體傷害。
// 標記到判定的秒數沒有錄影，5 秒只影響 AI 開始集合的時機。
sealed class Enumeration(BossModule module) : Components.GenericStackSpread(module, raidwideOnResolve: false)
{
    public override void OnEventIcon(Actor actor, uint iconID, ulong targetID)
    {
        var count = iconID switch
        {
            (uint)IconID.Enumeration2 => 2,
            (uint)IconID.Enumeration3 => 3,
            (uint)IconID.Enumeration4 => 4,
            _ => 0
        };
        if (count != 0)
            Stacks.Add(new(actor, 5f, count, count, WorldState.FutureTime(5d)));
    }

    public override void OnEventCast(Actor caster, ActorCastEvent spell)
    {
        if (spell.Action.ID == (uint)AID.Enumeration)
            Stacks.Clear();
    }
}

sealed class A6NMachineryBaysStates : StateMachineBuilder
{
    public A6NMachineryBaysStates(BossModule module) : base(module)
    {
        TrivialPhase()
            .ActivateOnEnter<BioArithmeticks>()
            .ActivateOnEnter<SuperCyclone>()
            .ActivateOnEnter<Enumeration>()
            .ActivateOnEnter<Components.UnclaimedCastAOEs>();
    }
}

// 四隻各在自己的機工兵倉庫，場地取離首領出生位置最近的那一間（台服碰撞地板放得下的最大圓／方形）。
// 不寫死「哪隻在哪間」：倉庫順序只有攻略的文字，取最近的那間就算順序猜錯也不會把 AI 引到別間。
public abstract class A6NMachineryBays(WorldState ws, Actor primary) : UnverifiedOidTrialModule(ws, primary, Bays[NearestBay(primary)].Center, Bays[NearestBay(primary)].Bounds)
{
    private static readonly (WPos Center, ArenaBounds Bounds)[] Bays =
    [
        (new(0f, -120f), new ArenaBoundsCircle(18.5f)),
        (new(0f, -184f), new ArenaBoundsCircle(18.5f)),
        (new(0f, -248f), new ArenaBoundsSquare(19.5f)),
        (new(0f, -312f), new ArenaBoundsCircle(23.5f)),
    ];

    private static int NearestBay(Actor primary)
    {
        var best = 0;
        var bestDist = float.MaxValue;
        for (var i = 0; i < Bays.Length; ++i)
        {
            var d = (primary.Position - Bays[i].Center).LengthSq();
            if (d < bestDist)
            {
                bestDist = d;
                best = i;
            }
        }
        return best;
    }
}

[ModuleInfo(BossModuleInfo.Maturity.WIP, StatesType = typeof(A6NMachineryBaysStates), PrimaryActorOID = (uint)OID.BlasterA, Contributors = "ffxiv-tc-port", Expansion = BossModuleInfo.Expansion.Heavensward, Category = BossModuleInfo.Category.Raid, GroupType = BossModuleInfo.GroupType.CFC, GroupID = 137u, NameID = 4699u, SortOrder = 1)]
public sealed class A6NBlasterA(WorldState ws, Actor primary) : A6NMachineryBays(ws, primary);

[ModuleInfo(BossModuleInfo.Maturity.WIP, StatesType = typeof(A6NMachineryBaysStates), PrimaryActorOID = (uint)OID.BlasterB, Contributors = "ffxiv-tc-port", Expansion = BossModuleInfo.Expansion.Heavensward, Category = BossModuleInfo.Category.Raid, GroupType = BossModuleInfo.GroupType.CFC, GroupID = 137u, NameID = 4699u, SortOrder = 1)]
public sealed class A6NBlasterB(WorldState ws, Actor primary) : A6NMachineryBays(ws, primary);

[ModuleInfo(BossModuleInfo.Maturity.WIP, StatesType = typeof(A6NMachineryBaysStates), PrimaryActorOID = (uint)OID.BrawlerA, Contributors = "ffxiv-tc-port", Expansion = BossModuleInfo.Expansion.Heavensward, Category = BossModuleInfo.Category.Raid, GroupType = BossModuleInfo.GroupType.CFC, GroupID = 137u, NameID = 4703u, SortOrder = 2)]
public sealed class A6NBrawlerA(WorldState ws, Actor primary) : A6NMachineryBays(ws, primary);

[ModuleInfo(BossModuleInfo.Maturity.WIP, StatesType = typeof(A6NMachineryBaysStates), PrimaryActorOID = (uint)OID.BrawlerB, Contributors = "ffxiv-tc-port", Expansion = BossModuleInfo.Expansion.Heavensward, Category = BossModuleInfo.Category.Raid, GroupType = BossModuleInfo.GroupType.CFC, GroupID = 137u, NameID = 4703u, SortOrder = 2)]
public sealed class A6NBrawlerB(WorldState ws, Actor primary) : A6NMachineryBays(ws, primary);

[ModuleInfo(BossModuleInfo.Maturity.WIP, StatesType = typeof(A6NMachineryBaysStates), PrimaryActorOID = (uint)OID.SwindlerA, Contributors = "ffxiv-tc-port", Expansion = BossModuleInfo.Expansion.Heavensward, Category = BossModuleInfo.Category.Raid, GroupType = BossModuleInfo.GroupType.CFC, GroupID = 137u, NameID = 4705u, SortOrder = 3)]
public sealed class A6NSwindlerA(WorldState ws, Actor primary) : A6NMachineryBays(ws, primary);

[ModuleInfo(BossModuleInfo.Maturity.WIP, StatesType = typeof(A6NMachineryBaysStates), PrimaryActorOID = (uint)OID.SwindlerB, Contributors = "ffxiv-tc-port", Expansion = BossModuleInfo.Expansion.Heavensward, Category = BossModuleInfo.Category.Raid, GroupType = BossModuleInfo.GroupType.CFC, GroupID = 137u, NameID = 4705u, SortOrder = 3)]
public sealed class A6NSwindlerB(WorldState ws, Actor primary) : A6NMachineryBays(ws, primary);

[ModuleInfo(BossModuleInfo.Maturity.WIP, StatesType = typeof(A6NMachineryBaysStates), PrimaryActorOID = (uint)OID.VortexerA, Contributors = "ffxiv-tc-port", Expansion = BossModuleInfo.Expansion.Heavensward, Category = BossModuleInfo.Category.Raid, GroupType = BossModuleInfo.GroupType.CFC, GroupID = 137u, NameID = 4706u, SortOrder = 4)]
public sealed class A6NVortexerA(WorldState ws, Actor primary) : A6NMachineryBays(ws, primary);

[ModuleInfo(BossModuleInfo.Maturity.WIP, StatesType = typeof(A6NMachineryBaysStates), PrimaryActorOID = (uint)OID.VortexerB, Contributors = "ffxiv-tc-port", Expansion = BossModuleInfo.Expansion.Heavensward, Category = BossModuleInfo.Category.Raid, GroupType = BossModuleInfo.GroupType.CFC, GroupID = 137u, NameID = 4706u, SortOrder = 4)]
public sealed class A6NVortexerB(WorldState ws, Actor primary) : A6NMachineryBays(ws, primary);
