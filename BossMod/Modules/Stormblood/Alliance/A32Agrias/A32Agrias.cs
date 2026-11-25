namespace BossMod.Stormblood.Alliance.A32Agrias;

class DivineLight(BossModule module) : Components.RaidwideCast(module, (uint)AID.DivineLight);
class NorthswainsStrikeEphemeralKnight(BossModule module) : Components.SimpleAOEs(module, (uint)AID.NorthswainsStrikeEphemeralKnight, new AOEShapeRect(60, 3));
class CleansingFlameSpread(BossModule module) : Components.SpreadFromCastTargets(module, (uint)AID.CleansingFlameSpread, 6);
sealed class MortalBlow(BossModule module) : Components.SimpleAOEs(module, (uint)AID.MortalBlow, 30f);
sealed class HeavenlyJudgment(BossModule module) : Components.RaidwideCast(module, (uint)AID.HeavenlyJudgment2);

// 同一個落點先後各放一次 15 碼圓與 15～30 碼環，順序不固定、間隔約 4.6 秒 ⇒ 第一發讀條時就把第二發也畫出來。
sealed class HallowedBolt(BossModule module) : Components.GenericAOEs(module)
{
    private static readonly AOEShapeCircle _circle = new(15f);
    private static readonly AOEShapeDonut _donut = new(15f, 30f);
    private const double SecondDelay = 4.6d;
    private readonly List<AOEInstance> _aoes = [];

    public override ReadOnlySpan<AOEInstance> ActiveAOEs(int slot, Actor actor) => CollectionsMarshal.AsSpan(_aoes);

    public override void OnCastStarted(Actor caster, ActorCastInfo spell)
    {
        AOEShape? shape = spell.Action.ID switch
        {
            (uint)AID.HallowedBoltAOE => _circle,
            (uint)AID.HallowedBoltDonut => _donut,
            _ => null
        };
        if (shape == null)
            return;
        var finish = Module.CastFinishAt(spell);
        var aoe = new AOEInstance(shape, spell.LocXZ, default, finish, actorID: caster.InstanceID);
        // 預測的那一發 ActorID 為 0；第二發真的讀條時取代它，不再追加預測。
        for (var i = 0; i < _aoes.Count; ++i)
        {
            if (_aoes[i].ActorID == 0 && _aoes[i].Shape == shape && _aoes[i].Origin.InCircle(spell.LocXZ, 1f))
            {
                _aoes[i] = aoe;
                Recolor();
                return;
            }
        }
        _aoes.Add(aoe);
        _aoes.Add(new(shape == _circle ? _donut : _circle, spell.LocXZ, default, finish.AddSeconds(SecondDelay)));
        Recolor();
    }

    public override void OnCastFinished(Actor caster, ActorCastInfo spell)
    {
        if (spell.Action.ID is not ((uint)AID.HallowedBoltAOE or (uint)AID.HallowedBoltDonut))
            return;
        for (var i = 0; i < _aoes.Count; ++i)
        {
            if (_aoes[i].ActorID == caster.InstanceID)
            {
                _aoes.RemoveAt(i);
                Recolor();
                return;
            }
        }
    }

    // 預測的第二發沒等到讀條，就在預定時間過 1 秒後放掉。
    public override void Update()
    {
        var expired = WorldState.FutureTime(-1d);
        for (var i = _aoes.Count - 1; i >= 0; --i)
            if (_aoes[i].ActorID == 0 && _aoes[i].Activation < expired)
                _aoes.RemoveAt(i);
    }

    // 下一批判定用危險色、之後的用一般色。
    private void Recolor()
    {
        var first = DateTime.MaxValue;
        for (var i = 0; i < _aoes.Count; ++i)
            if (_aoes[i].Activation < first)
                first = _aoes[i].Activation;
        var span = CollectionsMarshal.AsSpan(_aoes);
        for (var i = 0; i < span.Length; ++i)
            span[i].Color = span[i].Activation < first.AddSeconds(1d) ? Colors.Danger : default;
    }
}

