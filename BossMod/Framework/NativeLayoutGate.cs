using FFXIVClientStructs.FFXIV.Client.Game.Object;

namespace BossMod;

// 本檔假設與契約：
// - Resolve 只在插件建構子的同步路徑、任何 hook 掛上之前呼叫一次；對不上就擲例外＝插件載入失敗（使用者裁定：
//   改版當天寧可載入失敗，也不帶著未驗證的偏移在 hook／每幀路徑寫遊戲記憶體）。刻意不降級成略過寫入。
// - 程式碼位元組一律從 SigScanner 的 .text 讀（IsCopy 時是 Dalamud 從磁碟執行檔建的副本，不受別人的 hook 影響），
//   範圍限制在 .text 之內；不讀任何遊戲物件記憶體。推導細節見 commit 訊息。
internal static unsafe partial class NativeLayoutGate
{
    /// <summary>Character 內「轉向內插目標角度」（float）的位移；0＝尚未推導，呼叫端不可寫入。</summary>
    public static int CharacterDesiredRotationOffset { get; private set; }

    /// <summary>走路輸入函式 self 內「旋轉移動」旗標（byte）的位移；0＝沒推導（走路 hook 也沒掛），不可讀。</summary>
    public static int MoveControllerSpinningOffset { get; private set; }

    public static void Resolve()
    {
        var read = TextView();
        CharacterDesiredRotationOffset = DeriveDesiredRotationOffset(Service.SigScanner.ScanAllText, read, FunctionLength, GameObject.Addresses.SetRotation.Value, out var rotation);
        Service.Logger.Information($"[NativeLayout] 轉向目標角度位置：{rotation}");

        // 特徵碼對不上＝MovementOverride 不掛走路 hook、不會讀旗標，不必推。
        if (Service.SigScanner.TryScanText(MovementOverride.RMIWalkSignature, out var walkInput))
        {
            MoveControllerSpinningOffset = DeriveSpinningOffset(Service.SigScanner.ScanAllText, read, walkInput, out var spinning);
            Service.Logger.Information($"[NativeLayout] 旋轉移動旗標位置：{spinning}");
        }
        else
            Service.Logger.Information("[NativeLayout] 走路輸入函式特徵碼對不上：不掛走路覆寫，略過旋轉移動旗標推導");

        // 特徵碼對不上＝MovementOverride 不掛飛行 hook、不會寫 result，不必驗。
        if (!Service.SigScanner.TryScanText(MovementOverride.RMIFlySignature, out var flyInput))
        {
            Service.Logger.Information("[NativeLayout] 飛行輸入函式特徵碼對不上：不掛飛行覆寫，略過版面驗證");
            return;
        }
        var flyLength = FunctionLength(flyInput);
        var fly = "查不到函式範圍（.pdata）";
        if (flyLength <= 0 || !FlyInputWritesAxes(read(flyInput, flyLength), out fly))
            throw Fail($"飛行輸入函式 0x{flyInput:X} 不再把三個移動軸寫到 result+0／+4／+8（{fly}）");
        Service.Logger.Information($"[NativeLayout] 飛行輸入版面驗證通過：{fly}");
    }

    private static CodeReader TextView()
    {
        var scanner = Service.SigScanner;
        var copyDelta = scanner.SearchBase - scanner.Module.BaseAddress;
        var lo = scanner.TextSectionBase;
        var hi = lo + scanner.TextSectionSize;
        return (address, length) =>
        {
            var p = address + copyDelta;
            if (length <= 0 || p < lo || p >= hi)
                return ReadOnlySpan<byte>.Empty;
            return new ReadOnlySpan<byte>((void*)p, (int)Math.Min(length, hi - p));
        };
    }
}
