namespace BossMod.Stormblood.Trial.T06Rathalos;

public enum OID : uint
{
    Boss = 0x2129,
}

// 只有場地與雷達：沒有 AID 列舉＝模組不認領任何技能，敵方讀條全部由 UnclaimedCastAOEs 照沒有模組時的猜法處理。
class T06RathalosStates : StateMachineBuilder
{
    public T06RathalosStates(BossModule module) : base(module)
    {
        TrivialPhase()
            .ActivateOnEnter<Components.UnclaimedCastAOEs>();
    }
}

[ModuleInfo(BossModuleInfo.Maturity.Contributed, Contributors = "ffxiv-tc-port", GroupType = BossModuleInfo.GroupType.CFC, GroupID = 474, NameID = 7221)]
public class T06Rathalos(WorldState ws, Actor primary) : UnverifiedOidTrialModule(ws, primary, new(100f, 100f), new ArenaBoundsCircle(24.5f));
