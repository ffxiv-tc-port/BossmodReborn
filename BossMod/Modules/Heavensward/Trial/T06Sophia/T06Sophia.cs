namespace BossMod.Heavensward.Trial.T06Sophia;

// 首領 OID 沒有錄影：台服 BNpcBase 用索菲亞模型（ModelChara 1536）的列只有三個，極版 0x1791（錄影）、幻版 0x3E14（幻版模組）之外只剩這一個。
public enum OID : uint
{
    Boss = 0x178A,
    Barbelo = 0x178B, // 芭碧蘿（索菲亞的頭）；同模型 1537 的另外兩列是極版 0x1792 與幻版 0x3E16
}

// 只列有元件處理的技能；其餘讀條（魔槍突刺、痛苦環刺、類星體…）交給 UnclaimedCastAOEs 照沒有模組時的猜法。
public enum AID : uint
{
    ThunderIII = 6514, // Boss->self, 3.0s cast, range 5-15+R donut
    AeroIII = 6515, // Boss->self, 3.0s cast, range 5+R circle
    ThunderII = 6516, // Boss->self, 3.0s cast, range 15+R 90-degree cone
    DivineSpark = 6521, // Demiurge->self, 4.0s cast, range 50 circle gaze
    CloudyHeavens = 6528, // Boss->self, 3.0s cast, range 50 circle
    LightDew = 6530, // Barbelo->self, 1.0s cast, range 50+R width 18 rect
    Onrush = 6533, // Boss->self, 1.0s cast, range 50+R width 16 rect
}

// 大雷電的 Omen 是專用貼圖，後備猜法會把它當成實心圓、把 AI 往外推；安全區其實是王腳下。形狀照同模型的幻版模組。
class ThunderIII(BossModule module) : Components.SimpleAOEs(module, (uint)AID.ThunderIII, new AOEShapeDonut(5f, 20f));
class AeroIII(BossModule module) : Components.SimpleAOEs(module, (uint)AID.AeroIII, 10f);
class ThunderII(BossModule module) : Components.SimpleAOEs(module, (uint)AID.ThunderII, new AOEShapeCone(20f, 45f.Degrees()));
class DivineSpark(BossModule module) : Components.CastGaze(module, (uint)AID.DivineSpark);
class CloudyHeavens(BossModule module) : Components.RaidwideCast(module, (uint)AID.CloudyHeavens);
class LightDew(BossModule module) : Components.SimpleAOEs(module, (uint)AID.LightDew, new AOEShapeRect(55f, 9f));
class Onrush(BossModule module) : Components.SimpleAOEs(module, (uint)AID.Onrush, new AOEShapeRect(55f, 8f));

// 狀態機不排時間軸：每個元件都由事件驅動。
class T06SophiaStates : StateMachineBuilder
{
    public T06SophiaStates(BossModule module) : base(module)
    {
        TrivialPhase()
            .ActivateOnEnter<ThunderIII>()
            .ActivateOnEnter<AeroIII>()
            .ActivateOnEnter<ThunderII>()
            .ActivateOnEnter<DivineSpark>()
            .ActivateOnEnter<CloudyHeavens>()
            .ActivateOnEnter<LightDew>()
            .ActivateOnEnter<Onrush>()
            .ActivateOnEnter<Components.UnclaimedCastAOEs>();
    }
}

// 第二階段場地會傾斜、會掉下去：場地取碰撞地板內縮 0.25 碼，不讓 AI 站到邊緣。
[ModuleInfo(BossModuleInfo.Maturity.WIP, Contributors = "ffxiv-tc-port", GroupType = BossModuleInfo.GroupType.CFC, GroupID = 183, NameID = 5199)]
public sealed class T06Sophia(WorldState ws, Actor primary) : UnverifiedOidTrialModule(ws, primary, default, new ArenaBoundsRect(19.5f, 14.5f));
