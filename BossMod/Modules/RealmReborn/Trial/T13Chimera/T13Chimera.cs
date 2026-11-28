namespace BossMod.RealmReborn.Trial.T13Chimera;

// 本檔假設與契約（只有一場等級同步下很快打完的錄影佐證，數字是近似）：
// - 首領整場不移動；場地以首領出生位置為中心、半徑 30，地形交給障礙物圖。
// - 錄影裡沒出現的招式（例：冰凍咆哮）不列舉，交給 Components.UnclaimedCastAOEs 照沒有模組時的猜法畫。
public enum OID : uint
{
    Boss = 0x6A3, // R3.7
    CacophonyA = 0x8AE, // R2.0，嘈雜的噪音（無序的和聲的施放者）
    CacophonyB = 0x6A4, // R1.0，同名、第二次召喚出現；錄影在它爆開前就打完了
}

public enum AID : uint
{
    AutoAttack = 870, // Boss->player, no cast, single-target
    TheDragonsBreath = 1337, // Boss->self, no cast, range 6+R 120-degree cone
    TheRamsBreath = 1102, // Boss->self, 1.7s cast, range 6+R 120-degree cone
    ScorpionsTail = 1282, // Boss->self, 1.7s cast, range 6+R 90-degree cone
    TheRamsEye1 = 1106, // Boss->location, 2.7s cast, range 6 circle
    TheRamsEye2 = 1339, // Boss->location, 2.2s cast, range 6 circle
    TheDragonsVoice = 1338, // Boss->self, 1.7s cast, range 8-30 donut
    Cacophony1 = 1340, // Boss->self, no cast；召喚嘈雜的噪音
    Cacophony2 = 1107, // Boss->self, no cast；同上
    ChaoticChorus = 1108, // Cacophony->self, no cast, range 6 circle
    Electrify = 1283, // Boss->self, no cast；不處理：自身強化
    Freeze = 1284, // Boss->self, no cast；不處理：自身強化
}

class TheDragonsBreath(BossModule module) : Components.Cleave(module, (uint)AID.TheDragonsBreath, new AOEShapeCone(9.7f, 60f.Degrees()));
class TheRamsBreath(BossModule module) : Components.SimpleAOEs(module, (uint)AID.TheRamsBreath, new AOEShapeCone(9.7f, 60f.Degrees()));
class ScorpionsTail(BossModule module) : Components.SimpleAOEs(module, (uint)AID.ScorpionsTail, new AOEShapeCone(9.7f, 45f.Degrees()));
class TheRamsEye(BossModule module) : Components.SimpleAOEGroups(module, [(uint)AID.TheRamsEye1, (uint)AID.TheRamsEye2], 6f);
// 錄影：判定時離首領中心 9.3～16.1 碼的人中、6.3 碼以內的都沒中。
class TheDragonsVoice(BossModule module) : Components.SimpleAOEs(module, (uint)AID.TheDragonsVoice, new AOEShapeDonut(8f, 30f));

// 嘈雜的噪音出現約 4.7 秒後在原地爆開（無序的和聲，半徑 6）；危險區跟著球的當下位置。
class ChaoticChorus(BossModule module) : Components.GenericAOEs(module, (uint)AID.ChaoticChorus)
{
    private static readonly AOEShapeCircle circle = new(6f);
    private readonly List<Actor> _orbs = new(2);
    private readonly List<AOEInstance> _aoes = new(2);

    public override ReadOnlySpan<AOEInstance> ActiveAOEs(int slot, Actor actor) => CollectionsMarshal.AsSpan(_aoes);

    public override void Update()
    {
        var aoes = CollectionsMarshal.AsSpan(_aoes);
        for (var i = 0; i < aoes.Length; ++i)
            aoes[i].Origin = _orbs[i].Position.Quantized();
    }

    public override void OnActorCreated(Actor actor)
    {
        if (actor.OID is (uint)OID.CacophonyA or (uint)OID.CacophonyB)
        {
            _orbs.Add(actor);
            _aoes.Add(new(circle, actor.Position.Quantized(), default, WorldState.FutureTime(4.7d), actorID: actor.InstanceID));
        }
    }

    public override void OnActorDestroyed(Actor actor) => Remove(actor);

    public override void OnEventCast(Actor caster, ActorCastEvent spell)
    {
        if (spell.Action.ID == WatchedAction)
        {
            ++NumCasts;
            Remove(caster);
        }
    }

    private void Remove(Actor orb)
    {
        var i = _orbs.IndexOf(orb);
        if (i >= 0)
        {
            _orbs.RemoveAt(i);
            _aoes.RemoveAt(i);
        }
    }
}

class T13ChimeraStates : StateMachineBuilder
{
    public T13ChimeraStates(BossModule module) : base(module)
    {
        TrivialPhase()
            .ActivateOnEnter<TheDragonsBreath>()
            .ActivateOnEnter<TheRamsBreath>()
            .ActivateOnEnter<ScorpionsTail>()
            .ActivateOnEnter<TheRamsEye>()
            .ActivateOnEnter<TheDragonsVoice>()
            .ActivateOnEnter<ChaoticChorus>()
            .ActivateOnEnter<Components.UnclaimedCastAOEs>();
    }
}

[ModuleInfo(BossModuleInfo.Maturity.WIP, Contributors = "ffxiv-tc-port", GroupType = BossModuleInfo.GroupType.CFC, GroupID = 74, NameID = 2162)]
public class T13Chimera(WorldState ws, Actor primary) : BossModule(ws, primary, primary.Position, new ArenaBoundsCircle(30f, AllowObstacleMap: true))
{
    protected override void DrawEnemies(int pcSlot, Actor pc)
    {
        Arena.Actor(PrimaryActor);
        Arena.Actors(Enemies((uint)OID.CacophonyA));
        Arena.Actors(Enemies((uint)OID.CacophonyB));
    }
}
