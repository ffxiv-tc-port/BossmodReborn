namespace BossMod.Endwalker.Trial.T04Barbariccia;

public enum OID : uint
{
    Boss = 0x39D1,
}

// 只有場地與雷達：沒有 AID 列舉＝模組不認領任何技能，敵方讀條全部由 UnclaimedCastAOEs 照沒有模組時的猜法處理。
class T04BarbaricciaStates : StateMachineBuilder
{
    public T04BarbaricciaStates(BossModule module) : base(module)
    {
        TrivialPhase()
            .ActivateOnEnter<Components.UnclaimedCastAOEs>();
    }
}

[ModuleInfo(BossModuleInfo.Maturity.Contributed, Contributors = "ffxiv-tc-port", GroupType = BossModuleInfo.GroupType.CFC, GroupID = 870, NameID = 11398)]
public class T04Barbariccia(WorldState ws, Actor primary) : UnverifiedOidTrialModule(ws, primary, new(100f, 100f), new ArenaBoundsCircle(20f));
