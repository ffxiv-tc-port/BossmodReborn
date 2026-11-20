namespace BossMod.Shadowbringers.Trial.T05RubyWeapon;

// 首領 OID 沒有錄影：紅寶石武器兩種模型（ModelChara 2814 第一階段、2879 奈爾型態）各有兩列，分不出哪列是這場，四個都註冊。
public enum OID : uint
{
    BossCandidateA = 0x2CDC,
    BossCandidateB = 0x2CE2,
    NaelCandidateA = 0x2CDF,
    NaelCandidateB = 0x2CE6,
}

// 只列有元件處理的技能；其餘讀條交給 UnclaimedCastAOEs 照沒有模組時的猜法。
public enum AID : uint
{
    OptimizedUltima = 19112, // Boss->self, 4.0s cast, range 40 circle, raidwide
    RubyClaw = 19135, // RavensImage->player, 4.0s cast, single-target tankbuster
    HomingLasers = 19139, // ?->player, 5.0s cast, range 6 circle, spread
    HighPoweredHomingLasers = 19141, // ?->player, 5.5s cast, range 6 circle, stack (two markers)
    Stamp = 19143, // Boss->player, 5.0s cast, single-target tankbuster
    Outrage = 19144, // Boss->self, 7.0s cast, range 40 circle, raidwide
    MeteorStreamA = 20071, // ?->player, no cast, range 4 circle, spread
    MeteorStreamB = 20072, // ?->player, no cast, range 4 circle, spread
}

public enum IconID : uint
{
    MeteorStream = 0xE0,
}

sealed class OptimizedUltima(BossModule module) : Components.RaidwideCast(module, (uint)AID.OptimizedUltima);
sealed class Outrage(BossModule module) : Components.RaidwideCast(module, (uint)AID.Outrage);
sealed class Stamp(BossModule module) : Components.SingleTargetCast(module, (uint)AID.Stamp);
sealed class RubyClaw(BossModule module) : Components.SingleTargetCast(module, (uint)AID.RubyClaw);
sealed class HomingLasers(BossModule module) : Components.SpreadFromCastTargets(module, (uint)AID.HomingLasers, 6f);
// 兩個分攤點名、全隊分兩組。
sealed class HighPoweredHomingLasers(BossModule module) : Components.StackWithCastTargets(module, (uint)AID.HighPoweredHomingLasers, 6f, 2, 4);

// 結算是無讀條的 4E67／4E68，分不出哪個對哪個點名，兩個都收；點名到結算的秒數沒有證據，逾時清掉以免殘留。
sealed class MeteorStream(BossModule module) : Components.UniformStackSpread(module, default, 4f, alwaysShowSpreads: true)
{
    private const double Delay = 5d, Expiry = 5d;

    public override void OnEventIcon(Actor actor, uint iconID, ulong targetID)
    {
        if (iconID == (uint)IconID.MeteorStream)
            AddSpread(actor, WorldState.FutureTime(Delay));
    }

    public override void OnEventCast(Actor caster, ActorCastEvent spell)
    {
        if (spell.Action.ID is not ((uint)AID.MeteorStreamA or (uint)AID.MeteorStreamB))
            return;
        var id = spell.MainTargetID;
        for (var i = Spreads.Count - 1; i >= 0; --i)
            if (Spreads[i].Target.InstanceID == id)
                Spreads.RemoveAt(i);
    }

    public override void Update()
    {
        var now = WorldState.CurrentTime;
        for (var i = Spreads.Count - 1; i >= 0; --i)
        {
            var s = Spreads[i];
            if (s.Target.IsDead || now > s.Activation.AddSeconds(Expiry))
                Spreads.RemoveAt(i);
        }
    }
}

// 狀態機不排時間軸：每個元件都由事件驅動。
sealed class T05RubyWeaponStates : StateMachineBuilder
{
    public T05RubyWeaponStates(BossModule module) : base(module)
    {
        TrivialPhase()
            .ActivateOnEnter<OptimizedUltima>()
            .ActivateOnEnter<Outrage>()
            .ActivateOnEnter<Stamp>()
            .ActivateOnEnter<RubyClaw>()
            .ActivateOnEnter<HomingLasers>()
            .ActivateOnEnter<HighPoweredHomingLasers>()
            .ActivateOnEnter<MeteorStream>()
            .ActivateOnEnter<Components.UnclaimedCastAOEs>();
    }
}

// 兩階段的地板（y=0 與 y=-100）都是中心 (100,100) 的圓；半徑 20 邊緣有格子沒有碰撞地板，取 19.5。
public abstract class T05RubyWeapon(WorldState ws, Actor primary) : UnverifiedOidTrialModule(ws, primary, new(100f, 100f), new ArenaBoundsCircle(19.5f))
{
    public const uint RubyWeaponNameID = 9250;
}

[ModuleInfo(BossModuleInfo.Maturity.WIP, StatesType = typeof(T05RubyWeaponStates), PrimaryActorOID = (uint)OID.BossCandidateA, Contributors = "ffxiv-tc-port", GroupType = BossModuleInfo.GroupType.CFC, GroupID = 717, NameID = RubyWeaponNameID)]
public sealed class T05RubyWeaponA(WorldState ws, Actor primary) : T05RubyWeapon(ws, primary);

[ModuleInfo(BossModuleInfo.Maturity.WIP, StatesType = typeof(T05RubyWeaponStates), PrimaryActorOID = (uint)OID.BossCandidateB, Contributors = "ffxiv-tc-port", GroupType = BossModuleInfo.GroupType.CFC, GroupID = 717, NameID = RubyWeaponNameID)]
public sealed class T05RubyWeaponB(WorldState ws, Actor primary) : T05RubyWeapon(ws, primary);

[ModuleInfo(BossModuleInfo.Maturity.WIP, StatesType = typeof(T05RubyWeaponStates), PrimaryActorOID = (uint)OID.NaelCandidateA, Contributors = "ffxiv-tc-port", GroupType = BossModuleInfo.GroupType.CFC, GroupID = 717, NameID = RubyWeaponNameID)]
public sealed class T05RubyWeaponNaelA(WorldState ws, Actor primary) : T05RubyWeapon(ws, primary);

[ModuleInfo(BossModuleInfo.Maturity.WIP, StatesType = typeof(T05RubyWeaponStates), PrimaryActorOID = (uint)OID.NaelCandidateB, Contributors = "ffxiv-tc-port", GroupType = BossModuleInfo.GroupType.CFC, GroupID = 717, NameID = RubyWeaponNameID)]
public sealed class T05RubyWeaponNaelB(WorldState ws, Actor primary) : T05RubyWeapon(ws, primary);
