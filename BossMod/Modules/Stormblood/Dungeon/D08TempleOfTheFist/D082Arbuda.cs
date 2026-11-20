namespace BossMod.Stormblood.Dungeon.D08TempleOfTheFist.D082Arbuda;

public enum OID : uint
{
    Boss = 0x1B55 // 額部陀
}

// 契約：這個列舉＝模組知道的技能。列舉以外的敵方讀條由 Components.UnclaimedCastAOEs 照沒有模組時的猜法處理，
// 所以刻意不把其他技能列進來（列了就不再被猜）。
public enum AID : uint
{
    FourfoldShear = 8153, // Boss->player, 4.0s cast, single-target tankbuster
    ForeAndAft = 8155, // Boss->self, 3.8s cast, single-target, front and back cones follow
    PortAndStar = 8156, // Boss->self, 3.8s cast, single-target, left and right cones follow
    DoubleSlash = 8157 // ?->self, 4.0s cast, range 30 cone, no omen
}

sealed class FourfoldShear(BossModule module) : Components.SingleTargetCast(module, (uint)AID.FourfoldShear);

// 雙斬沒有預兆、Action 表也沒有角度，後備會退回 180° 扇形（前後兩發＝整圈危險）⇒ 認領，改看首領讀條定方向。
// 半角取 60°：攻略說站在斜角 45° 方便調整，實際角度沒有證據，取比 45° 寬（AI 站側面正中或前後正中）。
sealed class DoubleSlash(BossModule module) : Components.GenericAOEs(module)
{
    private readonly List<AOEInstance> _aoes = new(2);

    public override ReadOnlySpan<AOEInstance> ActiveAOEs(int slot, Actor actor) => CollectionsMarshal.AsSpan(_aoes);

    public override void OnCastStarted(Actor caster, ActorCastInfo spell)
    {
        var offset = spell.Action.ID switch
        {
            (uint)AID.ForeAndAft => 0f,
            (uint)AID.PortAndStar => 90f,
            _ => -1f
        };
        if (offset < 0f)
            return;
        var shape = new AOEShapeCone(30f + caster.HitboxRadius, 60f.Degrees());
        var activation = Module.CastFinishAt(spell, 0.2d);
        var pos = caster.Position.Quantized();
        _aoes.Clear();
        _aoes.Add(new(shape, pos, spell.Rotation + offset.Degrees(), activation));
        _aoes.Add(new(shape, pos, spell.Rotation + (offset + 180f).Degrees(), activation));
    }

    public override void OnEventCast(Actor caster, ActorCastEvent spell)
    {
        if (spell.Action.ID == (uint)AID.DoubleSlash)
            _aoes.Clear();
    }

    // 雙斬的判定事件沒收到時（施放者或 ID 對不上）不讓危險區永遠留著。
    public override void Update()
    {
        if (_aoes.Count != 0 && WorldState.CurrentTime > _aoes[0].Activation.AddSeconds(3d))
            _aoes.Clear();
    }
}

sealed class D082ArbudaStates : StateMachineBuilder
{
    public D082ArbudaStates(BossModule module) : base(module)
    {
        TrivialPhase()
            .ActivateOnEnter<FourfoldShear>()
            .ActivateOnEnter<DoubleSlash>()
            .ActivateOnEnter<Components.UnclaimedCastAOEs>();
    }
}

// 場地與尋路沿用沒有模組時的做法（跟著玩家走的方框）：這個副本沒有障礙物圖，也沒有錄影可量場地。
// 刻意不做預判（只有一面可打）與試煉刻印（站對應顏色的地板）：方向與地板位置沒有 ID 證據。
[ModuleInfo(BossModuleInfo.Maturity.WIP, Contributors = "ffxiv-tc-port", GroupType = BossModuleInfo.GroupType.CFC, GroupID = 236, NameID = 6118)]
public sealed class D082Arbuda(WorldState ws, Actor primary) : SimpleBossModule(ws, primary);
