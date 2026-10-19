namespace BossMod;

[ConfigDisplay(Name = "Automatic out-of-combat utility actions", Parent = typeof(ActionTweaksConfig), Order = -10)]
class OutOfCombatActionsConfig : ConfigNode
{
    [PropertyDisplay("Enable the feature")]
    public bool Enabled = false;

    [PropertyDisplay("Auto use Peloton when moving out of combat")]
    public bool AutoPeloton = false;

    [PropertyDisplay("Only auto use Peloton inside duties", tooltip: "Restricts the option above to instanced content - anything entered through the duty finder. When off, Peloton is also used while moving around the overworld.")]
    public bool AutoPelotonOnlyInDuty = false;
}

// Tweak to automatically use out-of-combat convenience actions (peloton, pet summoning, etc).
public sealed class OutOfCombatActionsTweak : IDisposable
{
    private readonly OutOfCombatActionsConfig _config = Service.Config.Get<OutOfCombatActionsConfig>();
    private readonly WorldState _ws;
    private readonly EventSubscriptions _subscriptions;
    private DateTime _nextAutoPeloton;

    public OutOfCombatActionsTweak(WorldState ws)
    {
        _ws = ws;
        _subscriptions = new
        (
            ws.Actors.StatusGain.Subscribe(OnStatusGain),
            ws.Actors.StatusLose.Subscribe(OnStatusLose)
        );
    }

    public void Dispose()
    {
        _subscriptions.Dispose();
    }

    public void FillActions(Actor player, AIHints hints)
    {
        if (!_config.Enabled || player.InCombat || _ws.Client.CountdownRemaining != null || player.MountId != 0 || player.Statuses.Any(s => s.ID is 418u or 2648u)) // note: in overworld content, you leave combat on death...
            return;

        // 「只在副本內」：CurrentCFCID 由 WorldStateGameSync 從 GameMain.CurrentContentFinderConditionId 同步，
        // 野外恆為 0、進副本才是該副本的 ContentFinderCondition row id，所以拿它當「人在副本裡」的旗標。
        // 走 WorldState 而不是 Service.Condition，是為了讓錄影重播也拿得到同一個判斷。
        if (_config.AutoPeloton && (!_config.AutoPelotonOnlyInDuty || _ws.CurrentCFCID != 0) && player.ClassCategory == ClassCategory.PhysRanged && _ws.CurrentTime >= _nextAutoPeloton)
        {
            var movementThreshold = 5f * _ws.Frame.Duration;
            if (player.LastFrameMovement.LengthSq() >= movementThreshold * movementThreshold)
                hints.ActionsToExecute.Push(ActionID.MakeSpell(ClassShared.AID.Peloton), player, ActionQueue.Priority.VeryLow);
        }

        // TODO: other things
    }

    private void OnStatusGain(Actor actor, int index)
    {
        if (actor != _ws.Party.Player())
            return;

        switch (actor.Statuses[index].ID)
        {
            case (uint)BRD.SID.Peloton:
                _nextAutoPeloton = actor.Statuses[index].ExpireAt.AddSeconds(-1);
                break;
        }
    }

    private void OnStatusLose(Actor actor, int index)
    {
        if (actor != _ws.Party.Player())
            return;

        switch (actor.Statuses[index].ID)
        {
            case (uint)BRD.SID.Peloton:
                if (_ws.CurrentTime < _nextAutoPeloton)
                    _nextAutoPeloton = _ws.FutureTime(1); // if peloton expired earlier than expected, don't recast immediately - this could've been caused by entering combat, status is lost few frames before combat flag is set
                break;
        }
    }
}
