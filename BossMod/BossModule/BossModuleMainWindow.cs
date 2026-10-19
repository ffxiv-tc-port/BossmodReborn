using Dalamud.Interface;
using Dalamud.Bindings.ImGui;

namespace BossMod;

public sealed class BossModuleMainWindow : UIWindow
{
    private readonly BossModuleManager _mgr;
    private readonly ZoneModuleManager _zmm;
    private readonly Action _openConfig;
    private readonly GenericTrialRadar _genericRadar;
    private readonly string _genericRadarWindowName;
    private bool _showGenericRadar; // PreOpenCheck 決定、同一幀的 Draw 沿用

    private const string _windowID = "###Boss module";
    private const string _moduleListWindowName = "Loaded boss modules" + _windowID;
    private string? _moduleWindowName; // 上一次組好的模組標題；下面兩個欄位是它對應的模組型別與種類
    private Type? _moduleWindowNameType;
    private bool _moduleWindowNameZone;

    public BossModuleMainWindow(BossModuleManager mgr, ZoneModuleManager zmm, AIHints hints, AIHintsBuilder hintsBuilder, Action openConfig) : base(_windowID, false, new(400, 400))
    {
        _mgr = mgr;
        _zmm = zmm;
        _openConfig = openConfig;
        _genericRadar = new(mgr.WorldState, hints, hintsBuilder);
        _genericRadarWindowName = Loc.T("Trial radar (no boss module)") + _windowID;
        RespectCloseHotkey = false;
        TitleBarButtons.Add(new() { Icon = FontAwesomeIcon.Cog, IconOffset = new(1), Click = _ => OpenModuleConfig() });
    }

    public override void PreOpenCheck()
    {
        base.PreOpenCheck();
        var showZoneModule = ShowZoneModule();
        // 通用雷達只在沒有任何模組載入、也沒有區域模組要畫時才問；設定關著或有模組時在這一行短路
        _showGenericRadar = BossModuleManager.Config.Enable && BossModuleManager.Config.ShowRadarInTrialsWithoutModule && !showZoneModule && _mgr.LoadedModules.Count == 0 && _genericRadar.ShouldShow();
        IsOpen = BossModuleManager.Config.Enable && (_mgr.LoadedModules.Count > 0 || showZoneModule || _showGenericRadar);
        ShowCloseButton = _mgr.ActiveModule != null && !showZoneModule;
        WindowName = _showGenericRadar ? _genericRadarWindowName : showZoneModule ? ModuleWindowName(_zmm.ActiveModule?.GetType(), true) : _mgr.ActiveModule != null ? ModuleWindowName(_mgr.ActiveModule.GetType(), false) : _moduleListWindowName;
        Flags = ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse;
        if (BossModuleManager.Config.TrishaMode)
            Flags |= ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoBackground;
        if (BossModuleManager.Config.Lock)
            Flags |= ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoInputs;
        ForceMainWindow = BossModuleManager.Config.TrishaMode; // NoBackground flag without ForceMainWindow works incorrectly for whatever reason

        if (BossModuleManager.Config.ShowWorldArrows && _mgr.ActiveModule != null && _mgr.WorldState.Party[PartyState.PlayerSlot] is var pc && pc != null)
            DrawMovementHints(_mgr.ActiveModule.CalculateMovementHintsForRaidMember(PartyState.PlayerSlot, ref pc), pc.PosRot.Y);
    }

    public override void OnOpen()
    {
        Service.Log($"[BMM] Opening main window; there are {_mgr.LoadedModules.Count} loaded modules, active is {_mgr.ActiveModule?.GetType().FullName ?? "<n/a>"}; zone module is {_zmm.ActiveModule?.GetType().FullName ?? "<n/a>"}");
    }

    public override void OnClose()
    {
        Service.Log($"[BMM] Closing main window; there are {_mgr.LoadedModules.Count} loaded modules, active is {_mgr.ActiveModule?.GetType().FullName ?? "<n/a>"}; zone module is {_zmm.ActiveModule?.GetType().FullName ?? "<n/a>"}");
    }

