namespace BossMod.Shadowbringers.Raid.E04NTitan;

sealed class VoiceOfTheLand(BossModule module) : Components.RaidwideCast(module, (uint)AID.VoiceOfTheLand);
sealed class EarthenFury(BossModule module) : Components.RaidwideCast(module, (uint)AID.EarthenFury);
sealed class Stonecrusher(BossModule module) : Components.BaitAwayCast(module, (uint)AID.Stonecrusher, 5f, tankbuster: true, damageType: AIHints.PredictedDamageType.Tankbuster);

// 斷裂帶刻意不處理：寬 52 的擊退範圍比場地（寬 40）還寬，交給後備猜法會畫成整個場地都是禁區；致命的斷層線沒有讀條。
sealed class E04NTitanStates : StateMachineBuilder
{
    public E04NTitanStates(BossModule module) : base(module)
    {
        TrivialPhase()
            .ActivateOnEnter<VoiceOfTheLand>()
            .ActivateOnEnter<EarthenFury>()
            .ActivateOnEnter<Stonecrusher>()
            .ActivateOnEnter<Components.UnclaimedCastAOEs>();
    }
}

// 場地＝16 塊 10×10 地板（大地重壓／邪土的方格對齊 90、100、110），內縮 0.5 碼；場外會掉下去。
[ModuleInfo(BossModuleInfo.Maturity.WIP, Contributors = "ffxiv-tc-port", Expansion = BossModuleInfo.Expansion.Shadowbringers, Category = BossModuleInfo.Category.Raid, GroupType = BossModuleInfo.GroupType.CFC, GroupID = 689u, NameID = 8350u, SortOrder = 1)]
public sealed class E04NTitan(WorldState ws, Actor primary) : BossModule(ws, primary, new(100f, 100f), new ArenaBoundsSquare(19.5f));
