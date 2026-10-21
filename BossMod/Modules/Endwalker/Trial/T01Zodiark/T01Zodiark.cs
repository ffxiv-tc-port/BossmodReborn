namespace BossMod.Endwalker.Trial.T01Zodiark;

public enum OID : uint
{
    Boss = 0x324C,
}

// 只有場地與雷達：沒有 AID 列舉＝模組不認領任何技能，敵方讀條全部由 UnclaimedCastAOEs 照沒有模組時的猜法處理。
class T01ZodiarkStates : StateMachineBuilder
{
    public T01ZodiarkStates(BossModule module) : base(module)
    {
        TrivialPhase()
            .ActivateOnEnter<Components.UnclaimedCastAOEs>();
    }
}

[ModuleInfo(BossModuleInfo.Maturity.Contributed, Contributors = "ffxiv-tc-port", GroupType = BossModuleInfo.GroupType.CFC, GroupID = 802, NameID = 10456)]
public class T01Zodiark(WorldState ws, Actor primary) : UnverifiedOidTrialModule(ws, primary, new(100f, 100f), new ArenaBoundsSquare(20f));
