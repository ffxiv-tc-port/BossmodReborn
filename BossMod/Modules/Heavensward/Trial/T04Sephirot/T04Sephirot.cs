namespace BossMod.Heavensward.Trial.T04Sephirot;

// 首領 OID 沒有錄影：台服 BNpcBase 用賽菲羅特模型（ModelChara 1345）的列除了幻版 0x39DA 還有三個，分不出哪個是這場，三個都註冊。
public enum OID : uint
{
    BossCandidateA = 0x14E2,
    BossCandidateB = 0x15B3,
    BossCandidateC = 0x15E9,
}

// 只列有元件處理的技能；其餘讀條（虛空、魔神之嘆、上升氣流…）交給 UnclaimedCastAOEs 照沒有模組時的猜法。
public enum AID : uint
{
    Chesed = 5851, // Boss->player, 4.0s cast, single-target tankbuster
    Ratzon = 5854, // ->player, no cast, range 5 circle spread
    FiendishRage = 5855, // Boss->player, no cast, range 6 circle stack
    Daat = 5857, // Boss->player, 6.0s cast, range 5 circle
    EarthShaker = 5863, // Boss->self, no cast, range 60 cone
    PillarOfMercy = 5866, // ->self, 4.5s cast, range 5 circle + knockback
    Malkuth = 5867, // Boss->self, 6.0s cast, range 80 circle knockback
}

public enum IconID : uint
{
    EarthShaker = 0x28,
    Ratzon = 0x46,
    FiendishRage = 0x48,
}

class Chesed(BossModule module) : Components.SingleTargetCast(module, (uint)AID.Chesed);
class Ratzon(BossModule module) : Components.SpreadFromIcon(module, (uint)IconID.Ratzon, (uint)AID.Ratzon, 5f, 5.1d);
class FiendishRage(BossModule module) : Components.StackWithIcon(module, (uint)IconID.FiendishRage, (uint)AID.FiendishRage, 6f, 5.1d, PartyState.MaxPartySize, PartyState.MaxPartySize);
class Daat(BossModule module) : Components.SpreadFromCastTargets(module, (uint)AID.Daat, 5f);
class PillarOfMercy(BossModule module) : Components.SimpleAOEs(module, (uint)AID.PillarOfMercy, 5f);
// 擊退距離照同模型幻版模組（17、25）；一般難度的距離沒有證據，寧可估長。
class PillarOfMercyKnockback(BossModule module) : Components.SimpleKnockbacks(module, (uint)AID.PillarOfMercy, 17f);
class Malkuth(BossModule module) : Components.SimpleKnockbacks(module, (uint)AID.Malkuth, 25f);

// 第三階段的賽菲羅特可能換成另一個 OID（幻版就是），所以依名字找場上可選取的那一個當扇形原點。
class EarthShaker(BossModule module) : Components.BaitAwayIcon(module, new AOEShapeCone(60f, 15f.Degrees()), (uint)IconID.EarthShaker, (uint)AID.EarthShaker)
{
    public override Actor? BaitSource(Actor target)
    {
        foreach (var a in WorldState.Actors)
            if (a.NameID == T04Sephirot.SephirotNameID && a.IsTargetable && !a.IsDead)
                return a;
        return null;
    }
}

// 狀態機不排時間軸：每個元件都由事件驅動。
class T04SephirotStates : StateMachineBuilder
{
    public T04SephirotStates(BossModule module) : base(module)
    {
        TrivialPhase()
            .ActivateOnEnter<Chesed>()
            .ActivateOnEnter<Ratzon>()
            .ActivateOnEnter<FiendishRage>()
            .ActivateOnEnter<Daat>()
            .ActivateOnEnter<PillarOfMercy>()
            .ActivateOnEnter<PillarOfMercyKnockback>()
            .ActivateOnEnter<Malkuth>()
            .ActivateOnEnter<EarthShaker>()
            .ActivateOnEnter<Components.UnclaimedCastAOEs>();
    }
}

public abstract class T04Sephirot(WorldState ws, Actor primary) : UnverifiedOidTrialModule(ws, primary, default, new ArenaBoundsCircle(19.5f))
{
    public const uint SephirotNameID = 4776;
}

[ModuleInfo(BossModuleInfo.Maturity.WIP, StatesType = typeof(T04SephirotStates), PrimaryActorOID = (uint)OID.BossCandidateA, Contributors = "ffxiv-tc-port", GroupType = BossModuleInfo.GroupType.CFC, GroupID = 134, NameID = SephirotNameID)]
public sealed class T04SephirotA(WorldState ws, Actor primary) : T04Sephirot(ws, primary);

[ModuleInfo(BossModuleInfo.Maturity.WIP, StatesType = typeof(T04SephirotStates), PrimaryActorOID = (uint)OID.BossCandidateB, Contributors = "ffxiv-tc-port", GroupType = BossModuleInfo.GroupType.CFC, GroupID = 134, NameID = SephirotNameID)]
public sealed class T04SephirotB(WorldState ws, Actor primary) : T04Sephirot(ws, primary);

[ModuleInfo(BossModuleInfo.Maturity.WIP, StatesType = typeof(T04SephirotStates), PrimaryActorOID = (uint)OID.BossCandidateC, Contributors = "ffxiv-tc-port", GroupType = BossModuleInfo.GroupType.CFC, GroupID = 134, NameID = SephirotNameID)]
public sealed class T04SephirotC(WorldState ws, Actor primary) : T04Sephirot(ws, primary);