// 持有聖盾的人面向阿格莉亞絲按任務指令「聖盾防禦」，才擋得下不動無明劍與聖光爆裂破。
// 聖盾防禦和武裝戍衛同類（狀態旗標相同）：用技能、開始詠唱、移動、轉向都會解除 ⇒
// 還沒舉盾時立刻把人轉向首領；舉盾之後不再轉向，改給熱病模式讓 BMR 不出招、不移動。
// 熱病模式只在舉盾之後才給：給早了會連「聖盾防禦」本身都被手動佇列擋下。
// 按下到狀態出現約 0.4 秒，下一招正好在這裡送出 ⇒ 按下後 1 秒內也算已舉盾（模組轉送玩家的施放事件）。
sealed class HolyShieldBlock(BossModule module) : Components.GenericGaze(module)
{
    private readonly List<(Actor Caster, DateTime Activation, uint ResolveAID)> _pending = [];
    private readonly Dictionary<ulong, DateTime> _pressedAt = [];

    private bool Raised(Actor actor) => actor.FindStatus((uint)SID.HolyShield) != null
        || _pressedAt.TryGetValue(actor.InstanceID, out var t) && t > WorldState.CurrentTime.AddSeconds(-1d);
    private bool HasShield(Actor actor) => Raised(actor) || actor.FindStatus((uint)SID.Shieldbearer) != null;

    // 舉著盾再按一次是重新舉盾，不是放下：狀態當下消失、約 0.4 秒後回來，中間仍要算已舉盾。
    public void OnShieldPressed(Actor actor) => _pressedAt[actor.InstanceID] = WorldState.CurrentTime;

    // 狀態到了就以狀態為準：之後被解除要立刻算放下，不能再靠按下的那 1 秒撐著。
    public override void OnStatusGain(Actor actor, ActorStatus status)
    {
        if (status.ID == (uint)SID.HolyShield)
            _pressedAt.Remove(actor.InstanceID);
    }

    // 聖光爆裂破的直線比讀條結束晚一點才判定 ⇒ 維持到判定那一發出現（沒出現就在預定時間 1 秒後放掉）。
    private (Actor Caster, DateTime Activation)? Next()
    {
        var expired = WorldState.CurrentTime.AddSeconds(-1d);
        for (var i = 0; i < _pending.Count; ++i)
            if (_pending[i].Activation > expired)
                return (_pending[i].Caster, _pending[i].Activation);
        return null;
    }

    public override ReadOnlySpan<Eye> ActiveEyes(int slot, Actor actor)
    {
        if (!HasShield(actor) || Next() is not var (caster, activation))
            return [];
        return new Eye[1] { new(caster.Position, Raised(actor) ? activation : WorldState.CurrentTime, inverted: true) };
    }

    public override void OnCastStarted(Actor caster, ActorCastInfo spell)
    {
        var resolve = spell.Action.ID switch
        {
            (uint)AID.JudgmentBlade => (uint)AID.JudgmentBlade,
            (uint)AID.DivineRuination1 => (uint)AID.DivineRuination2,
            _ => 0u
        };
        if (resolve == 0)
            return;
        var expired = WorldState.CurrentTime.AddSeconds(-1d);
        _pending.RemoveAll(p => p.Activation <= expired);
        _pending.Add((caster, Module.CastFinishAt(spell), resolve));
    }

    public override void OnEventCast(Actor caster, ActorCastEvent spell)
    {
        for (var i = _pending.Count - 1; i >= 0; --i)
            if (_pending[i].ResolveAID == spell.Action.ID)
                _pending.RemoveAt(i);
    }

    public override void AddHints(int slot, Actor actor, TextHints hints)
    {
        if (!HasShield(actor) || Next() is not var (c, _))
            return;
        if (!Raised(actor))
            hints.Add(string.Format(Loc.T("Face {0}, then use the duty action Holy Shield!"), c.Name));
        else if (!HitByEye(ref actor, ActiveEyes(slot, actor)[0]))
            hints.Add(string.Format(Loc.T("Holy Shield is not facing {0}!"), c.Name));
        else
            hints.Add(Loc.T("Holy Shield raised: do not act, move or turn until it resolves!"), false);
    }

