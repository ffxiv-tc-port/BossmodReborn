namespace BossMod.Endwalker.Raid.P6NHegemone;

sealed class HemitheosDark(BossModule module) : Components.RaidwideCast(module, (uint)AID.HemitheosDark);
sealed class ChorosIxou(BossModule module) : Components.SimpleAOEGroups(module, [(uint)AID.ChorosIxouAOE1, (uint)AID.ChorosIxouAOE2], new AOEShapeCone(40f, 45f.Degrees()));
sealed class SynergyAOE1(BossModule module) : Components.BaitAwayCast(module, (uint)AID.SynergyAOE1, 5f, tankbuster: true);
sealed class SynergyAOE2(BossModule module) : Components.BaitAwayCast(module, (uint)AID.SynergyAOE2, 5f, tankbuster: true);
sealed class DarkAshes(BossModule module) : Components.SpreadFromCastTargets(module, (uint)AID.DarkAshesAOE, 6f);
sealed class ReekHavoc(BossModule module) : Components.SimpleAOEs(module, (uint)AID.ReekHavoc, new AOEShapeCone(60f, 15f.Degrees()));

// 只畫有讀條的第一下；之後無讀條的旋轉（31254／31255）每步角度沒有證據，刻意不預測。
sealed class StropheIxou(BossModule module) : Components.SimpleAOEGroups(module, [(uint)AID.StropheIxouAOE1, (uint)AID.StropheIxouAOE2], new AOEShapeCone(40f, 22.5f.Degrees()));

sealed class P6NHegemoneStates : StateMachineBuilder
{
    public P6NHegemoneStates(BossModule module) : base(module)
    {
        TrivialPhase()
            .ActivateOnEnter<HemitheosDark>()
            .ActivateOnEnter<ChorosIxou>()
            .ActivateOnEnter<SynergyAOE1>()
            .ActivateOnEnter<SynergyAOE2>()
            .ActivateOnEnter<DarkAshes>()
            .ActivateOnEnter<ReekHavoc>()
            .ActivateOnEnter<StropheIxou>()
            .ActivateOnEnter<Components.UnclaimedCastAOEs>();
    }
}

// 乙太方陣的格子（地圖效果索引）在一般難度沒有證據，交給 UnclaimedCastAOEs 畫 1 秒讀條的方格。
[ModuleInfo(BossModuleInfo.Maturity.WIP, Contributors = "ffxiv-tc-port", GroupType = BossModuleInfo.GroupType.CFC, GroupID = 880, NameID = 11381)]
public sealed class P6NHegemone(WorldState ws, Actor primary) : BossModule(ws, primary, new(100f, 100f), new ArenaBoundsSquare(20f));