    public override void PostDraw()
    {
        if (!IsOpen)
        {
            // user pressed close button - deactivate current module and show module list instead
            // show module list instead of boss module
            Service.Log("[BMM] Bossmod window closed by user, showing module list instead...");
            _mgr.ActiveModule = null;
            IsOpen = true;
        }
        // base.PostDraw() 平衡 Dalamud Window 基底在 PreDraw() 推的樣式堆疊：
        // 覆寫 PostDraw 而不呼叫 base，那一推就永遠沒有對應的 pop。
        base.PostDraw();
    }

    public override void Draw()
    {
        if (ShowZoneModule())
        {
            _zmm.ActiveModule?.DrawGlobalHints();
        }
        else if (_mgr.ActiveModule != null)
        {
            try
            {
                _mgr.ActiveModule.Draw(BossModuleManager.Config.RotateArena ? _mgr.WorldState.Client.CameraAzimuth : BossModuleManager.Config.FlipArena ? 180f.Degrees() : default, PartyState.PlayerSlot, !BossModuleManager.Config.HintsInSeparateWindow, true);
            }
            catch (Exception ex)
            {
                Service.Log($"Boss module draw crashed: {ex}");
                _mgr.ActiveModule = null;
            }
        }
        else if (_showGenericRadar)
        {
            _genericRadar.Draw(BossModuleManager.Config.RotateArena ? _mgr.WorldState.Client.CameraAzimuth : BossModuleManager.Config.FlipArena ? 180f.Degrees() : default);
        }
        else
        {
            var count = _mgr.LoadedModules.Count;
            for (var i = 0; i < count; ++i)
            {
                var m = _mgr.LoadedModules[i];
                var oidType = BossModuleRegistry.FindByOID(m.PrimaryActor.OID)?.ObjectIDType;
                var oidName = oidType?.GetEnumName(m.PrimaryActor.OID);
                if (ImGui.Button($"{m.GetType()} ({m.PrimaryActor.InstanceID:X} '{m.PrimaryActor.Name}' {oidName})"))
                    _mgr.ActiveModule = m;
            }
        }

        RadarContextMenu.Draw(_mgr, _openConfig);
    }

    private void DrawMovementHints(BossComponent.MovementHints? arrows, float y)
    {
        if (arrows == null || arrows.Count == 0 || Camera.Instance == null)
            return;

        foreach ((var start, var end, var color) in arrows)
        {
            Vector3 start3 = start.ToVec3(y);
            Vector3 end3 = end.ToVec3(y);
            Camera.Instance.DrawWorldLine(start3, end3, color);
            var dir = Vector3.Normalize(end3 - start3);
            var arrowStart = end3 - 0.4f * dir;
            var offset = 0.07f * Vector3.Normalize(Vector3.Cross(Vector3.UnitY, dir));
            Camera.Instance.DrawWorldLine(arrowStart + offset, end3, color);
            Camera.Instance.DrawWorldLine(arrowStart - offset, end3, color);
        }
    }

    private void OpenModuleConfig()
    {
        if (_mgr.ActiveModule?.Info != null)
            _ = new BossModuleConfigWindow(_mgr.ActiveModule.Info, _mgr.WorldState);
    }

    // 每幀呼叫：模組型別與種類沒換就回上一次組好的字串，不重組。
    // 標題的字不可改：使用者看得到，也是 ImGui 的視窗名稱。
    private string ModuleWindowName(Type? moduleType, bool zone)
    {
        if (_moduleWindowName == null || _moduleWindowNameType != moduleType || _moduleWindowNameZone != zone)
        {
            _moduleWindowNameType = moduleType;
            _moduleWindowNameZone = zone;
            _moduleWindowName = (zone ? $"Zone module ({moduleType?.Name})" : $"Boss module ({moduleType?.Name})") + _windowID;
        }
        return _moduleWindowName;
    }

    private bool ShowZoneModule() => BossModuleManager.Config.ShowGlobalHints && !BossModuleManager.Config.HintsInSeparateWindow && _mgr.ActiveModule?.StateMachine.ActivePhase == null && (_zmm.ActiveModule?.WantDrawHints() ?? false);
}
