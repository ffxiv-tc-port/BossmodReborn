using Dalamud.Bindings.ImGui;

namespace BossMod;

// a lot of boss fights can be modeled as state machines
// by far the most common state has a single transition to a neighbouring state, and by far the most common transition is spell cast/finish by boss
// some bosses have multiple "phases"; when phase condition is triggered, initial state of the next phase is activated
// typical phase condition is boss reaching specific hp %
public sealed class StateMachine(List<StateMachine.Phase> phases)
{
    [Flags]
    public enum StateHint
    {
        None = 0,
        GroupWithNext = 1 << 0, // this state is a logical 'substate' - it should be grouped together with next one for visualization
        BossCastStart = 1 << 1, // state ends when boss starts some cast
        BossCastEnd = 1 << 2, // state ends when boss ends a cast
        Tankbuster = 1 << 3, // state end is a tankbuster event - tank has to press some save
        Raidwide = 1 << 4, // state end is a raidwide damage event - someone better press some save
        Knockback = 1 << 5, // state end is a knockback event - it's a good time to use arm's length, or otherwise avoid being knocked into voidzones/walls/etc.
        DowntimeStart = 1 << 6, // at state end downtime starts - there are no targets to damage
        DowntimeEnd = 1 << 7, // at state end downtime ends
        PositioningStart = 1 << 8, // at state end a phase with movement or precise positioning requirements starts - we should be careful with charges etc.
        PositioningEnd = 1 << 9, // at state end positioning requirements are relaxed
        VulnerableStart = 1 << 10, // at state end some target becomes vulnerable and takes extra damage
        VulnerableEnd = 1 << 11, // at state end vulnerability phase ends
    }

    [Flags]
    public enum PhaseHint
    {
        None = 0,
        StartWithDowntime = 1 << 0, // the phase starts with downtime
    }

    public class State
    {
        public uint ID;
        public float Duration; // estimated state duration
        public string Name = ""; // if name is empty, state is "hidden" from UI
        public string Comment = "";
        public Action? Enter; // callbacks executed when state is activated
        public Action? Exit; // callbacks executed when state is deactivated; note that this can happen unexpectedly, e.g. due to external reset or phase change
        public Func<float, int>? Update; // callback executed every frame when state is active; should return target state index for transition or -1 to remain in current state; argument = time since activation
        public State[]? NextStates; // potential successor states
        public StateHint EndHint = StateHint.None; // special flags for state end (used for visualization, autorotation, etc.)
    }

    public class Phase(State initialState, string name, float expectedDuration = -1)
    {
        public State InitialState = initialState;
        public string Name = name;
        public float ExpectedDuration = expectedDuration; // if >= 0, this is 'expected' phase duration (for use by CD planner etc); <0 means 'calculate from state tree max time'
        public Action? Enter; // callbacks executed when phase is activated
        public Action? Exit; // callbacks executed when phase is deactivated; note that this can happen unexpectedly, e.g. due to external reset
        public Func<bool>? Update; // callback executed every frame when phase is active; should return whether transition to next phase should happen
        public PhaseHint Hint = PhaseHint.None; // special flags for phase
    }

    public readonly List<Phase> Phases = phases;

    private DateTime _curTime;
    private DateTime _activation;
    private DateTime _phaseEnter;
    private DateTime _lastTransition;
    public float TimeSinceActivation => (float)(_curTime - _activation).TotalSeconds;
    public float TimeSincePhaseEnter => (float)(_curTime - _phaseEnter).TotalSeconds;
    public float TimeSinceTransition => (float)(_curTime - _lastTransition).TotalSeconds;
    public float TimeSinceTransitionClamped => Math.Min(TimeSinceTransition, ActiveState?.Duration ?? 0);

    public int ActivePhaseIndex = -1;
    public Phase? ActivePhase => Phases.ElementAtOrDefault(ActivePhaseIndex);
    public State? ActiveState;

    public void Start(DateTime now)
    {
        _activation = _curTime = now;
        if (Phases.Count != 0)
            TransitionToPhase(0);
    }

    public void Reset()
    {
        TransitionToPhase(-1);
    }

    public void Update(DateTime now)
    {
        _curTime = now;
        while (ActivePhase != null)
        {
            var transition = ActivePhase.Update?.Invoke() ?? false;
            if (!transition)
                break;
            Service.Log($"[StateMachine] Phase transition from {ActivePhaseIndex} '{ActivePhase.Name}', time={TimeSincePhaseEnter:f2}");
            TransitionToPhase(ActivePhaseIndex + 1);
        }
        while (ActiveState != null)
        {
            var transition = ActiveState.Update?.Invoke(TimeSinceTransition) ?? -1;
            var nextState = ActiveState.NextStates != null && transition >= 0 && transition < ActiveState.NextStates.Length ? ActiveState.NextStates[transition] : null;
            if (nextState == null)
                break;
            Service.Log($"[StateMachine] State transition from {ActiveState.ID:X} '{ActiveState.Name}' to {nextState.ID:X} '{nextState.Name}', overdue={TimeSinceTransition:f2}-{ActiveState.Duration:f2}={TimeSinceTransition - ActiveState.Duration:f2}");
            TransitionToState(nextState);
        }
    }

