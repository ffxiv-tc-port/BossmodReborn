namespace BossMod.RealmReborn.Dungeon.D30WanderersPalaceHard.D303ManxomeMolaaJaJa;

public enum OID : uint
{
    Boss = 0xDCE, // 光鱗兄弟 莫拉加加
    SacredIdol = 0xDD0 // 莫拉加加的祭器
}

// 契約：這個列舉＝模組知道的技能。列舉以外的敵方讀條由 Components.UnclaimedCastAOEs 照沒有模組時的猜法處理，
// 所以刻意不把其他技能列進來（列了就不再被猜）。
public enum AID : uint
{
    SoulDouse = 3230, // Boss->self, 3.0s cast, 隨機一人死亡宣告；不是凝視（背對首領的人照樣中）
    RottingSlash = 3227, // Boss->player, no cast, 附加腐氣狀態
    Miasma = 3228, // Boss->self, 2.5s cast, range 6 circle；同時讓身上有腐氣的人噴出 MiasmaBurst
    MiasmaBurst = 3440 // Helper->self, no cast, range 6 circle，在腐氣持有者腳下
}

public enum SID : uint
{
    Doom = 210, // 死亡宣告：補滿血才會解除
    Miasma = 622 // 腐氣：再吸入腐氣時以自己為中心噴出
}

sealed class MiasmaCircle(BossModule module) : Components.SimpleAOEs(module, (uint)AID.Miasma, 6f);

// 腐氣讀條開始時，身上有腐氣的人在讀條結束約 1.1 秒後以自己為中心噴出半徑 6 的範圍 ⇒ 當散開處理。
sealed class MiasmaBurst(BossModule module) : Components.GenericStackSpread(module, raidwideOnResolve: false)
{
    public override void OnCastStarted(Actor caster, ActorCastInfo spell)
    {
        if (spell.Action.ID != (uint)AID.Miasma)
            return;
        var activation = Module.CastFinishAt(spell, 1.1d);
        var raid = Raid.WithoutSlot();
        var len = raid.Length;
        for (var i = 0; i < len; ++i)
        {
            var p = raid[i];
            if (p.FindStatus((uint)SID.Miasma) != null && !Spreads.Exists(s => s.Target == p))
                Spreads.Add(new(p, 6f, activation));
        }
    }

    public override void OnEventCast(Actor caster, ActorCastEvent spell)
    {
        if (spell.Action.ID != (uint)AID.MiasmaBurst || Spreads.Count == 0)
            return;
        var at = caster.Position;
        var idx = Spreads.FindIndex(s => s.Target.Position.InCircle(at, 3f));
        if (idx >= 0)
            Spreads.RemoveAt(idx);
    }

    public override void OnStatusLose(Actor actor, ActorStatus status)
    {
        if (status.ID == (uint)SID.Miasma)
            Spreads.RemoveAll(s => s.Target == actor);
    }

    // 噴出事件沒對上（施放者位置偏差、錄影缺事件）時的上界：過了生效時間 2 秒一律收掉。
    public override void Update()
    {
        base.Update();
        if (Spreads.Count != 0)
            Spreads.RemoveAll(s => s.Activation.AddSeconds(2d) < WorldState.CurrentTime);
    }
}

sealed class Doom(BossModule module) : BossComponent(module)
{
    private readonly List<Actor> _doomed = [];

    public override void OnStatusGain(Actor actor, ActorStatus status)
    {
        if (status.ID == (uint)SID.Doom && !_doomed.Contains(actor))
            _doomed.Add(actor);
    }

    public override void OnStatusLose(Actor actor, ActorStatus status)
    {
        if (status.ID == (uint)SID.Doom)
            _doomed.Remove(actor);
    }

    public override void AddHints(int slot, Actor actor, TextHints hints)
    {
        if (_doomed.Count == 0)
            return;
        if (_doomed.Contains(actor))
            hints.Add(actor.Role == Role.Healer ? Loc.T("Heal yourself to full! (Doom).") : Loc.T("You were doomed! Get healed to full fast."));
        else if (actor.Role == Role.Healer)
            for (var i = 0; i < _doomed.Count; ++i)
                hints.Add($"Heal to full {_doomed[i].Name}! (Doom)");
    }
}

sealed class D303ManxomeMolaaJaJaStates : StateMachineBuilder
{
    public D303ManxomeMolaaJaJaStates(BossModule module) : base(module)
    {
        TrivialPhase()
            .ActivateOnEnter<MiasmaCircle>()
            .ActivateOnEnter<MiasmaBurst>()
            .ActivateOnEnter<Doom>()
            .ActivateOnEnter<Components.UnclaimedCastAOEs>();
    }
}

// 場地沿用沒有模組時的做法（跟著玩家走的方框）；牆由整座副本的障礙物圖（188.30）提供。
[ModuleInfo(BossModuleInfo.Maturity.WIP, Contributors = "ffxiv-tc-port", GroupType = BossModuleInfo.GroupType.CFC, GroupID = 30, NameID = 3095)]
public sealed class D303ManxomeMolaaJaJa(WorldState ws, Actor primary) : SimpleBossModule(ws, primary)
{
    // 祭器出現並連線首領時要盡快打掉 ⇒ 祭器優先。
    protected override void CalculateModuleAIHints(int slot, Actor actor, PartyRolesConfig.Assignment assignment, AIHints hints)
    {
        base.CalculateModuleAIHints(slot, actor, assignment, hints);
        var count = hints.PotentialTargets.Count;
        for (var i = 0; i < count; ++i)
        {
            var e = hints.PotentialTargets[i];
            if (e.Actor.OID == (uint)OID.SacredIdol)
                e.Priority = 1;
        }
    }

    protected override void DrawEnemies(int pcSlot, Actor pc)
    {
        Arena.Actor(PrimaryActor);
        Arena.Actors(Enemies((uint)OID.SacredIdol));
    }
}
