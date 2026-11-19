namespace BossMod.RealmReborn.Dungeon.D19HaukkeHard.D193Halicarnassus;

sealed class VoidFire2(BossModule module) : Components.SimpleAOEGroups(module, [(uint)AID.VoidFire2, (uint)AID.VoidFire2Alt], 5f);
sealed class VoidFire3(BossModule module) : Components.SimpleAOEs(module, (uint)AID.VoidFire3, 10f);
sealed class DarkMist(BossModule module) : Components.SimpleAOEs(module, (uint)AID.DarkMist, 10f);
sealed class DarkMistAmandine(BossModule module) : Components.SimpleAOEs(module, (uint)AID.DarkMistAmandine, 9.8f);
sealed class TerrifyingEye(BossModule module) : Components.SimpleAOEs(module, (uint)AID.TerrifyingEye, new AOEShapeCone(7.35f, 45f.Degrees()));
sealed class VoidFire2Maidservant(BossModule module) : Components.SpreadFromCastTargets(module, (uint)AID.VoidFire2Maidservant, 5f);
sealed class VoidThunder(BossModule module) : Components.SingleTargetCasts(module, [(uint)AID.VoidThunder2, (uint)AID.VoidThunder3]);
sealed class VoidThunder3Amandine(BossModule module) : Components.SingleTargetCast(module, (uint)AID.VoidThunder3Amandine, "", AIHints.PredictedDamageType.None);
sealed class VoidThunder3AmandineInterrupt(BossModule module) : Components.CastInterruptHint(module, (uint)AID.VoidThunder3Amandine);
sealed class BeguilingMist(BossModule module) : Components.CastHint(module, (uint)AID.BeguilingMist, "Charm: forced walk towards boss");
sealed class BloodRain(BossModule module) : Components.RaidwideCasts(module, [(uint)AID.BloodRain1, (uint)AID.BloodRain2, (uint)AID.BloodRain3]);

// 吸收小怪後的血雨傷害照被吸收那隻剩下的 HP 算（滿血吸收＝全滅），吸收時機是王的 HP 門檻或計時。
// 契約：王的 HP 低於該波出現門檻（女僕 60%、阿芒迪娜 40%）而那一波還有 HP 高的小怪時，王標成 PriorityForbidden，
// AI 換去打小怪、不再推王的 HP；小怪打到 Spent 以下（鎖 1 HP 不會死）或被吸收後放行。坦克不套用（要留住仇恨）。
// 刻意不做：別的隊員把王打太快擋不了（只控制得到自己），只在雷達上提示。
sealed class Absorption(BossModule module) : BossComponent(module)
{
    private const float Spent = 0.05f;
    private const float MaidHold = 0.6f;
    private const float AmandineHold = 0.4f;
    private const double PreSpawnHoldSeconds = 15d;

    private readonly List<Actor> _maids = module.Enemies((uint)OID.ManorMaidservant);
    private readonly List<Actor> _amandine = module.Enemies((uint)OID.LadyAmandine);
    private readonly List<Actor> _sentry = module.Enemies((uint)OID.ManorSentry);
    private readonly HashSet<ulong> _absorbed = [];
    private DateTime _maidHoldStart, _amandineHoldStart;
    private bool _maidsSeen, _amandineSeen;
    private bool _holdMaids, _holdAmandine;

    public bool Holding => _holdMaids || _holdAmandine;

    public override void OnCastStarted(Actor caster, ActorCastInfo spell)
    {
        if (spell.Action.ID == (uint)AID.Seduction)
            _absorbed.Add(spell.TargetID);
    }

    public override void Update()
    {
        var boss = Module.PrimaryActor;
        var hp = boss.HPMP.MaxHP > 0 ? (float)boss.HPMP.CurHP / boss.HPMP.MaxHP : 1f;
        _maidsSeen |= _maids.Count > 0;
        _amandineSeen |= _amandine.Count > 0;
        _holdMaids = Hold(hp, MaidHold, _maidsSeen, _maids, ref _maidHoldStart);
        _holdAmandine = Hold(hp, AmandineHold, _amandineSeen, _amandine, ref _amandineHoldStart);
    }

