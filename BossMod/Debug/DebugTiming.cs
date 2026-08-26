using Dalamud.Bindings.ImGui;

namespace BossMod;

public sealed class DebugTiming
{
    uint _prevFrameCounter;
    long _prevQPC;

    public unsafe void Draw()
    {
        // 🔴 Framework.Instance() 是 [StaticAddress(…, isPointer: true)]，回傳全域指標槽的**內容**，合法可為 null。
        //    本方法接下來每一行都要解參考 fwk，原本一律裸解＝AccessViolationException（攔不到）。
        //    ⚠️ 這個位置**不會**被「Framework.Instance()->」的 grep 掃到（先接到區域變數再解參考），
        //    所以前一批的枚舉漏了它——同形狀的還有 WorldStateGameSync.Update()。
        //    除錯視窗的中性行為＝顯示不可用；_prevQPC 不更新，下次拿得到時算出來的 dt 會涵蓋這段空窗，
        //    這比寫入一個假的基準值好。
        var fwk = FFXIVClientStructs.FFXIV.Client.System.Framework.Framework.Instance();
        if (fwk == null)
        {
            ImGui.TextUnformatted("Framework 不可用 —— 這一刻讀不到任何計時資訊");
            return;
        }

        var dtReal = (double)(fwk->PerformanceCounterValue - _prevQPC) / fwk->PerformanceCounterFrequency;
        ImGui.TextUnformatted($"Frame counter: {fwk->FrameCounter}");
        ImGui.TextUnformatted($"Frame time effective: {fwk->FrameDeltaTime}");
        ImGui.TextUnformatted($"Framerate: {fwk->FrameRate}");
        ImGui.TextUnformatted($"Forced frame duration: {fwk->FrameDeltaTimeOverride}");
        ImGui.TextUnformatted($"Forced next frame duration: {fwk->NextFrameDeltaTimeOverride}");
        ImGui.TextUnformatted($"Frame duration multiplier: {fwk->FrameDeltaFactor}");
        ImGui.TextUnformatted($"Tick speed multiplier: {fwk->GameSpeedMultiplier}");
        ImGui.TextUnformatted($"QPC freq: {fwk->PerformanceCounterFrequency}");
        ImGui.TextUnformatted($"QPC value: {fwk->PerformanceCounterValue}");
        ImGui.TextUnformatted($"dt raw: {fwk->RealFrameDeltaTime}");
        ImGui.TextUnformatted($"dt real: {dtReal} = raw + {dtReal - fwk->RealFrameDeltaTime}");
        ImGui.TextUnformatted($"dt ms granularity: {fwk->FrameDeltaTimeMSInt} + {fwk->FrameDeltaTimeMSRem}");
        ImGui.TextUnformatted($"dt us granularity: {fwk->FrameDeltaTimeUSInt} + {fwk->FrameDeltaTimeUSRem}");
        ImGui.TextUnformatted($"dt timer: {DateTime.UnixEpoch.AddSeconds(fwk->UtcTime.Timestamp)}");
        _prevFrameCounter = fwk->FrameCounter;
        _prevQPC = fwk->PerformanceCounterValue;
    }
}
