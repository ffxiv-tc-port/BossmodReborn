using Dalamud.Hooking;
using InteropGenerator.Runtime;

namespace BossMod;

// very simple wrappers for hooks, that provide some quality of life (no need to repeat delegate types multiple times, etc)
// if the address fails to resolve (typically because a game update changed signatures), the hook is not installed and the corresponding feature silently stops working instead of crashing the game on plugin load
public sealed class HookAddress<T> : IDisposable where T : Delegate
{
    private readonly Hook<T>? _hook;

    public nint Address => _hook?.Address ?? 0;
    // note: detours can still fire after the hook is disposed (plugin unload/hot-update race), so always use the dispose-safe original to avoid ObjectDisposedException killing the game
    public T Original => _hook != null ? _hook.OriginalDisposeSafe : throw new InvalidOperationException($"Hook {typeof(T)} was not installed (address/signature resolution failed - probably a game update)");
    public bool IsDisposed => _hook == null || _hook.IsDisposed;
    public bool Enabled
    {
        get => _hook?.IsEnabled ?? false;
        set
        {
            if (_hook == null)
                return;
            if (value)
                _hook.Enable();
            else
                _hook.Disable();
        }
    }

    public HookAddress(Address address, T detour, bool autoEnable = true) : this(address.Value, detour, autoEnable) { }
    public HookAddress(string signature, T detour, bool autoEnable = true) : this(ResolveSignature(signature), detour, autoEnable) { }
    public HookAddress(nint address, T detour, bool autoEnable = true)
    {
        Service.Log($"Hooking {typeof(T)} @ 0x{address:X}");
        if (address <= 0)
        {
            Service.Log($"[HookAddress] Address for {typeof(T)} did not resolve, hook is not installed; the corresponding feature will not work");
            return;
        }
        _hook = Service.Hook.HookFromAddress(address, detour);
        if (autoEnable)
            _hook.Enable();
    }

    public void Dispose() => _hook?.Dispose();

    private static nint ResolveSignature(string signature)
    {
        if (Service.SigScanner.TryScanText(signature, out var address))
            return address;
        Service.Log($"[HookAddress] Signature not found: {signature}");
        return 0;
    }
}
