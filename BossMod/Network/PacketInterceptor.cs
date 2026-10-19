namespace BossMod.Network;

[StructLayout(LayoutKind.Explicit, Pack = 1)]
unsafe struct ReceivedIPCPacket
{
    [FieldOffset(0x20)] public uint SourceActor;
    [FieldOffset(0x24)] public uint TargetActor;
    [FieldOffset(0x30)] public ulong PacketSize;
    [FieldOffset(0x38)] public ServerIPC.IPCHeader* PacketData;
}

[StructLayout(LayoutKind.Explicit, Pack = 1)]
unsafe struct ReceivedPacket
{
    [FieldOffset(0x10)] public ReceivedIPCPacket* IPC;
    [FieldOffset(0x18)] public long SendTimestamp;
}

[StructLayout(LayoutKind.Explicit, Size = 0x10)]
unsafe struct SentIPCHeader
{
    [FieldOffset(0x00)] public uint Opcode;
    [FieldOffset(0x08)] public ulong PayloadSize; // 0x10 (payload header) + actual data size
}

internal sealed class PacketInterceptor : IDisposable
{
    public delegate void ServerIPCReceivedDelegate(DateTime sendTimestamp, uint sourceServerActor, uint targetServerActor, ushort opcode, uint epoch, Span<byte> payload);
    public event ServerIPCReceivedDelegate? ServerIPCReceived;

    public delegate void ClientIPCSentDelegate(uint opcode, Span<byte> payload);
    public event ClientIPCSentDelegate? ClientIPCSent;

    private unsafe delegate bool FetchReceivedPacketDelegate(void* self, ReceivedPacket* outData);
    private readonly HookAddress<FetchReceivedPacketDelegate>? _fetchHook;

    private unsafe delegate byte SendPacketDelegate(void* self, SentIPCHeader* packet, int* a3, byte a4);
    private readonly HookAddress<SendPacketDelegate>? _sendHook;

    public bool ActiveRecv
    {
        get => _fetchHook?.Enabled ?? false;
        set
        {
            if (_fetchHook == null)
                Service.Log($"[NPI] Recv hook not found!");
            else
                _fetchHook.Enabled = value;
        }
    }

    public bool ActiveSend
    {
        get => _sendHook?.Enabled ?? false;
        set
        {
            if (_sendHook == null)
                Service.Log($"[NPI] Send hook not found!");
            else
                _sendHook.Enabled = value;
        }
    }

    public unsafe PacketInterceptor()
    {
        // alternative signatures - seem to be changing from build to build:
        // - E8 ?? ?? ?? ?? 84 C0 0F 85 ?? ?? ?? ?? 48 8D 35
        // - E8 ?? ?? ?? ?? 84 C0 0F 85 ?? ?? ?? ?? 44 0F B6 64 24
        var foundFetchAddress = Service.SigScanner.TryScanText("E8 ?? ?? ?? ?? 84 C0 0F 85 ?? ?? ?? ?? 48 8D 4C 24 ?? FF 15", out var fetchAddress)
            || Service.SigScanner.TryScanText("E8 ?? ?? ?? ?? 84 C0 0F 85 ?? ?? ?? ?? 44 0F B6 64 24", out fetchAddress);
        Service.Log($"[NPI] FetchReceivedPacket address = 0x{fetchAddress:X}");
        if (foundFetchAddress)
            _fetchHook = new(fetchAddress, FetchReceivedPacketDetour, false);

        _sendHook = new("48 89 5C 24 ?? 48 89 74 24 ?? 4C 89 64 24 ?? 55 41 56 41 57 48 8B EC 48 83 EC 70", SendPacketDetour, false);

        // potentially useful sigs from dalamud:
        // server ipc handler: 40 53 56 48 81 EC ?? ?? ?? ?? 48 8B 05 ?? ?? ?? ?? 48 33 C4 48 89 44 24 ?? 8B F2 --- void(void* self, uint targetId, void* dataPtr)
    }

    public void Dispose()
    {
        _fetchHook?.Dispose();
        _sendHook?.Dispose();
    }

    // fail-closed 約定（見 Util/DetourGuard.cs）：自訂邏輯（含訂閱者的回呼）進 try，Original 一律照樣呼叫、照樣回傳其結果。
    // outData 刻意不補判空：Original 就是負責**寫入** outData 的遊戲函式，它已經先解參考過了；packet 則是我們比 Original 先讀，有判空。
    // Original 回 false（這次沒取到封包）時不讀 outData。現行台服執行檔裡呼叫端每次呼叫前都把 outData->IPC 清成 null，
    //    所以今天這個判斷與下面的判空等價；留著是為了改版後呼叫端若不再清，沒取到那次不會去讀上一個已釋放封包的指標。

    private unsafe bool FetchReceivedPacketDetour(void* self, ReceivedPacket* outData)
    {
        var res = _fetchHook!.Original(self, outData);
        try
        {
            // 🔴 IPC 與 IPC->PacketData 都要判 —— 下面連著解參考 PacketData->MessageType／
            //    ->Epoch，並拿 PacketData+1 當 Span 的起點，少判一層是 AVE（攔不到）。
            //    null 時只是這一個封包不進網路記錄。
            if (res && outData->IPC != null && outData->IPC->PacketData != null)
            {
                ServerIPCReceived?.Invoke(
                    DateTimeOffset.FromUnixTimeMilliseconds(outData->SendTimestamp).DateTime,
                    outData->IPC->SourceActor,
                    outData->IPC->TargetActor,
                    outData->IPC->PacketData->MessageType,
                    outData->IPC->PacketData->Epoch,
                    new(outData->IPC->PacketData + 1, (int)outData->IPC->PacketSize - sizeof(ServerIPC.IPCHeader)));
            }
        }
        catch (Exception ex)
        {
            DetourGuard.Report(nameof(FetchReceivedPacketDetour), ex);
        }
        return res;
    }

    private unsafe byte SendPacketDetour(void* self, SentIPCHeader* packet, int* a3, byte a4)
    {
        try
        {
            // 🔴 這支是**我們比 Original 先解參考封包**（Original 在最後才呼叫），所以這裡的判空
            //    是有意義的：null 時我們不會搶在遊戲之前崩，且崩的堆疊會正確落在遊戲自己身上。
            if (packet != null)
                ClientIPCSent?.Invoke(packet->Opcode, new((byte*)packet + 0x20, (int)packet->PayloadSize - 0x10));
        }
        catch (Exception ex)
        {
            DetourGuard.Report(nameof(SendPacketDetour), ex);
        }
        return _sendHook!.Original(self, packet, a3, a4);
    }
}
