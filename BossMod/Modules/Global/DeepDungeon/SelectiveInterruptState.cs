namespace BossMod.Global.DeepDungeon;

// IPC BossMod.DeepDungeon.SelectiveInterruptActive（Func<bool>）的快照。名稱與型別是 WrathCombo 消費端的契約，不可改。
// 契約：true＝深宮模組這一幀在一般層標記打斷，而且「一般層只標記必要的打斷」開著；AutoClear 在框架執行緒每幀發佈。
// 模組關、BOSS 層、換層中、首領模組接手、模組卸載、超過 StaleMs 沒發佈 ⇒ false。端點任何執行緒可呼叫：只讀一個 long、不阻塞、不擲例外。
public static class DeepDungeonSelectiveInterruptState
{
    private const long StaleMs = 2000;

    // 0＝沒有生效；非 0＝最後一次發佈「生效」的時間（Environment.TickCount64）
    private static long _activeAtMs;

    public static bool IsActive()
    {
        var at = System.Threading.Interlocked.Read(ref _activeAtMs);
        return at != 0 && Environment.TickCount64 - at <= StaleMs;
    }

    internal static void Publish(bool selectiveOn) => System.Threading.Interlocked.Exchange(ref _activeAtMs, selectiveOn ? Environment.TickCount64 : 0);

    internal static void Clear() => System.Threading.Interlocked.Exchange(ref _activeAtMs, 0);
}
