namespace BossMod.Stormblood.Dungeon.D10HellsLid.D101OtakeMaru;

public enum OID : uint
{
    Boss = 0x20BC // 大岳丸 R5.6
}

// 契約：這個列舉＝模組知道的技能。列舉以外的敵方讀條（100噸迴轉、10噸揮打、噴石）由 Components.UnclaimedCastAOEs 照沒有模組時的猜法處理，
// 所以刻意不把其他技能列進來（列了就不再被猜）。
public enum AID : uint
{
    DisrobeBurn = 10176, // Helper->player, no cast, 王身邊每秒一跳（玄武熔岩）
    Disrobe1 = 10177, // Boss->self, no cast
    Disrobe2 = 10178, // Helper->location, 3.0s cast, range 4 circle，標出之後要跳過去的落點
    StoneCudgel = 10179, // Boss->self, no cast, range 6+R circle
    EruptiveLeap = 10180 // Boss->location, no cast
}

// 徒手（Disrobe1）之後到下一次噴火飛び，王身邊每秒一跳：離王中心 ≤4.9 被打、≥5.6 沒被打（王 hitbox 5.6）。
sealed class DisrobeBurn(BossModule module) : Components.GenericAOEs(module)
{
    private static readonly AOEShapeCircle circle = new(6f);
    private readonly AOEInstance[] _aoe = new AOEInstance[1];
    private DateTime _start;
    private DateTime _end;

    public override ReadOnlySpan<AOEInstance> ActiveAOEs(int slot, Actor actor)
    {
        if (_end == default)
            return [];
        _aoe[0] = new(circle, Module.PrimaryActor.Position, default, _start);
        return _aoe;
    }

    public override void Update()
    {
        if (_end != default && WorldState.CurrentTime > _end)
            _end = default;
    }

    public override void OnEventCast(Actor caster, ActorCastEvent spell)
    {
        switch (spell.Action.ID)
        {
            case (uint)AID.Disrobe1:
                _start = WorldState.FutureTime(1.9d);
                _end = WorldState.FutureTime(14d); // 保險上限；正常由下一次噴火飛び結束
                break;
            case (uint)AID.EruptiveLeap:
                _end = default;
                break;
        }
    }
}

// 徒手（Disrobe2）標出的落點，王之後跳過去放岩棍。只有一輪錄影、先跳哪一個看不出來 ⇒ 兩個都用第一跳的時間。
sealed class StoneCudgel(BossModule module) : Components.GenericAOEs(module)
{
    private static readonly AOEShapeCircle circle = new(11.6f);
    private readonly List<AOEInstance> _aoes = new(2);
    private DateTime _expire;

    public override ReadOnlySpan<AOEInstance> ActiveAOEs(int slot, Actor actor) => CollectionsMarshal.AsSpan(_aoes);

    public override void Update()
    {
        if (_aoes.Count != 0 && WorldState.CurrentTime > _expire)
            _aoes.Clear();
    }

    public override void OnCastStarted(Actor caster, ActorCastInfo spell)
    {
        if (spell.Action.ID == (uint)AID.Disrobe2)
        {
            _aoes.Add(new(circle, spell.LocXZ, default, Module.CastFinishAt(spell, 8.3d)));
            _expire = Module.CastFinishAt(spell, 16d);
        }
    }

    public override void OnEventCast(Actor caster, ActorCastEvent spell)
    {
        if (spell.Action.ID == (uint)AID.StoneCudgel)
        {
            var pos = caster.Position;
            for (var i = _aoes.Count - 1; i >= 0; --i)
                if (_aoes[i].Origin.InCircle(pos, 3f))
                    _aoes.RemoveAt(i);
        }
    }
}

sealed class Disrobe2(BossModule module) : Components.SimpleAOEs(module, (uint)AID.Disrobe2, 4f);

sealed class D101OtakeMaruStates : StateMachineBuilder
{
    public D101OtakeMaruStates(BossModule module) : base(module)
    {
        TrivialPhase()
            .ActivateOnEnter<DisrobeBurn>()
            .ActivateOnEnter<Disrobe2>()
            .ActivateOnEnter<StoneCudgel>()
            .ActivateOnEnter<Components.UnclaimedCastAOEs>();
    }
}

// 場地與尋路沿用沒有模組時的做法（跟著玩家走的方框）：這個副本沒有障礙物圖。
// 刻意不做噴火飛び的頭上標記（跳到被點名的人身上，落地本身沒有傷害）。
[ModuleInfo(BossModuleInfo.Maturity.WIP, Contributors = "ffxiv-tc-port", GroupType = BossModuleInfo.GroupType.CFC, GroupID = 284, NameID = 6994)]
public sealed class D101OtakeMaru(WorldState ws, Actor primary) : SimpleBossModule(ws, primary);
