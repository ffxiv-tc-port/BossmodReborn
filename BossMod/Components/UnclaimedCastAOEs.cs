namespace BossMod.Components;

// 模組沒認領的敵方讀條，照「沒有模組時」同一套猜法（AIHintsBuilder.GuessCastShape）畫在雷達上並交給 AI 閃避。
// 契約：模組的 AID 列舉＝模組知道的技能（有元件處理，或刻意不處理）；只有列舉以外的讀條才猜。
// 給機制沒寫全的模組用：模組啟用時 AIHintsBuilder 的後備猜測整個不跑，沒寫到的技能會連猜都不猜。
// 帶無敵狀態的敵人也照沒有模組時的做法降成 PriorityInvincible、帶反擊狀態的只擋自動攻擊（AIHintsBuilder.MarkInvincible／MarkSpikes，每幀看當下狀態欄）。
public class UnclaimedCastAOEs : GenericAOEs
{
    private readonly HashSet<uint> _claimed = [];
    private readonly List<AOEInstance> _aoes = [];
    private readonly List<(Actor Caster, Actor? Target, AOEShape Shape, WPos Location, Angle Rotation, DateTime Activation)> _gazes = [];
    private readonly Dictionary<ulong, CastGuessTimeout> _timeouts = []; // 鍵＝施放者；只在逾時清除開著時更新
    private readonly CastGuessTimeoutReport _timeoutReport = new("模組沒認領的讀條猜測");

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
        RemoveCaster(caster.InstanceID);
    }

    private void RemoveCaster(ulong id)
    {
        _timeouts.Remove(id);
        for (var i = _aoes.Count - 1; i >= 0; --i)
            if (_aoes[i].ActorID == id)
                _aoes.RemoveAt(i);
        for (var i = _gazes.Count - 1; i >= 0; --i)
            if (_gazes[i].Caster.InstanceID == id)
                _gazes.RemoveAt(i);
    }

    // 同 AIHintsBuilder 的沒有模組路徑：讀條結束事件沒來、讀條卻早該結束的猜測移除（見 CastGuessTimeout）。
    public override void Update()
    {
        _timeoutReport.Flush(WorldState.CurrentTime);
        if (_aoes.Count == 0 && _gazes.Count == 0 || !CastGuessTimeout.Enabled)
            return;
        for (var i = _aoes.Count - 1; i >= 0; --i)
        {
            if (i >= _aoes.Count)
                continue;
            var id = _aoes[i].ActorID;
            if (CheckTimeout(id, WorldState.Actors.Find(id)))
                RemoveCaster(id);
        }
        for (var i = _gazes.Count - 1; i >= 0; --i)
        {
            if (i >= _gazes.Count)
                continue;
            var caster = _gazes[i].Caster;
            if (CheckTimeout(caster.InstanceID, caster))
                RemoveCaster(caster.InstanceID);
        }
    }

    private bool CheckTimeout(ulong id, Actor? caster)
    {
        if (caster == null)
        {
            Service.Logger.Debug($"[ZoneTTL] 模組沒認領的讀條猜測：移除讀條猜測危險區（施放者已不在物件表）：{id:X}");
            return true;
        }
        ref var timeout = ref CollectionsMarshal.GetValueRefOrAddDefault(_timeouts, id, out _);
        var verdict = timeout.Update(WorldState, caster);
        if (verdict == CastGuessTimeout.Verdict.None)
            return false;
        _timeoutReport.Record(WorldState, caster, verdict);
        return true;
    }

    public override void AddAIHints(int slot, Actor actor, PartyRolesConfig.Assignment assignment, AIHints hints)
    {
        base.AddAIHints(slot, actor, assignment, hints);
        AIHintsBuilder.MarkInvincible(hints);
        AIHintsBuilder.MarkSpikes(hints);
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
