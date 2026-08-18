namespace BossMod.Dawntrail.Savage.M08SHowlingBlade;

[ConfigDisplay(Order = 0x130, Parent = typeof(DawntrailConfig))]
public sealed class M08SHowlingBladeConfig() : ConfigNode()
{
    [PropertyDisplay("Show platform numbers")]
    public bool ShowPlatformNumbers = true;

    [PropertyDisplay("Platform number colors:")]
    public Color[] PlatformNumberColors = [new(0xffffffff), new(0xffffffff), new(0xffffffff), new(0xffffffff), new(0xffffffff)];

    [PropertyDisplay("Platform number font size")]
    [PropertySlider(0.1f, 100, Speed = 1)]
    public float PlatformNumberFontSize = 22;

    public enum ReignStrategy
    {
        [PropertyDisplay("Show both safespots for current role")]
        Any,
        [PropertyDisplay("Assume G1 left, G2 right when looking at boss from arena center")]
        Standard,
        [PropertyDisplay("Assume G1 right, G2 left when looking at boss from arena center")]
        Inverse,
        // 🔴 這裡原本是 "None"，與 ActionTweaksConfig.ModifierKey.None 共用同一個扁平翻譯鍵
        //    （PropertyDisplay 標籤走 ConfigUI 的 Loc.T(label, label)，英文原句就是 key）。
        //    那邊的 "None" 是「不指定輔助按鍵」＝「無」，這裡是「不顯示站位提示」，
        //    一個 key 餵兩種語意 ⇒ 本選項在繁中會顯示成「無」而不是「不顯示任何提示」。
        //    改用 DSW1Config／TEAConfig 對同型選項既有的措辭，撞名與誤譯一起消失。
        [PropertyDisplay("Don't show any hints")]
        Disabled
    }

    [PropertyDisplay("Revolutionary/Eminent Reign positioning hints")]
    public ReignStrategy ReignHints = ReignStrategy.Standard;
}
