namespace BossMod.Stormblood.Raid.O9NChaos;

sealed class ChaoticDispersion(BossModule module) : Components.SingleTargetCast(module, (uint)AID.ChaoticDispersion);
sealed class DamningEdict(BossModule module) : Components.SimpleAOEs(module, (uint)AID.DamningEdict, new AOEShapeRect(60f, 30f));
// 兩對對向扇形先後判定，同時畫四個會把整圈都畫成危險區：只畫先到的一對。
sealed class Shockwave(BossModule module) : Components.SimpleAOEs(module, (uint)AID.Shockwave, new AOEShapeCone(60f, 45f.Degrees()), 2);
sealed class BigBangSmall(BossModule module) : Components.SimpleAOEs(module, (uint)AID.BigBangSmall, 6f);
sealed class BigBangLarge(BossModule module) : Components.SimpleAOEs(module, (uint)AID.BigBangLarge, 15f);
sealed class FiendishOrbsLine(BossModule module) : Components.ChargeAOEs(module, (uint)AID.FiendishOrbsLine, 1.5f);
sealed class KnockStack(BossModule module) : Components.StackWithIcon(module, (uint)IconID.KnockStack, (uint)AID.KnockStack, 6f, 5.9d);
sealed class Blaze(BossModule module) : Components.RaidwideCast(module, (uint)AID.Blaze);
sealed class Tsunami(BossModule module) : Components.RaidwideCast(module, (uint)AID.Tsunami);
sealed class Cyclone(BossModule module) : Components.RaidwideCast(module, (uint)AID.Cyclone);
sealed class Earthquake(BossModule module) : Components.RaidwideCast(module, (uint)AID.Earthquake);
sealed class BowelsOfAgony(BossModule module) : Components.RaidwideCast(module, (uint)AID.BowelsOfAgony);

// 狀態到期時以本人為中心爆炸；同一時間有兩批不同到期時間的人，只移除到期那個人的散開。
sealed class Entropy(BossModule module) : Components.GenericStackSpread(module)
{
    public override void OnStatusGain(Actor actor, ActorStatus status)
    {
        if (status.ID == (uint)SID.Entropy)
            Spreads.Add(new(actor, 5f, status.ExpireAt));
    }

    public override void OnStatusLose(Actor actor, ActorStatus status)
    {
        if (status.ID == (uint)SID.Entropy)
            Spreads.RemoveAll(s => s.Target == actor);
    }
}

// 元素地形讀條（火＝外圈、水＝東西兩側、風＝中央龍捲、土＝南半）在讀完後持續到下一個元素地形或深層痛楚。
// 持續時間沒有錄影：攻略說地形維持到下一個小階段，另設 100 秒上限防止殘留。
sealed class ElementalTerrain(BossModule module) : Components.GenericAOEs(module)
{
    private readonly List<AOEInstance> _aoes = [];
    private uint _current;

    public override ReadOnlySpan<AOEInstance> ActiveAOEs(int slot, Actor actor) => CollectionsMarshal.AsSpan(_aoes);

    public override void OnCastStarted(Actor caster, ActorCastInfo spell)
    {
        var id = spell.Action.ID;
        AOEShape? shape = id switch
        {
            (uint)AID.FireTerrain or (uint)AID.FireTerrain2 => new AOEShapeDonut(10f, 30f),
            (uint)AID.WaterTerrain => new AOEShapeRect(15f, 20f),
            (uint)AID.WindTerrain => new AOEShapeCircle(10f),
            (uint)AID.EarthTerrain => new AOEShapeRect(20f, 20f),
            _ => null
        };
        if (shape != null)
        {
            if (_current != id)
                _aoes.Clear();
            _current = id;
            _aoes.Add(new(shape, spell.LocXZ, spell.Rotation, Module.CastFinishAt(spell)));
        }
        else if (id == (uint)AID.BowelsOfAgony)
        {
            _aoes.Clear();
            _current = default;
        }
    }

    public override void Update()
    {
        if (_aoes.Count != 0 && WorldState.CurrentTime > _aoes[0].Activation.AddSeconds(100d))
        {
            _aoes.Clear();
            _current = default;
        }
    }
}

sealed class O9NChaosStates : StateMachineBuilder
{
    public O9NChaosStates(BossModule module) : base(module)
    {
        TrivialPhase()
            .ActivateOnEnter<ChaoticDispersion>()
            .ActivateOnEnter<DamningEdict>()
            .ActivateOnEnter<Shockwave>()
            .ActivateOnEnter<BigBangSmall>()
            .ActivateOnEnter<BigBangLarge>()
            .ActivateOnEnter<FiendishOrbsLine>()
            .ActivateOnEnter<KnockStack>()
            .ActivateOnEnter<Blaze>()
            .ActivateOnEnter<Tsunami>()
            .ActivateOnEnter<Cyclone>()
            .ActivateOnEnter<Earthquake>()
            .ActivateOnEnter<BowelsOfAgony>()
            .ActivateOnEnter<Entropy>()
            .ActivateOnEnter<ElementalTerrain>()
            .ActivateOnEnter<Components.UnclaimedCastAOEs>();
    }
}

// 場地＝LGB 地形事件範圍圍出的 40×40 方形（中心 (100,100)）；台服碰撞地板延伸到 ±28，方形內每格都有地板。
[ModuleInfo(BossModuleInfo.Maturity.WIP, StatesType = typeof(O9NChaosStates), Contributors = "ffxiv-tc-port", Expansion = BossModuleInfo.Expansion.Stormblood, Category = BossModuleInfo.Category.Raid, GroupType = BossModuleInfo.GroupType.CFC, GroupID = 587u, NameID = 7691u, SortOrder = 1)]
public sealed class O9NChaos(WorldState ws, Actor primary) : BossModule(ws, primary, new(100f, 100f), new ArenaBoundsSquare(20f));
