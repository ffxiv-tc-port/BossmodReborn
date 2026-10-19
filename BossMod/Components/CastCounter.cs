namespace BossMod.Components;

// generic component that counts specified casts
public class CastCounter(BossModule module, uint aid) : BossComponent(module)
{
    public readonly uint WatchedAction = aid;
    public int NumCasts;

    private readonly uint[] _hintDamageActions = aid != default ? [aid] : [];
    public override uint[] HintDamageActions => _hintDamageActions;

    public override void OnEventCast(Actor caster, ActorCastEvent spell)
    {
        if (spell.Action.ID == WatchedAction)
            ++NumCasts;
    }
}

public class CastCounterMulti(BossModule module, uint[] aids) : BossComponent(module)
{
    public readonly uint[] WatchedActions = aids;
    public int NumCasts;

    public override uint[] HintDamageActions => WatchedActions;

    public override void OnEventCast(Actor caster, ActorCastEvent spell)
    {
        var len = WatchedActions.Length;
        for (var i = 0; i < len; ++i)
        {
            if (spell.Action.ID == WatchedActions[i])
            {
                ++NumCasts;
                return;
            }
        }
    }
}
