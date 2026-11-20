namespace BossMod.Shadowbringers.Raid.E07NIdolOfDarkness;

sealed class EmptyWave(BossModule module) : Components.RaidwideCast(module, (uint)AID.EmptyWave);
sealed class UnshadowedStake(BossModule module) : Components.BaitAwayTethers(module, new AOEShapeRect(100f, 3f), (uint)TetherID.UnshadowedStake, (uint)AID.StygianStake);
sealed class SilverSledge(BossModule module) : Components.StackWithCastTargets(module, (uint)AID.SilverSledge, 6f);
sealed class SilverShot(BossModule module) : Components.SpreadFromCastTargets(module, (uint)AID.SilverShot, 6f);
// 內徑照 Omen gl_sircle_2008（外 20／內 8）；後備猜法對 CastType 10 一律給內徑 3。
sealed class StrengthInNumbersDonut(BossModule module) : Components.SimpleAOEs(module, (uint)AID.StrengthInNumbersDonut, new AOEShapeDonut(8f, 20f));

// 偏屬性：光的人要被黑暗奔流打中、偏屬性：暗的人要被白光奔流打中（被同色打中才致命），所以只對他畫同色的直線；
// 沒有偏屬性時兩色都要躲。經過傳送門轉向的那一段沒有讀條，照舊畫不出來。
sealed class Courses(BossModule module) : Components.GenericAOEs(module)
{
    private readonly List<AOEInstance> _light = [], _dark = [], _all = [];
    private static readonly AOEShapeRect _lane = new(50f, 5f), _laneToPortal = new(25f, 5f);

    public override ReadOnlySpan<AOEInstance> ActiveAOEs(int slot, Actor actor)
    {
        if (actor.FindStatus((uint)SID.AstralEffect) != null)
        {
            return CollectionsMarshal.AsSpan(_light);
        }
        if (actor.FindStatus((uint)SID.UmbralEffect) != null)
        {
            return CollectionsMarshal.AsSpan(_dark);
        }
        return CollectionsMarshal.AsSpan(_all);
    }

    public override void OnCastStarted(Actor caster, ActorCastInfo spell)
    {
        if (Classify(spell.Action.ID) is not bool isLight)
        {
            return;
        }
        var shape = spell.Action.ID == (uint)AID.LightsCourseToPortal ? _laneToPortal : _lane;
        var aoe = new AOEInstance(shape, spell.LocXZ, spell.Rotation, Module.CastFinishAt(spell), actorID: caster.InstanceID);
        (isLight ? _light : _dark).Add(aoe);
        _all.Add(aoe);
    }

    public override void OnCastFinished(Actor caster, ActorCastInfo spell)
    {
        if (Classify(spell.Action.ID) is null)
        {
            return;
        }
        var id = caster.InstanceID;
        _light.RemoveAll(a => a.ActorID == id);
        _dark.RemoveAll(a => a.ActorID == id);
        _all.RemoveAll(a => a.ActorID == id);
    }

    private static bool? Classify(uint aid) => aid switch
    {
        (uint)AID.LightsCourseShort or (uint)AID.LightsCourse or (uint)AID.LightsCourseAlt or (uint)AID.LightsCourseToPortal => true,
        (uint)AID.DarksCourseShort or (uint)AID.DarksCourse or (uint)AID.DarksCourseAlt => false,
        _ => null
    };
}

// 強制傳送（傳送後的落點）、黑暗之火的隕石塔要人踩，刻意不處理：落點方向與塔的人數沒有證據。
sealed class E07NIdolOfDarknessStates : StateMachineBuilder
{
    public E07NIdolOfDarknessStates(BossModule module) : base(module)
    {
        TrivialPhase()
            .ActivateOnEnter<EmptyWave>()
            .ActivateOnEnter<UnshadowedStake>()
            .ActivateOnEnter<SilverSledge>()
            .ActivateOnEnter<SilverShot>()
            .ActivateOnEnter<StrengthInNumbersDonut>()
            .ActivateOnEnter<Courses>()
            .ActivateOnEnter<Components.UnclaimedCastAOEs>();
    }
}

// 場地＝台服碰撞在 (100,100) 的 40×40 正方形地板（由幾條地板拼成），內縮 0.5 碼；傳送到場外會掉下去。
public abstract class E07NIdolOfDarkness(WorldState ws, Actor primary) : UnverifiedOidTrialModule(ws, primary, new(100f, 100f), new ArenaBoundsSquare(19.5f));

[ModuleInfo(BossModuleInfo.Maturity.WIP, StatesType = typeof(E07NIdolOfDarknessStates), PrimaryActorOID = (uint)OID.BossCandidateA, Contributors = "ffxiv-tc-port", Expansion = BossModuleInfo.Expansion.Shadowbringers, Category = BossModuleInfo.Category.Raid, GroupType = BossModuleInfo.GroupType.CFC, GroupID = 726u, NameID = 9298u, SortOrder = 1)]
public sealed class E07NIdolOfDarknessA(WorldState ws, Actor primary) : E07NIdolOfDarkness(ws, primary);

[ModuleInfo(BossModuleInfo.Maturity.WIP, StatesType = typeof(E07NIdolOfDarknessStates), PrimaryActorOID = (uint)OID.BossCandidateB, Contributors = "ffxiv-tc-port", Expansion = BossModuleInfo.Expansion.Shadowbringers, Category = BossModuleInfo.Category.Raid, GroupType = BossModuleInfo.GroupType.CFC, GroupID = 726u, NameID = 9298u, SortOrder = 1)]
public sealed class E07NIdolOfDarknessB(WorldState ws, Actor primary) : E07NIdolOfDarkness(ws, primary);
