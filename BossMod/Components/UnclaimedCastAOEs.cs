namespace BossMod.Components;

// 模組沒認領的敵方讀條，照「沒有模組時」同一套猜法（AIHintsBuilder.GuessCastShape）畫在雷達上並交給 AI 閃避。
// 契約：模組的 AID 列舉＝模組知道的技能（有元件處理，或刻意不處理）；只有列舉以外的讀條才猜。
// 給機制沒寫全的模組用：模組啟用時 AIHintsBuilder 的後備猜測整個不跑，沒寫到的技能會連猜都不猜。
// 帶無敵狀態的敵人也照沒有模組時的做法降成 PriorityInvincible。
public class UnclaimedCastAOEs : GenericAOEs
{
    private readonly HashSet<uint> _claimed = [];
    private readonly List<AOEInstance> _aoes = [];
    private readonly List<(Actor Caster, Actor? Target, AOEShape Shape, WPos Location, Angle Rotation, DateTime Activation)> _gazes = [];
    private readonly List<Actor> _invincible = [];

    public UnclaimedCastAOEs(BossModule module) : base(module)
    {
        if (module.Info?.ActionIDType is { } aidType)
            foreach (var v in Enum.GetValuesAsUnderlyingType(aidType))
                _claimed.Add(Convert.ToUInt32(v));
    }

    public override ReadOnlySpan<AOEInstance> ActiveAOEs(int slot, Actor actor) => CollectionsMarshal.AsSpan(_aoes);

    public override void OnCastStarted(Actor caster, ActorCastInfo spell)
    {
        if (caster.Type is not ActorType.Enemy and not ActorType.Helper || caster.IsAlly || _claimed.Contains(spell.Action.ID))
            return;
        if (AIHintsBuilder.GuessCastShape(caster, spell, out var isCharge, out var isGaze) is not AOEShape shape)
            return;
        var activation = Module.CastFinishAt(spell);
        if (isGaze)
        {
            _gazes.Add((caster, WorldState.Actors.Find(spell.TargetID), shape, spell.LocXZ, spell.Rotation, activation));
        }
        else if (isCharge)
        {
            var origin = caster.Position.Quantized();
            var dir = spell.LocXZ - origin;
            _aoes.Add(new(new AOEShapeRect(dir.Length(), ((AOEShapeRect)shape).HalfWidth), origin, Angle.FromDirection(dir), activation, actorID: caster.InstanceID));
        }
        else
        {
            _aoes.Add(new(shape, spell.LocXZ, spell.Rotation, activation, actorID: caster.InstanceID));
        }
    }

    public override void OnCastFinished(Actor caster, ActorCastInfo spell)
    {
        var id = caster.InstanceID;
        for (var i = _aoes.Count - 1; i >= 0; --i)
            if (_aoes[i].ActorID == id)
                _aoes.RemoveAt(i);
        for (var i = _gazes.Count - 1; i >= 0; --i)
            if (_gazes[i].Caster == caster)
                _gazes.RemoveAt(i);
    }

    public override void OnStatusGain(Actor actor, ActorStatus status)
    {
        if (AIHintsBuilder.IsInvincibleStatus(status.ID) && !_invincible.Contains(actor))
            _invincible.Add(actor);
    }

    public override void OnStatusLose(Actor actor, ActorStatus status)
    {
        if (AIHintsBuilder.IsInvincibleStatus(status.ID))
            _invincible.Remove(actor);
    }

    public override void AddAIHints(int slot, Actor actor, PartyRolesConfig.Assignment assignment, AIHints hints)
    {
        base.AddAIHints(slot, actor, assignment, hints);
        var invincible = _invincible.Count;
        for (var i = 0; i < invincible; ++i)
            hints.SetPriority(_invincible[i], AIHints.Enemy.PriorityInvincible);
        var count = _gazes.Count;
        for (var i = 0; i < count; ++i)
        {
            var gaze = _gazes[i];
            var target = gaze.Target?.Position ?? gaze.Location;
            if (gaze.Shape.Check(actor.Position, target, gaze.Rotation))
                hints.ForbiddenDirections.Add((Angle.FromDirection(target - actor.Position), 45f.Degrees(), gaze.Activation));
        }
    }
}
