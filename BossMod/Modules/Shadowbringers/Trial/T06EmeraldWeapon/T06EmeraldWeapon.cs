namespace BossMod.Shadowbringers.Trial.T06EmeraldWeapon;

// 首領 OID 沒有錄影：綠寶石武器第一階段的模型（ModelChara 2966）、名字是綠寶石武器的列有三個，分不出哪列是這場，三個都註冊。
// 第二階段的本體換哪個 OID 沒有證據，刻意不註冊（不畫換階段）。
public enum OID : uint
{
    BossCandidateA = 0x303A,
    BossCandidateB = 0x3041,
    BossCandidateC = 0x316D,
}

// 只列有元件處理的技能；其餘讀條交給 UnclaimedCastAOEs 照沒有模組時的猜法。
public enum AID : uint
{
    PhotonLaserDonut1 = 21814, // ?->self, 3.0s cast, range 10-17 donut
    PhotonLaserDonut2 = 21816, // ?->self, 3.0s cast, range 16-23 donut
    DivideEtImperaP1 = 21838, // ?->player, no cast, range 4 circle, tankbuster
    EmeraldShot = 21844, // Boss->player, 5.0s cast, single-target tankbuster
    OptimizedUltima1 = 21845, // Boss->self, 5.0s cast, range 40 circle, raidwide
    OptimizedUltima2 = 21846, // Boss->self, 5.0s cast, range 40 circle, raidwide
    DivideEtImperaP2 = 21872, // ?->player, no cast, range 4 circle, tankbuster
    OptimizedUltima3 = 23311, // Boss->self, 5.0s cast, range 40 circle, raidwide
}

public enum IconID : uint
{
    DivideEtImpera = 0xDA,
}

sealed class OptimizedUltima(BossModule module) : Components.RaidwideCasts(module, [(uint)AID.OptimizedUltima1, (uint)AID.OptimizedUltima2, (uint)AID.OptimizedUltima3]);
sealed class EmeraldShot(BossModule module) : Components.SingleTargetCast(module, (uint)AID.EmeraldShot);
// 後備猜法的甜甜圈內徑一律 3；內外徑取 Omen gl_sircle_1710／gl_sircle_2316。
sealed class PhotonLaserDonut1(BossModule module) : Components.SimpleAOEs(module, (uint)AID.PhotonLaserDonut1, new AOEShapeDonut(10f, 17f));
sealed class PhotonLaserDonut2(BossModule module) : Components.SimpleAOEs(module, (uint)AID.PhotonLaserDonut2, new AOEShapeDonut(16f, 23f));

// 頭上標記到結算（無讀條的 554E／5570）的秒數沒有證據，逾時清掉以免殘留。
sealed class DivideEtImpera(BossModule module) : Components.GenericBaitAway(module, centerAtTarget: true, tankbuster: true, damageType: AIHints.PredictedDamageType.Tankbuster)
{
    private const double Delay = 5.1d, Expiry = 5d;
    private static readonly AOEShapeCircle circle = new(4f);

    public override void OnEventIcon(Actor actor, uint iconID, ulong targetID)
    {
        if (iconID == (uint)IconID.DivideEtImpera)
            CurrentBaits.Add(new(Module.PrimaryActor, WorldState.Actors.Find(targetID) ?? actor, circle, WorldState.FutureTime(Delay)));
    }

    public override void OnEventCast(Actor caster, ActorCastEvent spell)
    {
        if (spell.Action.ID is not ((uint)AID.DivideEtImperaP1 or (uint)AID.DivideEtImperaP2))
            return;
        var id = spell.MainTargetID;
        for (var i = CurrentBaits.Count - 1; i >= 0; --i)
            if (CurrentBaits[i].Target.InstanceID == id)
                CurrentBaits.RemoveAt(i);
    }

    public override void Update()
    {
        var now = WorldState.CurrentTime;
        for (var i = CurrentBaits.Count - 1; i >= 0; --i)
        {
            var b = CurrentBaits[i];
            if (b.Target.IsDead || now > b.Activation.AddSeconds(Expiry))
                CurrentBaits.RemoveAt(i);
        }
    }
}

// 狀態機不排時間軸：每個元件都由事件驅動。
sealed class T06EmeraldWeaponStates : StateMachineBuilder
{
    public T06EmeraldWeaponStates(BossModule module) : base(module)
    {
        TrivialPhase()
            .ActivateOnEnter<OptimizedUltima>()
            .ActivateOnEnter<EmeraldShot>()
            .ActivateOnEnter<PhotonLaserDonut1>()
            .ActivateOnEnter<PhotonLaserDonut2>()
            .ActivateOnEnter<DivideEtImpera>()
            .ActivateOnEnter<Components.UnclaimedCastAOEs>();
    }
}

// 第一階段地板（y=0）是中心 (100,100) 半徑約 20.25 的圓，第二階段（y=-120）約 24.75；半徑 20 同時落在兩層地板內。
public abstract class T06EmeraldWeapon(WorldState ws, Actor primary) : UnverifiedOidTrialModule(ws, primary, new(100f, 100f), new ArenaBoundsCircle(20f))
{
    public const uint EmeraldWeaponNameID = 9696;
}

[ModuleInfo(BossModuleInfo.Maturity.WIP, StatesType = typeof(T06EmeraldWeaponStates), PrimaryActorOID = (uint)OID.BossCandidateA, Contributors = "ffxiv-tc-port", GroupType = BossModuleInfo.GroupType.CFC, GroupID = 762, NameID = EmeraldWeaponNameID)]
public sealed class T06EmeraldWeaponA(WorldState ws, Actor primary) : T06EmeraldWeapon(ws, primary);

[ModuleInfo(BossModuleInfo.Maturity.WIP, StatesType = typeof(T06EmeraldWeaponStates), PrimaryActorOID = (uint)OID.BossCandidateB, Contributors = "ffxiv-tc-port", GroupType = BossModuleInfo.GroupType.CFC, GroupID = 762, NameID = EmeraldWeaponNameID)]
public sealed class T06EmeraldWeaponB(WorldState ws, Actor primary) : T06EmeraldWeapon(ws, primary);

[ModuleInfo(BossModuleInfo.Maturity.WIP, StatesType = typeof(T06EmeraldWeaponStates), PrimaryActorOID = (uint)OID.BossCandidateC, Contributors = "ffxiv-tc-port", GroupType = BossModuleInfo.GroupType.CFC, GroupID = 762, NameID = EmeraldWeaponNameID)]
public sealed class T06EmeraldWeaponC(WorldState ws, Actor primary) : T06EmeraldWeapon(ws, primary);
