namespace BossMod.Endwalker.Trial.T06Golbez;

public enum OID : uint
{
    Boss = 0x3F54,
}

// 只有場地與雷達：沒有 AID 列舉＝模組不認領任何技能，敵方讀條全部由 UnclaimedCastAOEs 照沒有模組時的猜法處理。
class T06GolbezStates : StateMachineBuilder
{
    public T06GolbezStates(BossModule module) : base(module)
    {
        TrivialPhase()
            .ActivateOnEnter<Components.UnclaimedCastAOEs>();
    }
}

[ModuleInfo(BossModuleInfo.Maturity.Contributed, Contributors = "ffxiv-tc-port", GroupType = BossModuleInfo.GroupType.CFC, GroupID = 949, NameID = 12365)]
public class T06Golbez(WorldState ws, Actor primary) : UnverifiedOidTrialModule(ws, primary, new(100f, 100f), new ArenaBoundsSquare(15f));
