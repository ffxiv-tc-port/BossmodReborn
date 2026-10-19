namespace BossMod.AI;

// 移動擁有權換手與「算不出目的地」：逐筆寫 Debug，每分鐘彙總一行 Information（每小時最多 60 行）。
// 契約：AIBehaviour 從執行緒集區呼叫、NormalMovement 從主執行緒呼叫；鎖在 MinuteLogDigest 裡。
// 閒置常態（沒有人要求移動）照舊走 Verbose，不進彙總。
static class MovementLog
{
    public const int YieldToPreset = 0, BackToAI = 1, PresetGaveUp = 2, AINoDestination = 3;

    public static readonly MinuteLogDigest Digest = new("[移動擁有權][每分鐘彙總]",
        ["交給預設集的自動移動", "回到 AI 自動走位", "自動移動算不出目的地而交還", "AI 導航算不出目的地"],
        [0, 0, -1, -1], ["換手最短間隔"]);
}
