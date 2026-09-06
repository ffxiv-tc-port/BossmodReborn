namespace BossMod;

// class that creates and manages instances of proper boss modules in response to world state changes
public sealed class BossModuleManager : IDisposable
{
    public readonly WorldState WorldState;
    public readonly RaidCooldowns RaidCooldowns;
    public static readonly BossModuleConfig Config = Service.Config.Get<BossModuleConfig>();
    private readonly EventSubscriptions _subsciptions;

    public readonly List<BossModule> LoadedModules = [];
    public Event<BossModule> ModuleLoaded = new();
    public Event<BossModule> ModuleUnloaded = new();
    public Event<BossModule> ModuleActivated = new();
    public Event<BossModule> ModuleDeactivated = new();

    // drawn module among loaded modules; this can be changed explicitly if needed
    // usually we don't have multiple concurrently active modules, since this prevents meaningful cd planning, raid cooldown tracking, etc.
    // but it can theoretically happen e.g. around checkpoints and in typically trivial outdoor content
    private BossModule? _activeModule;
    private bool _activeModuleOverridden;
    public BossModule? ActiveModule
    {
        get => _activeModule;
        set
        {
            Service.Log($"[BMM] Active module override: from {_activeModule?.GetType().FullName ?? "<n/a>"} (manual-override={_activeModuleOverridden}) to {value?.GetType().FullName ?? "<n/a>"}");
            _activeModule = value;
            _activeModuleOverridden = true;
        }
    }

    public BossModuleManager(WorldState ws)
    {
        WorldState = ws;
        RaidCooldowns = new(ws);
        _subsciptions = new
        (
            WorldState.Actors.Added.Subscribe(ActorAdded),
            Config.Modified.ExecuteAndSubscribe(ConfigChanged)
        );

        foreach (var a in WorldState.Actors)
            ActorAdded(a);
    }

    public void Dispose()
    {
        _activeModule = null;
        foreach (var m in LoadedModules)
            m.Dispose();
        LoadedModules.Clear();

        _subsciptions.Dispose();
        RaidCooldowns.Dispose();
    }

    public void Update()
    {
        // update all loaded modules, handle activation/deactivation
        var bestPriority = 0;
        BossModule? bestModule = null;
        var anyModuleActivated = false;
        for (var i = 0; i < LoadedModules.Count; ++i)
        {
            var m = LoadedModules[i];
            var wasActive = m.StateMachine.ActiveState != null;
            var allowUpdate = wasActive || !LoadedModules.Any(other => other.StateMachine.ActiveState != null && other.GetType() == m.GetType()); // hack: forbid activating multiple modules of the same type
            bool isActive;
            try
            {
                if (allowUpdate)
                    m.Update();
                isActive = m.StateMachine.ActiveState != null;
            }
            catch (Exception ex)
            {
                Service.Log($"Boss module {m.GetType()} crashed: {ex}");
                wasActive = true; // force unload if exception happened before activation
                isActive = false;
            }

            // ⚠️ 排序陷阱(與 UnloadModule 裡的同一個形狀,差別是這兩顆事件在實機上真的有訂閱者):
            //    ReplayManagementWindow 的建構式訂了 ModuleActivated/ModuleDeactivated,
            //    處理常式會呼叫 StartRecording/StopRecording —— 那是會擲例外的檔案 I/O。
            //    Event<T>.Fire 零防護 ⇒ 這裡(以及下面 CheckReset 那條路徑上的 Fire)擲一次例外,
            //    後面的 UnloadModule 就不會執行(模組漏釋放),而且例外會一路傳出 Update()、
            //    傳到 Plugin.DrawUI —— 那是 Dalamud 的 UiBuilder.Draw 回呼。
            //    這次只加註不改碼:加隔離是行為改變,留給呼叫端裁決。
            // if module was activated or deactivated, notify listeners
            if (isActive != wasActive)
                (isActive ? ModuleActivated : ModuleDeactivated).Fire(m);

            // unload module either if it became deactivated or its primary actor disappeared without ever activating
            if (!isActive && (wasActive || m.PrimaryActor.IsDestroyed))
            {
                UnloadModule(i--);
                continue;
            }

            // if module is active and wants to be reset, oblige
            if (isActive && m.CheckReset())
            {
                ModuleDeactivated.Fire(m);
                var actor = m.PrimaryActor;
                UnloadModule(i--);
                if (!actor.IsDestroyed)
                    ActorAdded(actor);
                continue;
            }

            // module remains loaded
            var priority = ModuleDisplayPriority(m);
            if (priority > bestPriority)
            {
                bestPriority = priority;
                bestModule = m;
            }

            if (!wasActive && isActive)
            {
                Service.Log($"[BMM] Boss module '{m.GetType()}' for actor {m.PrimaryActor.InstanceID:X} ({m.PrimaryActor.OID:X}) '{m.PrimaryActor.Name}' activated");
                anyModuleActivated |= true;
            }
        }

        var curPriority = ModuleDisplayPriority(_activeModule);
        if (bestPriority > curPriority && (anyModuleActivated || !_activeModuleOverridden))
        {
            Service.Log($"[BMM] Active module change: from {_activeModule?.GetType().FullName ?? "<n/a>"} (prio {curPriority}, manual-override={_activeModuleOverridden}) to {bestModule?.GetType().FullName ?? "<n/a>"} (prio {bestPriority})");
            _activeModule = bestModule;
            _activeModuleOverridden = false;
        }
    }

