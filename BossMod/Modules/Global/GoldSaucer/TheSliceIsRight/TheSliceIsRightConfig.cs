namespace BossMod.Global.GoldSaucer.SliceIsRight;

[ConfigDisplay(Name = "Gold Saucer: The Slice Is Right", Order = 6, Parent = typeof(ModuleConfig))]
public sealed class TheSliceIsRightConfig() : ConfigNode()
{
    // 舊設定檔沒有這個鍵時 ConfigNode.Deserialize 不碰欄位 ⇒ 拿到這裡的初始值。
    [PropertyDisplay("Wait before dodging (seconds)", tooltip: "After one of Yojimbo's attack telegraphs appears, the AI waits this many seconds before treating it as a danger zone and dodging. The radar still shows the telegraph immediately.\nIf a telegraph is too short to wait the full time and still get out, the wait is shortened automatically so the AI always leaves in time.\nOnly affects the G.A.T.E. \"The Slice Is Right\".")]
    [PropertySlider(0f, SliceDodgeTiming.MaxDelaySeconds, Speed = 0.1f)]
    public float DodgeDelaySeconds = 2f;
}
