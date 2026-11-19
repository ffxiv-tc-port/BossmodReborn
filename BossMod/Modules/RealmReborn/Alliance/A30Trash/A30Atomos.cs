namespace BossMod.RealmReborn.Alliance.A30Atomos;

public enum OID : uint
{
    Boss = 0xE6E, // R1.000, 至尊亞特蒙斯（場中央、不可選取，三隻亞特蒙斯倒下時一起倒）
    Atomos = 0xDA4, // R4.000, x3（叫聲／奇聲／怒聲的亞特蒙斯，各在一個小隊的平台中央）
    SacrificeSoldier = 0xDA5, // R3.000, x3（祭品士兵）
    Helper = 0x1B2
}

public enum AID : uint
{
    AetherAllocation = 3419, // Boss->Atomos, no cast, single-target
    BlackHole = 3414, // Atomos->self, no cast, range 15+R 120-degree cone, attract
    WhiteHole = 3415, // Atomos->self, 5.7s cast, 不傷人（列在這裡讓後備猜測不要把它畫成扇形）
    WhiteHoleHit = 3438, // Helper->player, no cast, 把被黑洞吞下的人吐出來
    ImpactFrenzy = 3416, // Atomos->self, 3.7s cast, range 19 circle
}

public enum IconID : uint
{
    BlackHole = 1, // player
}

sealed class ImpactFrenzy(BossModule module) : Components.SimpleAOEs(module, (uint)AID.ImpactFrenzy, 19f);

// 黑洞沒有讀條：頭上標記約 5.5 秒後，離目標最近的亞特蒙斯當下轉向目標放扇形吸引，所以只能照標記預判。
sealed class BlackHole(BossModule module) : Components.GenericBaitAway(module, (uint)AID.BlackHole)
{
    private static readonly AOEShapeCone cone = new(19f, 60f.Degrees());

    public override void OnEventIcon(Actor actor, uint iconID, ulong targetID)
    {
        if (iconID != (uint)IconID.BlackHole)
            return;
        var target = WorldState.Actors.Find(targetID) ?? actor;
        var atomos = Module.Enemies((uint)OID.Atomos);
        Actor? source = null;
        var best = float.MaxValue;
        for (var i = 0; i < atomos.Count; ++i)
        {
            var d = (atomos[i].Position - target.Position).LengthSq();
            if (d < best)
            {
                best = d;
                source = atomos[i];
            }
        }
        if (source != null)
            CurrentBaits.Add(new(source, target, cone, WorldState.FutureTime(5.5d)));
    }

    public override void OnEventCast(Actor caster, ActorCastEvent spell)
    {
        if (spell.Action.ID != WatchedAction)
            return;
        ++NumCasts;
        var id = caster.InstanceID;
        for (var i = 0; i < CurrentBaits.Count; ++i)
        {
            if (CurrentBaits[i].Source.InstanceID == id)
            {
                CurrentBaits.RemoveAt(i);
                return;
            }
        }
    }
}

sealed class A30AtomosStates : StateMachineBuilder
{
    public A30AtomosStates(BossModule module) : base(module)
    {
        TrivialPhase()
            .ActivateOnEnter<ImpactFrenzy>()
            .ActivateOnEnter<BlackHole>();
    }
}

[ModuleInfo(BossModuleInfo.Maturity.Contributed, Contributors = "ffxiv-tc-port", GroupType = BossModuleInfo.GroupType.CFC, GroupID = 111, NameID = 3251)]
public sealed class A30Atomos(WorldState ws, Actor primary) : BossModule(ws, primary, arena.Center, arena)
{
    // 三個小隊各在一間圓形房（亞特蒙斯所在的平台＋外圈下層地面，碰撞資料量得半徑約 25），彼此不相連。
    private static readonly ArenaBoundsComplex arena = new([new Polygon(new(-31.172f, 49f), 24f, 48), new Polygon(new(0f, -5f), 24f, 48), new Polygon(new(31.172f, 49f), 24f, 48)]);

    // 主體不可選取，用三隻亞特蒙斯有沒有進戰鬥判斷開打。
    protected override bool CheckPull()
    {
        var atomos = Enemies((uint)OID.Atomos);
        for (var i = 0; i < atomos.Count; ++i)
        {
            if (atomos[i].InCombat)
                return true;
        }
        return false;
    }

    protected override void DrawEnemies(int pcSlot, Actor pc)
    {
        Arena.Actors(Enemies((uint)OID.Atomos));
        Arena.Actors(Enemies((uint)OID.SacrificeSoldier));
    }
}
