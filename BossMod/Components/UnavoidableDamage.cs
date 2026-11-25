namespace BossMod.Components;

// generic unavoidable raidwide, started and finished by a single cast
public class RaidwideCast(BossModule module, uint aid, string hint = "Raidwide") : CastHint(module, aid, hint)
{
    public override void AddAIHints(int slot, Actor actor, PartyRolesConfig.Assignment assignment, AIHints hints)
    {
        var count = Casters.Count;
        for (var i = 0; i < count; ++i)
        {
            var ci = Casters[i].CastInfo;
            hints.AddPredictedDamage(Raid.WithSlot().Mask(), Module.CastFinishAt(ci), AIHints.PredictedDamageType.Raidwide, ci?.Action.ID ?? 0u);
        }
    }
}

public class RaidwideCasts(BossModule module, uint[] aids, string hint = "Raidwide") : RaidwideCast(module, default, hint)
{
    private readonly uint[] AIDs = aids;

    public override uint[] HintDamageActions => AIDs;

    public override void OnCastStarted(Actor caster, ActorCastInfo spell)
    {
        var len = AIDs.Length;
        var id = spell.Action.ID;
        for (var i = 0; i < len; ++i)
        {
            if (id == AIDs[i])
            {
                Casters.Add(caster);
            }
        }
    }

    public override void OnCastFinished(Actor caster, ActorCastInfo spell)
    {
        var len = AIDs.Length;
        var id = spell.Action.ID;
        for (var i = 0; i < len; ++i)
        {
            if (id == AIDs[i])
            {
                Casters.Remove(caster);
            }
        }
    }

    public override void OnEventCast(Actor caster, ActorCastEvent spell)
    {
        var len = AIDs.Length;
        var id = spell.Action.ID;
        for (var i = 0; i < len; ++i)
        {
            if (id == AIDs[i])
            {
                ++NumCasts;
            }
        }
    }
}

// generic unavoidable raidwide, initiated by a custom condition and applied by an instant cast after a delay
public class RaidwideInstant(BossModule module, uint aid, double delay = default, string hint = "Raidwide") : CastCounter(module, aid)
{
    public readonly double Delay = delay;
    public readonly string Hint = hint;
    public DateTime Activation; // default if inactive, otherwise expected cast time

    // 刻意只在危險度表有列時才回報傷害 AID、表外回 0（走副本預設）：非 0 來源還會改變減傷的物理／魔法判定、
    // 本場實測升級與 IPC 來源，表外的瞬發招沒逐招驗過；有視覺讀條＋傷害 AID 的子類別直接覆寫成 WatchedAction
    protected virtual uint PredictedSourceAction
        => WatchedAction != 0 && DamageSeverityTable.TryGetEntry(WorldState.CurrentCFCID, WatchedAction, out _) ? WatchedAction : 0u;

    public override void AddGlobalHints(GlobalHints hints)
    {
        if (Activation != default && Hint.Length > 0)
            hints.Add(Loc.T(Hint));
    }

    public override void AddAIHints(int slot, Actor actor, PartyRolesConfig.Assignment assignment, AIHints hints)
    {
        if (Activation != default)
            hints.AddPredictedDamage(Raid.WithSlot().Mask(), Activation, AIHints.PredictedDamageType.Raidwide, PredictedSourceAction);
    }

    public override void OnEventCast(Actor caster, ActorCastEvent spell)
    {
        if (spell.Action.ID == WatchedAction)
        {
            ++NumCasts;
            Activation = default;
        }
    }
}

// generic unavoidable instant raidwide initiated by a cast (usually visual-only)
public class RaidwideCastDelay(BossModule module, uint actionVisual, uint actionAOE, double delay, string hint = "Raidwide") : RaidwideInstant(module, actionAOE, delay, hint)
{
    public uint ActionVisual = actionVisual;

    protected override uint PredictedSourceAction => WatchedAction;

    public override void OnCastStarted(Actor caster, ActorCastInfo spell)
    {
        if (spell.Action.ID == ActionVisual)
            Activation = Module.CastFinishAt(spell, Delay);
    }
}

// several visual->AOE pairs; contract: aidsVisual[i] is followed by aidsAOE[i] (same index)
public class RaidwideCastsDelay(BossModule module, uint[] aidsVisual, uint[] aidsAOE, double delay, string hint = "Raidwide") : RaidwideCastDelay(module, default, default, delay, hint)
{
    private readonly uint[] AIDsVisual = aidsVisual;
    private readonly uint[] AIDsAOE = aidsAOE;
    private uint _pendingAOE;

    public override uint[] HintDamageActions => AIDsAOE;
    protected override uint PredictedSourceAction => _pendingAOE;

    public override void OnCastStarted(Actor caster, ActorCastInfo spell)
    {
        var id = spell.Action.ID;
        var len = AIDsVisual.Length;
        for (var i = 0; i < len; ++i)
        {
            if (id == AIDsVisual[i])
            {
                Activation = Module.CastFinishAt(spell, Delay);
                _pendingAOE = i < AIDsAOE.Length ? AIDsAOE[i] : 0u;
            }
        }
    }

    public override void OnEventCast(Actor caster, ActorCastEvent spell)
    {
        var id = spell.Action.ID;
        var len = AIDsAOE.Length;
        for (var i = 0; i < len; ++i)
        {
            if (id == AIDsAOE[i])
            {
                ++NumCasts;
                Activation = default;
            }
        }
    }
}

// generic unavoidable instant raidwide cast initiated by NPC yell
public class RaidwideAfterNPCYell(BossModule module, uint aid, uint npcYellID, double delay, string hint = "Raidwide") : RaidwideInstant(module, aid, delay, hint)
{
    public uint NPCYellID = npcYellID;