    public override void AddAIHints(int slot, Actor actor, PartyRolesConfig.Assignment assignment, AIHints hints)
    {
        if (Next() == null)
            return;
        if (!Raised(actor))
            base.AddAIHints(slot, actor, assignment, hints);
        else
            hints.AddSpecialMode(AIHints.SpecialMode.Pyretic, WorldState.CurrentTime);
    }
}

// 聖光爆裂破：從阿格莉亞絲往每個連線者各拉一條直線；線上的人沒舉盾（見 HolyShieldBlock）就會被打中。
// 不預告成死刑：預告會讓別的插件替線上的人放減傷，而任何技能都會解除聖盾防禦。
sealed class DivineRuination : Components.BaitAwayTethers
{
    public DivineRuination(BossModule module) : base(module, new AOEShapeRect(60f, 3f), (uint)TetherID.Tether2, (uint)AID.DivineRuination2, (uint)OID.Boss, 8.1d)
    {
        DamageType = AIHints.PredictedDamageType.None;
    }
}

// Emblazoned Shields tethered to the Shield Knight keep it invulnerable; they are broken with the Swordbearer duty action.
// Deliberately hint-only: the duty action is never pressed for the player (positioning and facing in a 24-player fight).
sealed class ShieldKnightInvulnerable(BossModule module) : BossComponent(module)
{
    private readonly List<Actor> _knights = module.Enemies((uint)OID.ShieldKnight);

    private Actor? Invulnerable()
    {
        for (var i = 0; i < _knights.Count; ++i)
        {
            var k = _knights[i];
            if (!k.IsDead && k.FindStatus((uint)SID.Invincibility2) != null)
                return k;
        }
        return null;
    }

    public override void AddGlobalHints(GlobalHints hints)
    {
        if (Invulnerable() is Actor k)
            hints.Add(string.Format(Loc.T("{0} is invulnerable: take the sword from the silver circle and break the tethered shields with the duty action!"), k.Name));
    }

    public override void AddHints(int slot, Actor actor, TextHints hints)
    {
        if (actor.FindStatus((uint)SID.Swordbearer) != null && Invulnerable() != null)
            hints.Add(Loc.T("You hold the sword: face the tethered shields and use the duty action!"), false);
    }

    public override void AddAIHints(int slot, Actor actor, PartyRolesConfig.Assignment assignment, AIHints hints)
    {
        for (var i = 0; i < _knights.Count; ++i)
        {
            var k = _knights[i];
            if (k.FindStatus((uint)SID.Invincibility2) != null && hints.FindEnemy(k) is AIHints.Enemy e && e.Priority > AIHints.Enemy.PriorityInvincible)
                e.Priority = AIHints.Enemy.PriorityInvincible;
        }
    }
}

[ModuleInfo(BossModuleInfo.Maturity.WIP, Contributors = "The Combat Reborn Team", GroupType = BossModuleInfo.GroupType.CFC, GroupID = 636, NameID = 7916)]
public class A32Agrias : BossModule
{
    // 元件收不到玩家的施放事件（BossModule 只轉送非玩家的），聖盾防禦的按下要由模組轉給 HolyShieldBlock。
    private readonly EventSubscription _shieldPresses;

    public A32Agrias(WorldState ws, Actor primary) : base(ws, primary, new(600, -54), new ArenaBoundsCircle(30))
    {
        _shieldPresses = ws.Actors.CastEvent.Subscribe(OnAnyCastEvent);
    }

    private void OnAnyCastEvent(Actor actor, ActorCastEvent cast)
    {
        if (cast.IsSpell() && cast.Action.ID == (uint)AID.HolyShield && actor.Type == ActorType.Player)
            FindComponent<HolyShieldBlock>()?.OnShieldPressed(actor);
    }

    protected override void Dispose(bool disposing)
    {
        _shieldPresses.Dispose();
        base.Dispose(disposing);
    }
}