    private void LoadModule(BossModule m)
    {
        LoadedModules.Add(m);
        Service.Log($"[BMM] Boss module '{m.GetType()}' loaded for actor {m.PrimaryActor}");
        ModuleLoaded.Fire(m);
    }

    private void UnloadModule(int index)
    {
        var m = LoadedModules[index];
        Service.Log($"[BMM] Boss module '{m.GetType()}' unloaded for actor {m.PrimaryActor}");
        // 🔴 訂閱者擲出的受管理例外不可以害死這一行以下的釋放。Event<T>.Fire 是裸的多播委派
        //    Invoke(Util/Event.cs 的 _ev?.Invoke(a1),零防護),所以任何一個訂閱者擲例外,
        //    都會同時吃掉其餘訂閱者**與這一行以下的全部敘述**:模組不會被 Dispose
        //    (它的 WorldState 事件訂閱、元件、Obstacles 全部留著指向已卸載的實例),
        //    也不會從 LoadedModules 移除 —— 下一幀的 Update() 又會把它跑一次。
        // 📌 順序刻意不動:唯一的訂閱者 ReplayBuilder.ModuleUnloaded 要在這時候讀
        //    module.PrimaryActor.InstanceID 把那場戰鬥收尾;改成先 Dispose 是行為改變,不在這次的範圍。
        // 📌 但那個訂閱者掛在 ReplayBuilder 自己 new 出來的 BossModuleManager 上,不是 Plugin 那一顆
        //    (ReplayBuilder 建構式裡的 _ws/_mgr 都是它私有的)⇒ 實機戰鬥路徑上這顆事件目前是
        //    **零訂閱者**,下面這段對它是純保險;真的會跑到訂閱者的是「解析回放紀錄」那條路徑
        //    (ReplayParserLog.Parse -> ReplayBuilder.FinishFrame -> _mgr.Update)。
        // 🔴 這**不是** AccessViolationException 的防護 —— AVE 在 .NET Core 是
        //    corrupted-state exception,catch(Exception) 攔不到;這裡只處理受管理例外。
        try
        {
            ModuleUnloaded.Fire(m);
        }
        catch (Exception ex)
        {
            try
            {
                Service.Logger.Error(ex, $"[UnloadModule] 「{m.GetType().Name}」的 ModuleUnloaded 訂閱者擲出例外，已略過通知、照常釋放這個模組。");
            }
            catch
            {
                // 連寫 log 都失敗時也不能中斷釋放 —— 這裡已經沒有別的地方可以回報了。
            }
        }
        if (_activeModule == m)
        {
            _activeModule = null;
            _activeModuleOverridden = false;
        }
        m.Dispose();
        LoadedModules.RemoveAt(index);
    }

    private static int ModuleDisplayPriority(BossModule? m)
    {
        if (m == null)
            return 0;
        if (m.StateMachine.ActiveState != null)
            return 4;
        if (m.PrimaryActor.InstanceID == 0)
            return 2; // demo module
        if (!m.PrimaryActor.IsDestroyed && !m.PrimaryActor.IsDead && m.PrimaryActor.IsTargetable)
            return 3;
        return 1;
    }

    private DemoModule CreateDemoModule() => new(WorldState, new(0, 0, -1, 0, "", 0, ActorType.None, Class.None, 0, default));

    private void ActorAdded(Actor actor)
    {
        var m = BossModuleRegistry.CreateModuleForActor(WorldState, actor, Config.MinMaturity);
        if (m != null)
        {
            LoadModule(m);
        }
    }

    private void ConfigChanged()
    {
        var demoIndex = LoadedModules.FindIndex(m => m is DemoModule);
        if (Config.ShowDemo && demoIndex < 0)
            LoadModule(CreateDemoModule());
        else if (!Config.ShowDemo && demoIndex >= 0)
            UnloadModule(demoIndex);
    }
}
