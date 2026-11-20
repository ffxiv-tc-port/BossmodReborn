namespace BossMod.Stormblood.Dungeon.D14SaintMociannesArboretumHard.D142Lakhamu;

public enum OID : uint
{
    Boss = 0x2429, // 拉哈穆
    SiltGolem = 0x242A // 淤泥魔像
}

// 契約：這個列舉＝模組知道的技能。列舉以外的敵方讀條由 Components.UnclaimedCastAOEs 照沒有模組時的猜法處理，
// 所以刻意不把其他技能列進來（列了就不再被猜）。
public enum AID : uint
{
    StoneII = 12586, // Boss->player, 3.5s cast, single-target tankbuster
    EmptyGaze = 12587, // Boss->self, 3.5s cast, range 65 circle gaze
    Tectonics = 12588, // Boss->self, 3.5s cast, range 70 circle raidwide
    EarthquakeOuter = 12591 // Boss->self, 2.5s cast, range 10-20 donut
}

sealed class StoneII(BossModule module) : Components.SingleTargetCast(module, (uint)AID.StoneII);
sealed class EmptyGaze(BossModule module) : Components.CastGaze(module, (uint)AID.EmptyGaze);
sealed class Tectonics(BossModule module) : Components.RaidwideCast(module, (uint)AID.Tectonics);

// 地震（外圈）CastType 5 是圓、預兆 gl_sircle_2010 是甜甜圈；後備會畫成整個圓（AI 跑到 20 碼外）⇒ 認領。
// 外徑照 CastType 5 加施放者 hitbox；內徑取預兆的 10、不加 hitbox（比按比例縮的內徑小，偏保守）。
sealed class EarthquakeOuter(BossModule module) : Components.GenericAOEs(module)
{
    private readonly List<AOEInstance> _aoes = new(1);

    public override ReadOnlySpan<AOEInstance> ActiveAOEs(int slot, Actor actor) => CollectionsMarshal.AsSpan(_aoes);

    public override void OnCastStarted(Actor caster, ActorCastInfo spell)
    {
        if (spell.Action.ID == (uint)AID.EarthquakeOuter)
            _aoes.Add(new(new AOEShapeDonut(10f, 20f + caster.HitboxRadius), spell.LocXZ, default, Module.CastFinishAt(spell), actorID: caster.InstanceID));
    }

    public override void OnCastFinished(Actor caster, ActorCastInfo spell)
    {
        if (spell.Action.ID == (uint)AID.EarthquakeOuter)
            _aoes.RemoveAll(a => a.ActorID == caster.InstanceID);
    }
}

sealed class D142LakhamuStates : StateMachineBuilder
{
    public D142LakhamuStates(BossModule module) : base(module)
    {
        TrivialPhase()
            .ActivateOnEnter<StoneII>()
            .ActivateOnEnter<EmptyGaze>()
            .ActivateOnEnter<Tectonics>()
            .ActivateOnEnter<EarthquakeOuter>()
            .ActivateOnEnter<Components.UnclaimedCastAOEs>();
    }
}

// 場地與尋路沿用沒有模組時的做法（跟著玩家走的方框，套用本副本的障礙物圖）：沒有錄影可量場地。
// 刻意不做大地搖動（扇形角度沒有證據）與滑坡的輸送帶推移（方向與速度沒有證據）。
[ModuleInfo(BossModuleInfo.Maturity.WIP, Contributors = "ffxiv-tc-port", GroupType = BossModuleInfo.GroupType.CFC, GroupID = 584, NameID = 7660)]
public sealed class D142Lakhamu(WorldState ws, Actor primary) : SimpleBossModule(ws, primary)
{
    protected override void DrawEnemies(int pcSlot, Actor pc)
    {
        Arena.Actor(PrimaryActor);
        Arena.Actors(Enemies((uint)OID.SiltGolem), Colors.Object);
    }
}
