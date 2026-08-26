using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using FFXIVClientStructs.Interop;
using Dalamud.Bindings.ImGui;

namespace BossMod;

public sealed unsafe class DebugAddon : IDisposable
{
    delegate nint AddonReceiveEventDelegate(AtkEventListener* self, AtkEventType eventType, uint eventParam, AtkEvent* eventData, ulong* inputData);
    delegate void* AgentReceiveEventDelegate(AgentInterface* self, void* eventData, AtkValue* values, int valueCount, ulong eventKind);

    private readonly Dictionary<nint, HookAddress<AddonReceiveEventDelegate>> _rcvAddonHooks = [];
    private readonly Dictionary<nint, HookAddress<AgentReceiveEventDelegate>> _rcvAgentHooks = [];
    private readonly Dictionary<string, nint> _addonRcvs = [];
    private readonly Dictionary<uint, nint> _agentRcvs = [];
    private string _newHook = "";

    public DebugAddon()
    {
    }

    public void Dispose()
    {
        foreach (var h in _rcvAddonHooks.Values)
            h.Dispose();
        foreach (var h in _rcvAgentHooks.Values)
            h.Dispose();
    }

    // 📌 Draw 本身**不是** detour（稽核工具會把它列出來，因為兩個真正的 detour 是寫在它裡面的
    //    lambda，而 lambda 裡有 .Original( 呼叫）。真正要防護的是那兩個 lambda，見下方註解。
    //    Draw 走的是 Dalamud 的受管理繪製路徑，受管理例外不會穿過原生框架；但**裸指標解參考照樣是
    //    AVE**，所以這裡該做的是判空而不是包 try。
    public void Draw()
    {
        ImGui.TextUnformatted("Addons:");
        foreach (var (k, v) in _addonRcvs)
        {
            var hook = _rcvAddonHooks[v];
            if (ImGui.Button($"{(hook.Enabled ? "Disable" : "Enable")} {k} ({v:X})"))
                hook.Enabled ^= true;
        }

        ImGui.TextUnformatted("Agents:");
        foreach (var (k, v) in _agentRcvs)
        {
            var hook = _rcvAgentHooks[v];
            if (ImGui.Button($"{(hook.Enabled ? "Disable" : "Enable")} {k} ({v:X})"))
                hook.Enabled ^= true;
        }

        ImGui.InputText("Addon name / agent id", ref _newHook, 256);
        if (_newHook.Length > 0 && !_addonRcvs.ContainsKey(_newHook) && (AtkUnitBase*)Service.GameGui.GetAddonByName(_newHook).Address is var addon && addon != null)
        {
            ImGui.SameLine();
            if (ImGui.Button("Hook addon!"))
            {
                var address = (nint)addon->VirtualTable->ReceiveEvent;
                _addonRcvs[_newHook] = address;
                if (!_rcvAddonHooks.ContainsKey(address))
                {
                    var name = _newHook;
                    HookAddress<AddonReceiveEventDelegate> hook = null!;
                    hook = new(address, (self, eventType, eventParam, eventData, inputData) =>
                    {
                        try
                        {
                            // 🔴 inputData 是裸指標，而且**經常真的是 null** —— AtkEventListener::ReceiveEvent
                            //    的最後一個參數在 CS 的宣告裡就是 `AtkEventData* atkEventData = null`，
                            //    不帶 input 的事件型別（大多數）傳的就是 0。對 null 解參考是 AVE，
                            //    在 .NET Core 是 corrupted-state exception，上面那個 try 攔不到 ——
                            //    所以這裡必須判空，不能靠 try。
                            //    ⚠️ 非 null 時仍然是無界讀 3 個 ulong：這個介面沒有任何長度可查，
                            //    維持上游行為不動（要改就得改成只印 [0]，那會少掉除錯資訊）。
                            if (inputData != null)
                                Service.Log($"RCV: listener={name} {(nint)self:X}, type={eventType}, param={eventParam}, input={inputData[0]:X16} {inputData[1]:X16} {inputData[2]:X16}");
                            else
                                Service.Log($"RCV: listener={name} {(nint)self:X}, type={eventType}, param={eventParam}, input=<null>");
                        }
                        catch (Exception ex)
                        {
                            // fail-closed（見 Util/DetourGuard.cs）：記錄失敗不能把事件分派整條帶走，
                            // Original 照樣呼叫、照樣回傳其結果 —— addon 的行為完全不受我們影響。
                            DetourGuard.Report("DebugAddon.AddonReceiveEvent", ex);
                        }
                        return hook.Original(self, eventType, eventParam, eventData, inputData);
                    }, false);
                    // 🔴 先完成指派、最後才啟用。HookAddress 的建構式預設在回傳前就 Enable()，
                    //    而 `hook` 這個區域變數要等建構式回傳才被指派 —— 那個窗口裡若 detour 觸發，
                    //    `hook.Original` 會是 NullReferenceException 並逸出到原生框架。
                    //    結束狀態與原本完全相同（已註冊、已啟用）。
                    _rcvAddonHooks[address] = hook;
                    hook.Enabled = true;
                }
            }
        }
        // 🔴 AgentModule.Instance() 是手寫的鏈式 Instance()（內部走 Framework → UIModule → GetAgentModule），
        //    在標題畫面／登入中／退出時回 null。原本直接 `->GetAgentByInternalId` 就是對 null 解參考 ＝ AVE。
        //    判空後這個區塊整段不畫，等於「還不能掛 agent hook」。
        if (_newHook.Length > 0 && uint.TryParse(_newHook, out var agentId) && agentId > 0 && !_agentRcvs.ContainsKey(agentId)
            && AgentModule.Instance() is var agentModule && agentModule != null
            && agentModule->GetAgentByInternalId((AgentId)agentId) is var agent && agent != null)
        {
            ImGui.SameLine();
            if (ImGui.Button("Hook agent!"))
            {
                var address = (nint)agent->VirtualTable->ReceiveEvent;
                _agentRcvs[agentId] = address;
                if (!_rcvAgentHooks.ContainsKey(address))
                {
                    HookAddress<AgentReceiveEventDelegate> hook = null!;
                    hook = new(address, (self, eventData, values, valueCount, eventKind) =>
                    {
                        try
                        {
                            // values 的判空在 AtkValuesString 裡（valueCount>0 而 values==null 是 AVE）。
                            Service.Log($"RCV: listener={agentId} {(nint)self:X}, kind={eventKind}, values={AtkValuesString(values, valueCount)}");
                        }
                        catch (Exception ex)
                        {
                            // fail-closed：同上，Original 一律留在 try 外。
                            DetourGuard.Report("DebugAddon.AgentReceiveEvent", ex);
                        }
                        return hook.Original(self, eventData, values, valueCount, eventKind);
                    }, false);
                    // 🔴 與 addon 那支同理：指派完成後才啟用，避免 `hook` 還是 null 的窗口。
                    _rcvAgentHooks[address] = hook;
                    hook.Enabled = true;
                }
            }
        }
    }

