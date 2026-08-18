using FFXIVClientStructs.FFXIV.Client.System.Framework;

namespace BossMod.Network;

// some of the packets have a simplistic 'scramble' transformation applied to some of the fields:
// - action-id of ActorCast packet
// - action-id of ActionEffectN packets
// - param1 (icon-id) of ActorControl packet with TargetIcon category
// as of patch 7.2, the scramble delta is opcode-specific (kind of); there's an array of three ints stored on NetworkModulePacketReceiverCallback, so keys[opcode mod 3] is used as the starting value, then the game session and zone random values are subtracted from it
public static unsafe class IDScramble
{
    public const uint Delta = 0;

    // 🔴 Framework.Instance() 是 [StaticAddress(…, isPointer: true)]，回傳的是全域指標槽的**內容**，
    //    登入前／登出過程中合法為 null；NetworkModuleProxy 與 ReceiverCallback 也都是普通的指標欄位，
    //    網路模組還沒接起來時同樣是 null。原本這條三層裸鏈解參考就是 AccessViolationException，
    //    而 AVE 在 .NET Core 是 corrupted-state exception，try/catch 攔不到。
    // 📌 回傳型別刻意改成可為 null：`null`＝「這一刻讀不到」，與「讀到了，而且五個欄位都是 0」
    //    （＝合法的 `default`）是兩件不同的事。呼叫端要能分辨，否則讀不到時會把上一次讀到的
    //    正確 scramble 覆寫成全 0，接下來每個封包都會被解錯——那比不更新糟得多。
    public static NetworkState.IDScrambleFields? Get()
    {
        var fwk = Framework.Instance();
        var networkModuleProxy = fwk != null ? fwk->NetworkModuleProxy : null;
        var proxy = networkModuleProxy != null ? networkModuleProxy->ReceiverCallback : null;
        if (proxy == null)
            return null;

        return new NetworkState.IDScrambleFields(proxy->GameSessionRandom, proxy->LastPacketRandom, proxy->Key0, proxy->Key1, proxy->Key2);
    }
}
