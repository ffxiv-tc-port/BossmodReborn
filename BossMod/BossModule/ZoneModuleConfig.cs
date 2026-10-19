namespace BossMod;

[ConfigDisplay(Name = "Full duty automation", Order = 6)]
public sealed class ZoneModuleConfig : ConfigNode
{
    [PropertyDisplay("Required maturity for zone modules to be loaded")]
    public BossModuleInfo.Maturity MinMaturity = BossModuleInfo.Maturity.Contributed;

    [PropertyDisplay("Enable automatic execution of quest battles / solo duties")]
    public bool EnableQuestBattles = false;

    // 讀取端一律用這個：其他插件的執行期覆寫優先；存檔、設定頁、/bmr cfg 看的仍是上面的使用者值。
    public bool EnableQuestBattlesEffective => TransientConfigOverrides.EnableQuestBattles ?? EnableQuestBattles;

    [PropertyDisplay("Draw waypoints in game world")]
    public bool ShowWaypoints = false;

    [PropertyDisplay("Use dash abilities for navigation (Smudge, Elusive Jump, etc)")]
    public bool UseDash = true;
}
