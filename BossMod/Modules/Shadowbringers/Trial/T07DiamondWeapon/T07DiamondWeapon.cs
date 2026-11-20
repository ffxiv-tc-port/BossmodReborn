namespace BossMod.Shadowbringers.Trial.T07DiamondWeapon;

// 首領 OID 沒有錄影：鑽石武器的模型（ModelChara 3056）有兩列，分不出哪列是這場，兩個都註冊。
public enum OID : uint
{
    BossCandidateA = 0x32DC,
    BossCandidateB = 0x32DF,
}

// 只列有元件處理的技能；其餘讀條交給 UnclaimedCastAOEs 照沒有模組時的猜法。
public enum AID : uint
{
    DiamondFlashMarker = 22393, // Boss->player, no cast, marks the line stack target
    DiamondRain = 24487, // Boss->self, 5.0s cast, raidwide
    DiamondFlash = 24532, // Boss->player, no cast, range 40 width 6 rect, line stack
    Outrage = 24535, // Boss->self, 5.0s cast, range 45 circle, raidwide
    AuriDoomstead = 24536, // Boss->player, 5.0s cast, range 5 circle, tankbuster
    HomingLaser = 24539, // ?->player, 5.0s cast, range 5 circle, spread
}

sealed class DiamondRain(BossModule module) : Components.RaidwideCast(module, (uint)AID.DiamondRain);
sealed class Outrage(BossModule module) : Components.RaidwideCast(module, (uint)AID.Outrage);
sealed class AuriDoomstead(BossModule module) : Components.BaitAwayCast(module, (uint)AID.AuriDoomstead, 5f, tankbuster: true);
sealed class HomingLaser(BossModule module) : Components.SpreadFromCastTargets(module, (uint)AID.HomingLaser, 5f);
// 全隊直線分攤：5779 點名、約 8.4 秒後 5FD4 結算。
sealed class DiamondFlash(BossModule module) : Components.LineStack(module, aidMarker: (uint)AID.DiamondFlashMarker, (uint)AID.DiamondFlash, 8.4d, 40f, 3f, PartyState.MaxPartySize, PartyState.MaxPartySize);

// 狀態機不排時間軸：每個元件都由事件驅動。
sealed class T07DiamondWeaponStates : StateMachineBuilder
{
    public T07DiamondWeaponStates(BossModule module) : base(module)
    {
        TrivialPhase()
            .ActivateOnEnter<DiamondRain>()
            .ActivateOnEnter<Outrage>()
            .ActivateOnEnter<AuriDoomstead>()
            .ActivateOnEnter<HomingLaser>()
            .ActivateOnEnter<DiamondFlash>()
            .ActivateOnEnter<Components.UnclaimedCastAOEs>();
    }
}

// 東西兩片 20×40 的平台（碰撞 x 69.75～90／109.75～130、z 79.75～120），中間是空的；兩片之間只能靠場地機關移動，不畫通道。
public abstract class T07DiamondWeapon(WorldState ws, Actor primary) : UnverifiedOidTrialModule(ws, primary, arena.Center, arena)
{
    public const uint DiamondWeaponNameID = 9953;
    private static readonly ArenaBoundsComplex arena = new([new Rectangle(new(80f, 100f), 10f, 20f), new Rectangle(new(120f, 100f), 10f, 20f)]);
}

[ModuleInfo(BossModuleInfo.Maturity.WIP, StatesType = typeof(T07DiamondWeaponStates), PrimaryActorOID = (uint)OID.BossCandidateA, Contributors = "ffxiv-tc-port", GroupType = BossModuleInfo.GroupType.CFC, GroupID = 781, NameID = DiamondWeaponNameID)]
public sealed class T07DiamondWeaponA(WorldState ws, Actor primary) : T07DiamondWeapon(ws, primary);

[ModuleInfo(BossModuleInfo.Maturity.WIP, StatesType = typeof(T07DiamondWeaponStates), PrimaryActorOID = (uint)OID.BossCandidateB, Contributors = "ffxiv-tc-port", GroupType = BossModuleInfo.GroupType.CFC, GroupID = 781, NameID = DiamondWeaponNameID)]
public sealed class T07DiamondWeaponB(WorldState ws, Actor primary) : T07DiamondWeapon(ws, primary);
