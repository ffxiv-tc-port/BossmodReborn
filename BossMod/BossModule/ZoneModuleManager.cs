namespace BossMod;

public sealed class ZoneModuleManager : IDisposable
{
    public readonly WorldState WorldState;
    public static readonly ZoneModuleConfig Config = Service.Config.Get<ZoneModuleConfig>();
    private readonly EventSubscriptions _subsciptions;

    public ZoneModule? ActiveModule;
    public Event<ZoneModule> ModuleLoaded = new();
    public Event<ZoneModule> ModuleUnloaded = new();

    public ZoneModuleManager(WorldState ws)
    {
        WorldState = ws;
        _subsciptions = new
        (
            WorldState.CurrentZoneChanged.Subscribe(op => OnZoneChanged(op.CFCID))
        );
        OnZoneChanged(ws.CurrentCFCID);
    }

    public void Dispose()
    {
        _subsciptions.Dispose();
        ActiveModule?.Dispose();
    }

    private void OnZoneChanged(uint cfcid)
    {
        if (ActiveModule != null)
        {
            Service.Log($"[ZMM] Unloading zone module '{ActiveModule.GetType()}'");
            // ⚠️ 排序陷阱:ModuleUnloaded.Fire() 排在下面的 ActiveModule.Dispose() 之前,
            //    而 Event<T>.Fire 就是一個裸的多播委派 Invoke(Util/Event.cs),沒有任何防護。
            //    ⇒ 只要有訂閱者擲例外,它後面的 Dispose() 與 ActiveModule = null 就整段不執行:
            //      舊的區域模組連同它自己的訂閱一起留著,而外面看起來只像「換區時報了一個錯」。
            // 📌 今天這條是 no-op:ZoneModuleManager.ModuleUnloaded 全 repo 零訂閱者
            //    (2026-09-06 實查——唯一訂閱 ModuleUnloaded 的是 ReplayBuilder.cs:62,
            //    而它訂的是 BossModuleManager 的那一顆,不是這一顆)。
            //    第一個訂閱者加進來的那天,上面那段就會從理論變成真的漏洞。
            // 📌 同一個形狀在 BossModuleManager.UnloadModule 也有,而那一顆**已經有**訂閱者。
            // 🔴 這裡刻意不改順序:先 Fire、讓訂閱者拿到「還沒被釋放」的模組是既有語意,
            //    對調是行為改變,要改請連訂閱者的期待一起裁決。
            ModuleUnloaded.Fire(ActiveModule);
            ActiveModule.Dispose();
            ActiveModule = null;
        }

        var m = ZoneModuleRegistry.CreateModule(WorldState, cfcid, Config.MinMaturity);
        if (m != null)
        {
            Service.Log($"[ZMM] Loading module '{m.GetType()}' for zone {cfcid}");
            ActiveModule = m;
            ModuleLoaded.Fire(m);
        }
    }
}