    // 繪製用的暫存：只在繪製執行緒用、Draw 不重入。
    private static readonly StringBuilder _drawText = new();
    private static char[] _drawChars = new char[256];

    public void Draw()
    {
        var sb = _drawText;
        sb.Clear().Append(Loc.T("SM_Cur", "Cur: "));
        State? next = null;
        if (ActiveState != null)
            next = AppendComplexStateNameAndDuration(sb, ActiveState, TimeSinceTransition, true);
        else
            sb.Append(Loc.T("SM_Inactive", "Inactive"));
        DrawText(sb);

        sb.Clear().Append(Loc.T("SM_Then", "Then: "));
        var prefix = sb.Length;
        AppendStateChain(sb, next, " ---> ", 5);
        if (sb.Length == prefix)
        {
            ImGui.TextUnformatted("");
        }
        else
        {
            DrawText(sb);
        }
    }

    private static void DrawText(StringBuilder sb)
    {
        var len = sb.Length;
        if (_drawChars.Length < len)
            _drawChars = new char[Math.Max(len, _drawChars.Length * 2)];
        sb.CopyTo(0, _drawChars, 0, len);
        ImGui.TextUnformatted(_drawChars.AsSpan(0, len));
    }

    public string BuildStateChain(State? start, string sep, int maxCount = 5)
    {
        var res = new StringBuilder();
        AppendStateChain(res, start, sep, maxCount);
        return res.ToString();
    }

    private static void AppendStateChain(StringBuilder res, State? start, string sep, int maxCount)
    {
        var count = 0;
        while (start != null && count < maxCount)
        {
            var mark = res.Length;
            if (count > 0)
                res.Append(sep);
            var nameStart = res.Length;
            var next = AppendComplexStateNameAndDuration(res, start, 0, false);
            if (res.Length > nameStart)
                ++count;
            else
                res.Length = mark; // 空名字不算一段，也不留分隔
            start = next;
        }
    }

    public DateTime NextTransitionWithFlag(StateHint flag)
    {
        var time = _lastTransition;
        var next = ActiveState;
        while (next != null)
        {
            time = time.AddSeconds(next.Duration);
            if (next.EndHint.HasFlag(flag))
                return time;
            next = next.NextStates?.Length == 1 ? next.NextStates[0] : null;
        }
        return DateTime.MaxValue;
    }

    // this whole chain (Draw -> AppendStateChain -> AppendComplexStateNameAndDuration) exists purely to render the sequence/timer line,
    // so it is safe to localize the state names here - nothing else ever sees these strings
    // 把狀態名（含 GroupWithNext 併進來的）與剩餘時間接在 res 後面；「名字是不是空的」只看這次接上去的部分。
    private static State? AppendComplexStateNameAndDuration(StringBuilder res, State start, float timeActive, bool writeTime)
    {
        var baseLen = res.Length;
        res.Append(HintText.Translate(start.Name));
        var timeLeft = Math.Max(0, start.Duration - timeActive);
        if (writeTime && res.Length > baseLen)
        {
            AppendTimeLeft(res, timeLeft);
            timeLeft = 0;
        }

        while (start.EndHint.HasFlag(StateHint.GroupWithNext) && start.NextStates?.Length == 1)
        {
            start = start.NextStates[0];
            timeLeft += Math.Max(0, start.Duration);
            if (start.Name.Length > 0)
            {
                if (res.Length > baseLen)
                    res.Append(" + ");
                res.Append(HintText.Translate(start.Name));

                if (writeTime && timeLeft > 0)
                {
                    AppendTimeLeft(res, timeLeft);
                    timeLeft = 0;
                }
            }
        }

        if (writeTime && timeLeft > 0)
        {
            if (res.Length == baseLen)
                res.Append("???");
            AppendTimeLeft(res, timeLeft);
        }

        return start.NextStates?.Length == 1 ? start.NextStates[0] : null;
    }

    // english reads "<name> in 3.5s", chinese reads "<name> 3.5 秒後" - hence a prefix + suffix pair rather than a format string
    private static void AppendTimeLeft(StringBuilder res, float timeLeft)
    {
        res.Append(Loc.T("SM_TimePrefix", " in "));
        res.Append($"{timeLeft:f1}");
        res.Append(Loc.T("SM_TimeSuffix", "s"));
    }

    private void TransitionToPhase(int nextIndex)
    {
        if (ActivePhase != null)
            TransitionToState(null);

        ActivePhase?.Exit?.Invoke();
        ActivePhaseIndex = nextIndex;
        _phaseEnter = _curTime;
        ActivePhase?.Enter?.Invoke();

        if (ActivePhase != null)
            TransitionToState(ActivePhase.InitialState);
    }

    private void TransitionToState(State? nextState)
    {
        ActiveState?.Exit?.Invoke();
        ActiveState = nextState;
        _lastTransition = _curTime;
        ActiveState?.Enter?.Invoke();
    }
}