    // 跨過門檻但那一波還沒出現（王在讀別的招）最多等 PreSpawnHoldSeconds，門檻推測錯時王不會永遠打不得；
    // 出現後等到每隻都打完或被吸收。
    private bool Hold(float bossHP, float threshold, bool seen, List<Actor> adds, ref DateTime holdStart)
    {
        if (bossHP >= threshold || Module.PrimaryActor.IsDead)
            return false;
        if (!seen)
        {
            if (holdStart == default)
                holdStart = WorldState.CurrentTime;
            return (WorldState.CurrentTime - holdStart).TotalSeconds < PreSpawnHoldSeconds;
        }
        var count = adds.Count;
        for (var i = 0; i < count; ++i)
            if (IsDangerous(adds[i]))
                return true;
        return false;
    }

    // 剛出生還不能選取的那幾幀也算：不然王會在小怪出現那一刻短暫解禁。
    private bool IsDangerous(Actor add) => !add.IsDead && !add.IsDestroyed && add.HPRatio > Spent && !_absorbed.Contains(add.InstanceID);

    public override void AddAIHints(int slot, Actor actor, PartyRolesConfig.Assignment assignment, AIHints hints)
    {
        if (actor.Role == Role.Tank)
            return;
        Prioritize(hints, _sentry, 2, false);
        Prioritize(hints, _maids, 3, true);
        Prioritize(hints, _amandine, 3, true);
        if (Holding)
            hints.SetPriority(Module.PrimaryActor, AIHints.Enemy.PriorityForbidden);
    }

    private void Prioritize(AIHints hints, List<Actor> adds, int priority, bool urgent)
    {
        var count = adds.Count;
        for (var i = 0; i < count; ++i)
        {
            var a = adds[i];
            if (hints.FindEnemy(a) is not { } e || e.Actor != a)
                continue;
            if (IsDangerous(a))
            {
                e.Priority = priority;
                e.UrgentKill = urgent;
            }
            else
            {
                e.Priority = AIHints.Enemy.PriorityPointless;
            }
        }
    }

    public override void AddGlobalHints(GlobalHints hints)
    {
        if (Holding)
            hints.Add(Loc.T("Stop attacking Halicarnassus: bring the add to 0% before she absorbs it!"));
    }
}

sealed class D193HalicarnassusStates : StateMachineBuilder
{
    public D193HalicarnassusStates(BossModule module) : base(module)
    {
        TrivialPhase()
            .ActivateOnEnter<VoidFire2>()
            .ActivateOnEnter<VoidFire3>()
            .ActivateOnEnter<DarkMist>()
            .ActivateOnEnter<DarkMistAmandine>()
            .ActivateOnEnter<TerrifyingEye>()
            .ActivateOnEnter<VoidFire2Maidservant>()
            .ActivateOnEnter<VoidThunder>()
            .ActivateOnEnter<VoidThunder3Amandine>()
            .ActivateOnEnter<VoidThunder3AmandineInterrupt>()
            .ActivateOnEnter<BeguilingMist>()
            .ActivateOnEnter<BloodRain>()
            .ActivateOnEnter<Absorption>()
            .ActivateOnEnter<Components.UnclaimedCastAOEs>();
    }
}

[ModuleInfo(BossModuleInfo.Maturity.Contributed, Contributors = "ffxiv-tc-port", GroupType = BossModuleInfo.GroupType.CFC, GroupID = 19, NameID = 2346)]
public sealed class D193Halicarnassus(WorldState ws, Actor primary) : BossModule(ws, primary, new(1.5f, default), new ArenaBoundsRect(24f, 16f))
{
    protected override void DrawEnemies(int pcSlot, Actor pc)
    {
        Arena.Actor(PrimaryActor);
        Arena.Actors(Enemies((uint)OID.ManorSentry));
        Arena.Actors(Enemies((uint)OID.ManorMaidservant));
        Arena.Actors(Enemies((uint)OID.LadyAmandine));
    }
}
