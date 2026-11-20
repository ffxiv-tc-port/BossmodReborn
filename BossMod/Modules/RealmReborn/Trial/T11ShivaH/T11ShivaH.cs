namespace BossMod.RealmReborn.Trial.T11ShivaH;

// 首領 OID 沒有錄影：台服 BNpcBase 用濕婆模型（ModelChara 312）的列只有這兩個是 2.x 時期，分不出哪個是真、哪個是極，兩個都註冊。
public enum OID : uint
{
    BossCandidateA = 0xC1C,
    BossCandidateB = 0xD01,
}

// 只列有元件處理或刻意不處理的技能；其餘讀條（冰柱衝擊、冰河怒擊…）交給 UnclaimedCastAOEs 照沒有模組時的猜法。
public enum AID : uint
{
    Hailstorm = 2456, // ->player, 3.0s cast, range 6 circle spread
    Permafrost = 2457, // ->location, 1.0s cast, range 20 circle；地面結冰，刻意不當成要躲的範圍
    DiamondDust = 2466, // Boss->self, 9.0s cast, range 60 circle
}

public enum SID : uint
{
    ThinIce604 = 604, // 冰面
    ThinIce911 = 911, // 冰面
    ThinIce1579 = 1579, // 冰面
    ThinIce1580 = 1580, // 冰面
}

public enum IconID : uint
{
    Hailstorm = 0x1D,
}

class Hailstorm(BossModule module) : Components.SpreadFromIcon(module, (uint)IconID.Hailstorm, (uint)AID.Hailstorm, 6f, 5.1d);
class DiamondDust(BossModule module) : Components.RaidwideCast(module, (uint)AID.DiamondDust);

// 冰面：身上有「站在結冰的地面上，一旦移動便會滑開」的狀態時不要移動。台服有四個同名狀態，不確定這場用哪一個，全部都認。
sealed class ThinIce(BossModule module) : Components.StayMove(module)
{
    public override void OnStatusGain(Actor actor, ActorStatus status)
    {
        if (IsThinIce(status.ID))
        {
            var state = new PlayerState(Requirement.Stay2, WorldState.CurrentTime);
            SetState(Raid.FindSlot(actor.InstanceID), ref state);
        }
    }

    public override void OnStatusLose(Actor actor, ActorStatus status)
    {
        if (IsThinIce(status.ID))
            ClearState(Raid.FindSlot(actor.InstanceID));
    }

    private static bool IsThinIce(uint sid) => sid is (uint)SID.ThinIce604 or (uint)SID.ThinIce911 or (uint)SID.ThinIce1579 or (uint)SID.ThinIce1580;
}

// 狀態機不排時間軸：每個元件都由事件驅動。
class T11ShivaHStates : StateMachineBuilder
{
    public T11ShivaHStates(BossModule module) : base(module)
    {
        TrivialPhase()
            .ActivateOnEnter<Hailstorm>()
            .ActivateOnEnter<DiamondDust>()
            .ActivateOnEnter<ThinIce>()
            .ActivateOnEnter<Components.UnclaimedCastAOEs>();
    }
}

public abstract class T11ShivaH(WorldState ws, Actor primary) : UnverifiedOidTrialModule(ws, primary, default, new ArenaBoundsCircle(20f));

[ModuleInfo(BossModuleInfo.Maturity.WIP, StatesType = typeof(T11ShivaHStates), PrimaryActorOID = (uint)OID.BossCandidateA, Contributors = "ffxiv-tc-port", GroupType = BossModuleInfo.GroupType.CFC, GroupID = 79, NameID = 2994)]
public sealed class T11ShivaHA(WorldState ws, Actor primary) : T11ShivaH(ws, primary);

[ModuleInfo(BossModuleInfo.Maturity.WIP, StatesType = typeof(T11ShivaHStates), PrimaryActorOID = (uint)OID.BossCandidateB, Contributors = "ffxiv-tc-port", GroupType = BossModuleInfo.GroupType.CFC, GroupID = 79, NameID = 2994)]
public sealed class T11ShivaHB(WorldState ws, Actor primary) : T11ShivaH(ws, primary);
