namespace BossMod.Endwalker.Trial.T05Rubicante;

public enum OID : uint
{
    Boss = 0x3AEE,
}

// 只有場地與雷達：沒有 AID 列舉＝模組不認領任何技能，敵方讀條全部由 UnclaimedCastAOEs 照沒有模組時的猜法處理。
class T05RubicanteStates : StateMachineBuilder
{
    public T05RubicanteStates(BossModule module) : base(module)
    {
        TrivialPhase()
            .ActivateOnEnter<Components.UnclaimedCastAOEs>();
    }
}

[ModuleInfo(BossModuleInfo.Maturity.Contributed, Contributors = "ffxiv-tc-port", GroupType = BossModuleInfo.GroupType.CFC, GroupID = 886, NameID = 12054)]
public class T05Rubicante(WorldState ws, Actor primary) : BossModule(ws, primary, arena.Center, arena)
{
    private static readonly ArenaBoundsComplex arena = new([new Polygon(new(100f, 100f), 20f, 64)]);

    // 首領 OID 是從極版同模型推的、沒有錄影佐證：只在這個副本裡啟用，推錯時不會在別處誤啟用
    protected override bool CheckPull() => base.CheckPull() && WorldState.CurrentCFCID == 886;
}