    public override void OnActorNpcYell(Actor actor, ushort id)
    {
        if (id == NPCYellID)
            Activation = WorldState.FutureTime(Delay);
    }
}

// generic unavoidable single-target damage, started and finished by a single cast (typically tankbuster, but not necessary)
public class SingleTargetCast(BossModule module, uint aid, string hint = "Tankbuster", AIHints.PredictedDamageType damageType = AIHints.PredictedDamageType.Tankbuster) : CastHint(module, aid, hint)
{
    public override void AddAIHints(int slot, Actor actor, PartyRolesConfig.Assignment assignment, AIHints hints)
    {
        var count = Casters.Count;
        for (var i = 0; i < count; ++i)
        {
            var c = Casters[i];
            if (c.CastInfo != null)
            {
                var target = c.CastInfo.TargetID != c.InstanceID ? c.CastInfo.TargetID : c.TargetID; // assume self-targeted casts actually hit main target
                hints.AddPredictedDamage(new BitMask().WithBit(Raid.FindSlot(target)), Module.CastFinishAt(c.CastInfo), damageType, c.CastInfo.Action.ID, spread: damageType == AIHints.PredictedDamageType.Raidwide);
            }
        }
    }
}

public class SingleTargetCasts(BossModule module, uint[] aids, string hint = "Tankbuster") : SingleTargetCast(module, default, hint)
{
    private readonly uint[] AIDs = aids;

    public override uint[] HintDamageActions => AIDs;

    public override void OnCastStarted(Actor caster, ActorCastInfo spell)
    {
        var len = AIDs.Length;
        var id = spell.Action.ID;
        for (var i = 0; i < len; ++i)
        {
            if (id == AIDs[i])
            {
                Casters.Add(caster);
            }
        }
    }

    public override void OnCastFinished(Actor caster, ActorCastInfo spell)
    {
        var len = AIDs.Length;
        var id = spell.Action.ID;
        for (var i = 0; i < len; ++i)
        {
            if (id == AIDs[i])
            {
                Casters.Remove(caster);
            }
        }
    }

    public override void OnEventCast(Actor caster, ActorCastEvent spell)
    {
        var len = AIDs.Length;
        var id = spell.Action.ID;
        for (var i = 0; i < len; ++i)
        {
            if (id == AIDs[i])
            {
                ++NumCasts;
            }
        }
    }
}

// generic unavoidable single-target damage, initiated by a custom condition and applied by an instant cast after a delay
public class SingleTargetInstant(BossModule module, uint aid, double delay = default, string hint = "Tankbuster", AIHints.PredictedDamageType damageType = AIHints.PredictedDamageType.Tankbuster) : CastCounter(module, aid)
{
    public readonly double Delay = delay; // delay from visual cast end to cast event
    public readonly string Hint = hint;
    public readonly List<(int slot, DateTime activation, ulong instanceID)> Targets = [];

    public override void AddGlobalHints(GlobalHints hints)
    {
        if (Targets.Count != 0 && Hint.Length != 0)
            hints.Add(Loc.T(Hint));
    }

    public override void AddAIHints(int slot, Actor actor, PartyRolesConfig.Assignment assignment, AIHints hints)
    {
        foreach (var t in Targets)
            hints.AddPredictedDamage(new BitMask().WithBit(t.slot), t.activation, damageType, WatchedAction, spread: damageType == AIHints.PredictedDamageType.Raidwide);
    }

    public override void OnEventCast(Actor caster, ActorCastEvent spell)
    {
        if (spell.Action.ID == WatchedAction)
        {
            ++NumCasts;
            Targets.RemoveAll(t => t.instanceID == spell.MainTargetID);
        }
    }
}

// generic unavoidable instant single-target damage initiated by a cast (usually visual-only)
public class SingleTargetCastDelay(BossModule module, uint actionVisual, uint actionAOE, double delay, string hint = "Tankbuster", AIHints.PredictedDamageType damageType = AIHints.PredictedDamageType.Tankbuster) : SingleTargetInstant(module, actionAOE, delay, hint, damageType)
{
    public uint ActionVisual = actionVisual;

    public override void OnCastStarted(Actor caster, ActorCastInfo spell)
    {
        if (spell.Action.ID == ActionVisual)
        {
            var target = spell.TargetID != caster.InstanceID ? spell.TargetID : caster.TargetID; // assume self-targeted casts actually hit main target
            Targets.Add((Raid.FindSlot(target), Module.CastFinishAt(spell, Delay), target));
        }
    }
}

// generic unavoidable instant single-target damage initiated by a cast (usually visual-only)
public class SingleTargetEventDelay(BossModule module, uint actionVisual, uint actionAOE, double delay, string hint = "Tankbuster") : SingleTargetInstant(module, actionAOE, delay, hint)
{
    public uint ActionVisual = actionVisual;

    public override void OnEventCast(Actor caster, ActorCastEvent spell)
    {
        base.OnEventCast(caster, spell);
        if (spell.Action.ID == ActionVisual)
        {
            var target = spell.MainTargetID != caster.InstanceID ? spell.MainTargetID : caster.TargetID; // assume self-targeted casts actually hit main target
            Targets.Add((Raid.FindSlot(target), WorldState.FutureTime(Delay), target));
        }
    }
}

// generic unavoidable single-target damage, started and finished by a single cast, that can be delayed by moving out of range (typically tankbuster, but not necessary)
public class SingleTargetDelayableCast(BossModule module, uint aid, string hint = "Tankbuster", AIHints.PredictedDamageType damageType = AIHints.PredictedDamageType.Tankbuster) : SingleTargetCastDelay(module, aid, aid, default, hint, damageType);
