namespace BossMod.Stormblood.Dungeon.D11FractalContinuumHard.D113UltimaBeast;

public enum OID : uint
{
    Boss = 0x20C8 // 究極神獸
}

// 契約：這個列舉＝模組知道的技能。列舉以外的敵方讀條由 Components.UnclaimedCastAOEs 照沒有模組時的猜法處理，
// 所以刻意不把其他技能列進來（列了就不再被猜）。
public enum AID : uint
{
    AetherBend1 = 10159, // Boss->self, 6.0s cast, range 30 donut, safe inside the hitbox
    AetherBend2 = 10160, // Boss->self, 6.0s cast, range 30 donut, safe inside the hitbox
    DemiUltima = 10162, // Boss->self, 6.0s cast, range 40 circle raidwide
    AllaganGravityVisual = 10165, // Boss->self, 5.5s cast, single-target, every player gets a circle
    AllaganGravity = 10169 // Boss->location, no cast, range 6 circle
}

// 乙太曲折在 Action 表是範圍 30 的圓（後備當全體傷害不畫）；攻略：王 hitbox 以外都會中 ⇒ 認領成內徑＝hitbox 的甜甜圈。
sealed class AetherBend(BossModule module) : Components.GenericAOEs(module)
{
    private readonly List<AOEInstance> _aoes = new(1);

    public override ReadOnlySpan<AOEInstance> ActiveAOEs(int slot, Actor actor) => CollectionsMarshal.AsSpan(_aoes);

    public override void OnCastStarted(Actor caster, ActorCastInfo spell)
    {
        if (spell.Action.ID is (uint)AID.AetherBend1 or (uint)AID.AetherBend2)
            _aoes.Add(new(new AOEShapeDonut(caster.HitboxRadius, 30f), caster.Position.Quantized(), default, Module.CastFinishAt(spell), actorID: caster.InstanceID));
    }

    public override void OnCastFinished(Actor caster, ActorCastInfo spell)
    {
        if (spell.Action.ID is (uint)AID.AetherBend1 or (uint)AID.AetherBend2)
            _aoes.RemoveAll(a => a.ActorID == caster.InstanceID);
    }
}

sealed class DemiUltima(BossModule module) : Components.RaidwideCast(module, (uint)AID.DemiUltima);

// 亞拉戈重力讀條時每個人腳下都會落一個 6 碼圓 ⇒ 全員散開，第一發判定就全部清掉（同時落下）。
sealed class AllaganGravity(BossModule module) : Components.UniformStackSpread(module, default, 6f, alwaysShowSpreads: true)
{
    public override void OnCastStarted(Actor caster, ActorCastInfo spell)
    {
        if (spell.Action.ID == (uint)AID.AllaganGravityVisual)
            AddSpreads(Raid.WithoutSlot(), Module.CastFinishAt(spell));
    }

    public override void OnEventCast(Actor caster, ActorCastEvent spell)
    {
        if (spell.Action.ID == (uint)AID.AllaganGravity)
            Spreads.Clear();
    }

    // 判定事件沒收到時不讓散開永遠留著。
    public override void Update()
    {
        if (Spreads.Count != 0 && WorldState.CurrentTime > Spreads[0].Activation.AddSeconds(3d))
            Spreads.Clear();
    }
}

sealed class D113UltimaBeastStates : StateMachineBuilder
{
    public D113UltimaBeastStates(BossModule module) : base(module)
    {
        TrivialPhase()
            .ActivateOnEnter<AetherBend>()
            .ActivateOnEnter<DemiUltima>()
            .ActivateOnEnter<AllaganGravity>()
            .ActivateOnEnter<Components.UnclaimedCastAOEs>();
    }
}

// 場地與尋路沿用沒有模組時的做法（跟著玩家走的方框）：這個副本沒有障礙物圖，也沒有錄影可量場地。
// 刻意不做亞拉戈火光與耀星（距離衰減，半徑沒有證據）、耀星留下的岩漿（物件 ID 沒有證據）。
[ModuleInfo(BossModuleInfo.Maturity.WIP, Contributors = "ffxiv-tc-port", GroupType = BossModuleInfo.GroupType.CFC, GroupID = 285, NameID = 7058)]
public sealed class D113UltimaBeast(WorldState ws, Actor primary) : SimpleBossModule(ws, primary);