    private string AtkValuesString(AtkValue* values, int count)
    {
        // 🔴 values 是遊戲傳進來的裸指標。count>0 而 values==null 時 `values[i]` 就是對 null 解參考 ＝ AVE，
        //    try/catch 攔不到，所以在這裡判空。count<=0 時原本就不會進迴圈，行為不變。
        if (values == null)
            return count > 0 ? $"<null x{count}>" : "[]";
        var res = "[";
        for (var i = 0; i < count; ++i)
        {
            if (i > 0)
                res += ", ";
            res += values[i].Type switch
            {
                FFXIVClientStructs.FFXIV.Component.GUI.ValueType.Int => $"int {values[i].Int}",
                FFXIVClientStructs.FFXIV.Component.GUI.ValueType.Bool => $"bool {values[i].Byte}",
                FFXIVClientStructs.FFXIV.Component.GUI.ValueType.UInt => $"uint {values[i].UInt}",
                FFXIVClientStructs.FFXIV.Component.GUI.ValueType.Float => $"int {values[i].Float}",
                FFXIVClientStructs.FFXIV.Component.GUI.ValueType.String => $"string",
                FFXIVClientStructs.FFXIV.Component.GUI.ValueType.String8 => $"string8",
                FFXIVClientStructs.FFXIV.Component.GUI.ValueType.Vector => $"vector",
                FFXIVClientStructs.FFXIV.Component.GUI.ValueType.Pointer => $"pointer",
                FFXIVClientStructs.FFXIV.Component.GUI.ValueType.ManagedString => $"astring",
                FFXIVClientStructs.FFXIV.Component.GUI.ValueType.ManagedVector => $"avector",
                _ => $"{values[i].Type} unknown"
            };
        }
        res += "]";
        return res;
    }
}
