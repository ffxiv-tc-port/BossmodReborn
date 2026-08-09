using BossMod.Pathfinding;
using Dalamud.Bindings.ImGui;

using static FFXIVClientStructs.FFXIV.Client.Game.InstanceContent.InstanceContentDeepDungeon;

namespace BossMod.Global.DeepDungeon;

enum OID : uint
{
    CairnPalace = 0x1EA094,
    BeaconHoH = 0x1EA9A3,
    PylonEO = 0x1EB867,
    SilverCoffer = 0x1EA13D,
    GoldCoffer = 0x1EA13E,
    BandedCofferIndicator = 0x1EA1F6,
    BandedCoffer = 0x1EA1F7,
}

enum SID : uint
{
    Silence = 7,
    Pacification = 620,
    ItemPenalty = 1094,

    PhysicalDamageUp = 53,
    DamageUp = 61,
    DreadBeastAura = 2056, // unnamed status, displays red fog vfx on actor
    EvasionUp = 2402, // applied by Peculiar Light from orthos diplocaulus

    StoneCurse = 437, // petrification (on enemies)
    AutoHealPenalty = 1097,
}

public abstract class AutoClear : ZoneModule
{
    public readonly int LevelCap;

    public static readonly HashSet<uint> BronzeChestIDs = [
        // PotD
        782, 783, 784, 785, 786, 787, 788, 789, 790, 802, 803, 804, 805,
        // HoH
        1036, 1037, 1038, 1039, 1040, 1041, 1042, 1043, 1044, 1045, 1046, 1047, 1048, 1049,
        // EO
        1541, 1542, 1543, 1544, 1545, 1546, 1547, 1548, 1549, 1550, 1551, 1552, 1553, 1554
    ];
    public static readonly HashSet<uint> RevealedTrapOIDs = [0x1EA08E, 0x1EA08F, 0x1EA090, 0x1EA091, 0x1EA092, 0x1EA9A0, 0x1EB864];

    protected readonly List<(Actor Source, float Inner, float Outer)> Donuts = [];
    protected readonly List<(Actor Source, float Radius)> Circles = [];
    protected readonly List<(Actor Source, float Radius)> KnockbackZones = [];
    protected readonly List<(Actor Source, AOEShape Zone)> Voidzones = [];
    private readonly List<Gaze> Gazes = [];
    protected readonly List<Actor> Interrupts = [];
    protected readonly List<Actor> Stuns = [];
    protected readonly List<(Actor Actor, DateTime Timeout)> Spikes = [];
    protected readonly List<Actor> HintDisabled = [];
    private readonly List<Actor> LOS = [];
    private readonly List<WPos> IgnoreTraps = [];

    private readonly Dictionary<ulong, (WPos, Bitmap)> _losCache = [];

    public record class Gaze(Actor Source, AOEShape Shape);

    protected static readonly AutoDDConfig Config = Service.Config.Get<AutoDDConfig>();
    private readonly EventSubscriptions _subscriptions;
    private readonly WPos[] _trapsCurrentZone = [];

    private readonly Dictionary<ulong, PomanderID> _chestContentsGold = [];
    private readonly Dictionary<ulong, int> _chestContentsSilver = [];
    private readonly HashSet<ulong> _openedChests = [];
    private readonly HashSet<ulong> _fakeExits = [];
    private PomanderID? _lastChestContentsGold;
    private bool _lastChestMagicite;
    private bool _trapsHidden = true;

    private readonly List<(Wall Wall, bool Rotated)> Walls = [];

    /// <summary>
    /// 每個房間格子的中心世界座標；null＝這一格在本層的版面裡沒有房間，或還沒載入。
    /// </summary>
    /// <remarks>
    /// ⚠️ 這裡以前是 <c>List&lt;WPos&gt;</c>，而且**只在 <c>room &gt; 0</c> 時 Add** ——
    /// 也就是索引跟房號對不起來（第 0 格沒房間時，清單第 0 筆其實是第 1 間房）。
    /// 那個欄位從頭到尾沒有任何地方讀取，所以錯位一直沒被發現。改成 25 格對齊陣列。
    /// <para>
    /// 🔴 <b>座標來源是上游從國際服 dump 出來的寫死數值</b>（<see cref="LoadedFloors"/>），
    /// 台服不保證相同。所有用到這份座標的功能都必須先過
    /// <see cref="CheckRoomCoords"/> 的校驗閘門。
    /// </para>
    /// </remarks>
    private readonly WPos?[] RoomCenters = new WPos?[DeepDungeonState.NumRooms];

    private readonly List<WPos> ProblematicTrapLocations = [];

    private int Kills;
    private int DesiredRoom;
    private bool BetweenFloors;
    private (int from, int to) _lastPathfindFailureLogged = (-1, -1);

    /// <summary>目前的目標房間是誰決定的。</summary>
    protected enum DestinationSource
    {
        /// <summary>使用者自己在小地圖上點的。</summary>
        User,
        /// <summary>被「探索所有房間」覆寫掉了。</summary>
        FullClear,
        /// <summary>使用者沒指定，由「自動前往通道石」填入。</summary>
        AutoPassage
    }

    private DestinationSource _destinationSource;

    /// <summary>上一次樓層尋路是不是找不到路（連通的房間還沒探索到時是正常現象）。</summary>
    private bool _lastPathfindFailed;

    protected struct PlayerImmuneState
    {
        public DateTime RoleBuffExpire; // 0 if not active
        public DateTime JobBuffExpire; // 0 if not active
        public bool KnockbackPenalty;

        public readonly bool ImmuneAt(DateTime time) => KnockbackPenalty || RoleBuffExpire > time || JobBuffExpire > time;
    }

    private readonly PlayerImmuneState[] _playerImmunes = new PlayerImmuneState[4];

    private ObstacleMapManager _obstacles;

    protected DeepDungeonState Palace => World.DeepDungeon;

    protected AutoClear(WorldState ws, int LevelCap) : base(ws)
    {
        this.LevelCap = LevelCap;
        _obstacles = new(ws);

        _subscriptions = new(
            ws.SystemLogMessage.Subscribe(OnSystemLogMessage),
            ws.Actors.CastStarted.Subscribe(OnCastStarted),
            ws.Actors.CastFinished.Subscribe(OnCastFinished),
            ws.Actors.CastEvent.Subscribe(OnEventCast),
            ws.Actors.Added.Subscribe(OnActorCreated),
            ws.Actors.InCombatChanged.Subscribe(OnActorCombatChanged),
            ws.Actors.StatusGain.Subscribe(OnActorStatusGain),
            ws.Actors.StatusLose.Subscribe(OnActorStatusLose),
            ws.Actors.IsDeadChanged.Subscribe(op =>
            {
                if (!op.IsAlly && op.IsDead)
                    ++Kills;
            }),
            ws.Actors.EventOpenTreasure.Subscribe(OnOpenTreasure),
            ws.Actors.EventObjectAnimation.Subscribe(OnEObjAnim),
            ws.DeepDungeon.MapDataChanged.Subscribe(_ =>
            {
                BetweenFloors = false;
                if (Walls.Count == 0)
                    LoadWalls();
            })
        );

        _trapsCurrentZone = GeneratedTrapData.Traps.TryGetValue(ws.CurrentZone, out var locations) ? locations : [];

        ProblematicTrapLocations.AddRange(ProblematicTrapLocations);
        IgnoreTraps.AddRange(ProblematicTrapLocations);
    }

    protected override void Dispose(bool disposing)
    {
        // 我們自己叫起來的移動不要留給下一個場景。
        // 🔴 Dispose 期間的 IPC 要整個包起來：外掛卸載時對方可能已經先走一步，
        //    這裡擲例外會打斷後面的 Dispose 鏈（全艦隊踩過的形狀）。
        if (WalkActive)
        {
            try
            {
                DeepDungeonNav.Stop();
            }
            catch (Exception ex)
            {
                Service.Log($"[DD nav] Dispose 時停止移動失敗（可忽略）: {ex.Message}");
            }
        }

        _subscriptions.Dispose();
        _obstacles.Dispose();
        base.Dispose(disposing);
    }

    protected virtual void OnCastStarted(Actor actor) { }

    protected virtual void OnCastFinished(Actor actor) { }
    protected virtual void OnEventCast(Actor actor, ActorCastEvent ev) { }

    private void OnActorStatusGain(Actor actor, int index)
    {
        var status = actor.Statuses[index];

        switch (status.ID)
        {
            case (uint)WHM.SID.Surecast:
            case (uint)WAR.SID.ArmsLength:
                var slot1 = World.Party.FindSlot(actor.InstanceID);
                if (slot1 >= 0)
                    _playerImmunes[slot1].RoleBuffExpire = status.ExpireAt;
                break;
            case (uint)WAR.SID.InnerStrength:
                var slot2 = World.Party.FindSlot(actor.InstanceID);
                if (slot2 >= 0)
                    _playerImmunes[slot2].JobBuffExpire = status.ExpireAt;
                break;
            // Knockback Penalty floor effect
            case 1096:
            case 1512:
                var slot3 = World.Party.FindSlot(actor.InstanceID);
                if (slot3 >= 0)
                    _playerImmunes[slot3].KnockbackPenalty = true;
                break;
        }

        OnStatusGain(actor, status);
    }

    protected virtual void OnStatusGain(Actor actor, ActorStatus status) { }

    private void OnActorStatusLose(Actor actor, int index)
    {
        var status = actor.Statuses[index];
        OnStatusLose(actor, status);
    }

    protected virtual void OnStatusLose(Actor actor, ActorStatus status) { }

    protected virtual void OnActorCombatChanged(Actor actor) { }

    private void OnSystemLogMessage(WorldState.OpSystemLogMessage op)
    {
        switch (op.MessageId)
        {
            case 7222: // pomander overcap
                _lastChestContentsGold = (PomanderID)op.Args[0];
                break;
            case 7248: // transference initiated
                ClearState();
                break;
            case 7255: // safety used
            case 7256: // sight used
                _trapsHidden = false;
                break;
            case 10287: // demiclone overcap
                _lastChestMagicite = true;
                break;
        }
    }

    private void OnOpenTreasure(Actor chest) => _openedChests.Add(chest.InstanceID);

    private void OnEObjAnim(Actor actor, ushort p1, ushort p2)
    {
        // fake beacon deactivation; accompanied by system log #9217 but it does not indicate a specific actor
        if (actor.OID == (uint)OID.BeaconHoH && p1 == 0x0400 && p2 == 0x0800)
            _fakeExits.Add(actor.InstanceID);
    }

    protected virtual void OnChangeFloors() { }

    private void ClearState()
    {
        // 換層了，上一層算出來的路徑一律作廢；我們發起的移動也停掉
        // （角色會被傳走，繼續照舊路徑走是沒有意義而且可能有害的）
        if (WalkActive)
            RequestWalkStop();
        _walkMessage = null;
        _walkTargetRoom = -1;
        _walkCorridor.Clear();

        Donuts.Clear();
        Circles.Clear();
        Gazes.Clear();
        Interrupts.Clear();
        Stuns.Clear();
        Spikes.Clear();
        HintDisabled.Clear();
        LOS.Clear();
        Walls.Clear();
        Array.Clear(RoomCenters);
        _coordGateLogged = false;
        IgnoreTraps.Clear();
        IgnoreTraps.AddRange(ProblematicTrapLocations);
        DesiredRoom = 0;
        Kills = 0;
        Array.Fill(_playerImmunes, default);
        _lastChestContentsGold = null;
        _lastChestMagicite = false;
        _chestContentsGold.Clear();
        _chestContentsSilver.Clear();
        _trapsHidden = true;
        _openedChests.Clear();
        _fakeExits.Clear();
        OnChangeFloors();
        BetweenFloors = true;
    }

    protected void AddGaze(Actor Source, AOEShape Shape) => Gazes.Add(new(Source, Shape));
    protected void AddGaze(Actor Source, float Radius) => AddGaze(Source, new AOEShapeCircle(Radius));

    protected void AddLOS(Actor Source, float Range)
    {
        if (Config.AutoLOS)
            AddLOSFromTerrain(Source, Range);
        else
            Circles.Add((Source, Range));
    }

    private bool OpenGold => Config.GoldCoffer;
    private bool OpenSilver
    {
        get
        {
            // disabled
            if (!Config.SilverCoffer)
                return false;

            // sanity check
            if (World.Party.Player() is not { } player)
                return false;

            // explosive silver chests deal 70% max hp damage
            if (player.HPMP.CurHP <= player.HPMP.MaxHP * 0.7f)
                return false;

            // upgrade weapon if desired
            if (Palace.Progress.WeaponLevel + Palace.Progress.ArmorLevel < 198)
                return true;

            return Palace.DungeonId switch
            {
                DeepDungeonState.DungeonType.HOH or DeepDungeonState.DungeonType.EO => Palace.Floor >= 7, // magicite/demiclones start dropping on floor 7
                _ => false,
            };
        }
    }

    private bool OpenBronze => Config.BronzeCoffer;

    public override bool WantDrawExtra() => Config.EnableMinimap && !Palace.IsBossFloor;

    public sealed override string WindowName() => "BMR DD minimap###Zone module";

    public override void DrawExtra()
    {
        var player = World.Party.Player()!;

        var coords = CheckRoomCoords(player, out var coordDistance, out _);
        var targetRoom = new Minimap(Palace, player, DesiredRoom, Config, ComputeChestSpots(coords)).Draw();
        if (targetRoom >= 0)
        {
            DesiredRoom = targetRoom;
            _destinationSource = DestinationSource.User;
        }

        // 座標對不上時要說出來，否則使用者只會看到「寶箱一直是半透明的」而不知道為什麼
        if (coords == RoomCoordState.Mismatch)
            ImGui.TextColored(ColorUnknownText,
                string.Format(Loc.T("DD_CoordMismatch", "Coffer positions unavailable on this floor: the built-in room coordinates do not match this map (you are {0:f0}y from the centre of the room the game says you are in)."), coordDistance));

        DrawNavigationStatus(player);

        if (Config.ManualRoomWalk)
            DrawWalkControls(player, coords);

        ImGui.Text($"Kills: {Kills}");

        var maxPull = Config.MaxPull;

        ImGui.SetNextItemWidth(200);
        if (ImGui.DragInt(Loc.T("Max mobs to pull") + "###MaxPull", ref maxPull, 0.05f, 0, 15))
        {
            Config.MaxPull = maxPull;
            Config.Modified.Fire();
        }

        if (ImGui.Button(Loc.T("DD_ReloadObstacles", "Reload obstacles")))
        {
            _obstacles.Dispose();
            _obstacles = new(World);
        }

        if (player == null)
            return;

        var (entry, data) = _obstacles.Find(player.PosRot.XYZ());
        if (entry == null)
        {
            ImGui.SameLine();
            UIMisc.HelpMarker(() => "Obstacle map missing for floor!", Dalamud.Interface.FontAwesomeIcon.ExclamationTriangle);
        }

        if (data != null && data.PixelSize != 0.5f)
        {
            ImGui.SameLine();
            UIMisc.HelpMarker(() => $"Wrong resolution for map; should be 0.5, got {data.PixelSize}", Dalamud.Interface.FontAwesomeIcon.ExclamationTriangle);
        }

        if (ImGui.Button(Loc.T("DD_SetClosestTrapIgnored", "Set closest trap location as ignored")))
        {
            WPos? pos = null;
            var minDistanceSq = float.MaxValue;
            var lenCurrent = _trapsCurrentZone.Length;
            var countProblematic = ProblematicTrapLocations.Count;
            for (var i = 0; i < lenCurrent; ++i)
            {
                ref readonly var trap = ref _trapsCurrentZone[i];
                var isProblematic = false;
                for (var j = 0; j < countProblematic; ++j)
                {
                    if (trap == ProblematicTrapLocations[j])
                    {
                        isProblematic = true;
                        break;
                    }
                }

                if (isProblematic)
                    continue;

                var distanceSq = (trap - player.Position).LengthSq();

                if (distanceSq < minDistanceSq)
                {
                    minDistanceSq = distanceSq;
                    pos = trap;
                }
            }
            if (pos is WPos position)
            {
                pos = position.Rounded(0.1f);
                ProblematicTrapLocations.Add(position);
                IgnoreTraps.Add(position);
            }
        }
    }

    /// <summary>「不知道／不會動」用的灰字。不用警示色——這些多半不是錯誤，只是沒在動。</summary>
    private static readonly Vector4 ColorUnknownText = new(0.72f, 0.72f, 0.72f, 1f);

    #region 手動「走到目標房間」

    /// <summary>手動導航的狀態。</summary>
    private enum WalkState
    {
        Idle,
        /// <summary>已經叫 vnavmesh 算路徑，等結果。</summary>
        Pathfinding,
        /// <summary>路徑驗過了，交給 vnavmesh 在走。</summary>
        Moving
    }

    private WalkState _walkState;
    private string? _walkMessage;
    private Task<List<Vector3>>? _walkTask;

    /// <summary>
    /// 按下停止時遞增，讓還在背景算的那條路徑作廢。
    /// </summary>
    /// <remarks>
    /// 🔑 這就是為什麼刻意不用 <c>SimpleMove.PathfindAndMoveTo</c>：那個端點算完會自己開走，
    /// 呼叫端攔不到，於是「按了停止、幾秒後角色自己走起來」。改成自己持有那個 Task，
    /// 停止時只要對不上世代就整條丟掉，問題在結構上消失。
    /// </remarks>
    private int _walkGeneration;
    private int _walkTaskGeneration;

    /// <summary>用活的連通旗標算出來的房間走廊：路徑點只准落在這些房間裡。</summary>
    private HashSet<int> _walkCorridor = [];
    private int _walkTargetRoom = -1;

    private DateTime _walkStopEnforceUntil = DateTime.MinValue;
    private DateTime _walkNextEnforce = DateTime.MinValue;

    private bool WalkActive => _walkState != WalkState.Idle;

    public override void Update()
    {
        base.Update();

        UpdateStopWatchdog();

        switch (_walkState)
        {
            case WalkState.Pathfinding:
                PollWalkPathfind();
                break;
            case WalkState.Moving:
                // vnavmesh 沒有「到了」的回呼，路徑點走完就是到了（或被停掉了）
                if (!DeepDungeonNav.IsPathRunning())
                {
                    _walkState = WalkState.Idle;
                    _walkMessage = null;
                }
                break;
        }
    }

    /// <summary>
    /// 使用者按下停止之後，持續補送停止直到真的停了。
    /// </summary>
    /// <remarks>
    /// 送一次就好嗎？我們自己這條路徑是的（見 <see cref="_walkGeneration"/>）。
    /// 但別的外掛可能也在用 vnavmesh，而使用者按停止的意思就是「現在給我停下來」，
    /// 所以窗口內只要偵測到還在走就補送。窗口 3 秒，每 100ms 一次，
    /// 確認既沒在算也沒在走就提早收工，不會空轉滿 3 秒。
    /// </remarks>
    private void UpdateStopWatchdog()
    {
        if (_walkStopEnforceUntil == DateTime.MinValue)
            return;

        var now = World.CurrentTime;
        if (now >= _walkStopEnforceUntil)
        {
            _walkStopEnforceUntil = DateTime.MinValue;
            return;
        }

        if (now < _walkNextEnforce)
            return;
        _walkNextEnforce = now.AddMilliseconds(100);

        var running = DeepDungeonNav.IsPathRunning();
        if (running)
            DeepDungeonNav.Stop();
        else if (!DeepDungeonNav.IsSimpleMovePathfinding())
            _walkStopEnforceUntil = DateTime.MinValue;
    }

    private void PollWalkPathfind()
    {
        if (_walkTask is not { IsCompleted: true } task)
            return;
        _walkTask = null;

        // 這段期間使用者按過停止 ⇒ 整條作廢
        if (_walkTaskGeneration != _walkGeneration)
        {
            _walkState = WalkState.Idle;
            return;
        }

        if (task.IsFaulted)
        {
            // 讀一次 Exception 把它「觀察掉」，順便留下真正的原因；
            // 只判 IsFaulted 而不碰 Exception 會留下 unobserved task exception。
            Service.Log($"[DD nav] vnavmesh 算路徑失敗: {task.Exception?.InnerException?.Message ?? task.Exception?.Message}");
            SetWalkBlocked(Loc.T("DD_WalkNoRoute", "vnavmesh could not find a route to that room."));
            return;
        }

        if (task.IsCanceled || task.Result is not { Count: > 0 } path)
        {
            SetWalkBlocked(Loc.T("DD_WalkNoRoute", "vnavmesh could not find a route to that room."));
            return;
        }

        // 🔴 路徑點驗證——整個功能的安全核心，不可省略。
        if (ValidateWalkPath(path) is string reason)
        {
            SetWalkBlocked(reason);
            return;
        }

        if (!DeepDungeonNav.MoveAlong(path))
        {
            SetWalkBlocked(Loc.T("DD_WalkHandoffFailed", "vnavmesh refused the route."));
            return;
        }

        _walkState = WalkState.Moving;
        _walkMessage = null;
    }

    /// <summary>
    /// 🔴 檢查 vnavmesh 給的路徑有沒有跑出「房間走廊」。
    /// </summary>
    /// <remarks>
    /// <b>為什麼需要這一步</b>：vnavmesh 的導航網格快取鍵在同一組樓層的 10 層裡是相同的，
    /// 但門與牆是<b>逐層不同</b>的——也就是它手上那份網格很可能是<b>上一層</b>的。
    /// 拿那份網格算出來的路會大方地穿過這一層其實關著的門，而且完全不報錯。
    /// <para>
    /// 檢查方式：先用<b>活的連通旗標</b>做房間層 BFS 算出「從現在這間走到目標該經過哪些房間」，
    /// 再逐一驗證每個路徑點最近的房間中心是否落在那個集合裡。跨的房間越多，
    /// 舊網格繞錯路的機會越大，所以這個檢查在跨房版本比單跳版本更重要。
    /// </para>
    /// <para>📌 房間歸屬用「最近的房間中心」而不設距離上限：門口那種夾在兩間中間的點也要有歸屬。</para>
    /// </remarks>
    /// <returns>null＝通過；否則是要顯示給使用者看的拒絕原因。</returns>
    private string? ValidateWalkPath(List<Vector3> path)
    {
        var count = path.Count;
        for (var i = 0; i < count; ++i)
        {
            var w = path[i];
            var room = NearestRoom(new WPos(w.X, w.Z), float.MaxValue);
            if (room < 0 || !_walkCorridor.Contains(room))
                return string.Format(
                    Loc.T("DD_WalkPathLeavesCorridor", "Refusing to move: vnavmesh's route leaves the corridor of rooms that are actually connected on this floor (waypoint {0} of {1} lands in room {2}). Its navigation mesh is probably still the one from the previous floor."),
                    i + 1, count, room);
        }
        return null;
    }

    private void SetWalkBlocked(string message)
    {
        _walkState = WalkState.Idle;
        _walkMessage = message;
    }

    private void StartWalk(Actor player, int targetRoom)
    {
        var playerRoom = FindPlayerRoom(player);
        if (playerRoom < 0)
        {
            SetWalkBlocked(Loc.T("DD_WalkRoomUnknown", "The game has not reported which room you are in yet."));
            return;
        }

        // 房間層 BFS，只認活的連通旗標（也就是這一層真的打開的門）
        var rooms = new FloorPathfind(Palace.Rooms).Pathfind(playerRoom, targetRoom);
        if (rooms.Count == 0)
        {
            SetWalkBlocked(Loc.T("DD_BlockedNoPath", "No route to that room yet - the rooms in between have not been revealed."));
            return;
        }

        if (!TryGetRoomDestination(player, targetRoom, out var dest))
        {
            SetWalkBlocked(Loc.T("DD_WalkNoDestinationPoint", "The position of that room is unknown on this floor."));
            return;
        }

        var task = DeepDungeonNav.Pathfind(player.PosRot.XYZ(), dest);
        if (task == null)
        {
            SetWalkBlocked(Loc.T("DD_WalkNoRoute", "vnavmesh could not find a route to that room."));
            return;
        }

        _walkCorridor = [playerRoom, .. rooms];
        _walkTargetRoom = targetRoom;
        _walkTask = task;
        _walkTaskGeneration = _walkGeneration;
        _walkState = WalkState.Pathfinding;
        _walkMessage = null;
        Service.Logger.Information($"[DD] 手動導航：房間 {playerRoom} → {targetRoom}，走廊 [{string.Join(", ", _walkCorridor)}]");
    }

    /// <summary>
    /// 目標房間要走到哪一個世界座標。
    /// </summary>
    /// <remarks>
    /// 目標房有通道石而且實體已經在 <c>ObjectTable</c> 裡，就走到實體旁邊——
    /// 「下層解鎖後點傳送標記那一格」正是這個功能的主要使用情境。
    /// 🔴 <b>只是走過去，絕不自動互動</b>：要不要下樓由使用者自己點。
    /// </remarks>
    private bool TryGetRoomDestination(Actor player, int room, out Vector3 dest)
    {
        dest = default;

        if (Palace.Rooms[room].HasFlag(RoomFlags.Passage))
        {
            foreach (var a in World.Actors)
            {
                if (a.OID is not ((uint)OID.CairnPalace or (uint)OID.BeaconHoH or (uint)OID.PylonEO))
                    continue;
                if (_fakeExits.Contains(a.InstanceID))
                    continue;
                if (NearestRoom(a.Position, RoomCenterTolerance) != room)
                    continue;
                dest = a.PosRot.XYZ();
                return true;
            }
        }

        if (RoomCenters[room] is not WPos c)
            return false;

        // 房間中心只有 X／Z，高度要問 vnavmesh 的地板查詢。
        // ⚠️ 探測起點的 Y 要高於地形，否則會從地板底下往下找而落空。
        // 查不到就退回玩家目前的高度——深牢單層是平的，這個退路夠用，也比猜一個數字誠實。
        dest = DeepDungeonNav.TryPointOnFloor(new Vector3(c.X, player.PosRot.Y + 2f, c.Z), out var onFloor)
            ? onFloor
            : new Vector3(c.X, player.PosRot.Y, c.Z);
        return true;
    }

    private void RequestWalkStop()
    {
        ++_walkGeneration; // 還在背景算的路徑就此作廢
        _walkTask = null;
        _walkState = WalkState.Idle;
        _walkMessage = null;
        DeepDungeonNav.Stop();
        _walkStopEnforceUntil = World.CurrentTime.AddSeconds(3d);
        _walkNextEnforce = DateTime.MinValue;
    }

    private DateTime _vnavProbedAt = DateTime.MinValue;
    private bool _vnavInstalled;
    private bool _vnavMeshReady;

    /// <summary>
    /// 探測 vnavmesh 在不在、網格好了沒。
    /// </summary>
    /// <remarks>
    /// ⚠️ 刻意<b>不長期快取</b>——使用者可能中途裝上或停用外掛，沿用舊判定會顯示錯的原因。
    /// 但也不能每幀直接探：沒安裝時 <c>InvokeFunc</c> 是靠<b>擲例外</b>回報的，
    /// 每幀丟兩個例外只為了畫一行灰字並不划算。取 0.5 秒重探一次，
    /// 短到使用者感覺不出延遲，又不會變成每幀成本。
    /// </remarks>
    private void ProbeVnav()
    {
        var now = World.CurrentTime;
        if (_vnavProbedAt != DateTime.MinValue && (now - _vnavProbedAt).TotalSeconds < 0.5d)
            return;
        _vnavProbedAt = now;
        _vnavInstalled = DeepDungeonNav.IsInstalled();
        _vnavMeshReady = _vnavInstalled && DeepDungeonNav.IsMeshReady();
    }

    /// <summary>按鈕不能按的原因；null＝可以按。</summary>
    private string? GetWalkBlockedReason(Actor player, RoomCoordState coords)
    {
        if (DesiredRoom <= 0)
            return Loc.T("DD_WalkPickRoomFirst", "Pick a destination room on the map first.");

        // 🔑 三態分開講：要去裝外掛／只要等一下／裝好了但這一層不能信任，處置完全不同
        ProbeVnav();
        if (!_vnavInstalled)
            return Loc.T("DD_WalkNoVnav", "vnavmesh is not installed or not loaded, so walking is unavailable.");
        if (!_vnavMeshReady)
            return Loc.T("DD_WalkMeshNotReady", "vnavmesh's navigation mesh is not ready yet (still loading, or there is no mesh for this area).");
        if (coords != RoomCoordState.Ok)
            return Loc.T("DD_WalkCoordsUnverified", "The built-in room coordinates do not check out on this floor, so a route cannot be verified. Walking is disabled here.");

        var playerRoom = FindPlayerRoom(player);
        if (playerRoom < 0)
            return Loc.T("DD_WalkRoomUnknown", "The game has not reported which room you are in yet.");
        if (playerRoom == DesiredRoom)
            return Loc.T("DD_WalkAlreadyThere", "You are already in that room.");
        return null;
    }

    private void DrawWalkControls(Actor player, RoomCoordState coords)
    {
        // 已經在算／在走的時候不要再給「走過去」——重複按只會把自己的路徑重下一次
        var blocked = WalkActive ? null : GetWalkBlockedReason(player, coords);
        if (WalkActive)
        {
            // 只留停止鈕，狀態由下面那行說明
        }
        else if (blocked != null)
        {
            ImGui.TextColored(ColorUnknownText, blocked);
        }
        else
        {
            if (ImGui.Button(string.Format(Loc.T("DD_WalkToRoom", "Walk to room {0}"), DesiredRoom)))
                StartWalk(player, DesiredRoom);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(Loc.T("DD_WalkToRoomTooltip", "Walks there and stops on arrival. It does not open coffers, does not use the Cairn of Passage, and does not start the next leg by itself.\n\nThe route does not avoid mobs and does not avoid trap hints.\nIf you run NecroLens with automatic coffer opening, walking past a coffer will make both plugins reach for it."));
            ImGui.SameLine();
        }

        // 停止一律可按：Path.Stop 對「本來就沒在動」是安全的無操作，
        // 而按鈕變灰的那半秒恰好是最想反悔的半秒。
        if (ImGui.Button(Loc.T("DD_WalkStop", "Stop moving")))
            RequestWalkStop();
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(Loc.T("DD_WalkStopTooltip", "Asks vnavmesh to stop. Safe to press at any time. Note this also stops movement started by other plugins."));

        if (_walkMessage != null)
            ImGui.TextColored(ColorUnknownText, _walkMessage);
        else if (_walkState == WalkState.Pathfinding)
            ImGui.TextColored(ColorUnknownText, Loc.T("DD_WalkComputing", "Computing a route..."));
        else if (_walkState == WalkState.Moving)
            ImGui.Text(string.Format(Loc.T("DD_WalkMoving", "Walking to room {0}."), _walkTargetRoom));
    }

    #endregion

    /// <summary>
    /// 小地圖下方的狀態列：目標房間是哪一間、由誰決定的、以及<b>為什麼現在沒在動</b>。
    /// </summary>
    /// <remarks>
    /// 🔑 存在的理由是「不會動」這件事原本完全沒有回饋——使用者點了格子、角色不動，
    /// 而原因可能是模組沒開、BMR 的 AI 沒開、在戰鬥中、路還沒探到，
    /// 或者目標剛被「探索所有房間」無條件蓋掉。這幾種的處置完全不同，
    /// 合併成一句「沒在動」等於沒說。
    /// </remarks>
    private void DrawNavigationStatus(Actor player)
    {
        if (DesiredRoom <= 0)
        {
            ImGui.TextColored(ColorUnknownText, Loc.T("DD_NoDestination", "No destination room set (click a room on the map)."));
            return;
        }

        var source = _destinationSource switch
        {
            DestinationSource.FullClear => Loc.T("DD_DestFromFullClear", " (set by \"reveal all rooms\", which overrides your pick)"),
            DestinationSource.AutoPassage => Loc.T("DD_DestFromAutoPassage", " (set by \"navigate to Cairn of Passage\")"),
            _ => "",
        };
        ImGui.Text(string.Format(Loc.T("DD_Destination", "Destination: room {0}{1}"), DesiredRoom, source));

        // 為什麼沒在動。順序＝由外而內，先講最根本的那一個。
        string? blocked = null;
        if (!Config.Enable)
            blocked = Loc.T("DD_BlockedModuleOff", "The Auto-DeepDungeon module is switched off, so nothing will move.");
        else if (BetweenFloors)
            blocked = Loc.T("DD_BlockedBetweenFloors", "Changing floors.");
        else if (AI.AIManager.Instance?.Beh == null)
            blocked = Loc.T("DD_BlockedAIOff", "Navigation only happens while BMR's AI is running; it is currently off.");
        else if (player.InCombat && Config.MaxPull == 0)
            blocked = Loc.T("DD_BlockedInCombat", "Navigation is paused during combat (\"max mobs to pull\" is 0).");
        else if (_lastPathfindFailed)
            blocked = Loc.T("DD_BlockedNoPath", "No route to that room yet - the rooms in between have not been revealed.");

        if (blocked != null)
            ImGui.TextColored(ColorUnknownText, blocked);
    }

    private readonly List<PomanderID> AutoUsable = [
        PomanderID.Steel,
        PomanderID.Strength,
        PomanderID.Sight,
        PomanderID.Raising,
        PomanderID.Fortune,
        PomanderID.Concealment,
        PomanderID.Affluence,
        PomanderID.Frailty,
        PomanderID.ProtoSteel,
        PomanderID.ProtoStrength,
        PomanderID.ProtoSight,
        PomanderID.ProtoRaising,
        PomanderID.ProtoLethargy,
        PomanderID.ProtoFortune,
        PomanderID.ProtoAffluence
    ];

    private bool CanAutoUse(PomanderID p) => AutoUsable.Contains(p);

    private void IterAndExpire<T>(List<T> items, Func<T, bool> expire, Action<T> action, Action<T>? onRemove = null)
    {
        for (var i = items.Count - 1; i >= 0; --i)
        {
            var item = items[i];
            if (expire(item))
            {
                items.RemoveAt(i);
                onRemove?.Invoke(item);
            }
            else
                action(item);
        }
    }

    protected virtual void OnActorCreated(Actor c)
    {
        if (c.OID is (uint)OID.BeaconHoH or (uint)OID.BandedCofferIndicator)
            IgnoreTraps.Add(c.Position);
    }

    private DateTime CastFinishAt(Actor c) => World.FutureTime(c.CastInfo!.NPCRemainingTime);

    protected virtual void CalculateExtraHints(int playerSlot, Actor player, AIHints hints) { }

    public override void CalculateAIHints(int playerSlot, Actor player, AIHints hints)
    {
        if (!Config.Enable || Palace.IsBossFloor || BetweenFloors)
            return;

        bool canNavigate;

        if (Config.MaxPull == 0)
        {
            canNavigate = !player.InCombat;
        }
        else
        {
            var count = 0;
            var countTargets = hints.PotentialTargets.Count;
            for (var i = 0; i < countTargets; ++i)
            {
                var target = hints.PotentialTargets[i];
                if (target.Actor.AggroPlayer && !target.Actor.IsDeadOrDestroyed)
                {
                    ++count;
                    if (count >= Config.MaxPull)
                        break;
                }
            }
            canNavigate = count < Config.MaxPull;
        }

        var countWalls = Walls.Count;
        for (var i = 0; i < countWalls; ++i)
        {
            var wall = Walls[i];
            var w = wall.Wall;
            hints.AddForbiddenZone(ShapeDistance.Rect(w.Position, (wall.Rotated ? 90f : default).Degrees(), w.Depth, w.Depth, 20f));
        }

        if (canNavigate)
            HandleFloorPathfind(player, hints);

        DrawAOEs(playerSlot, player, hints);
        CalculateExtraHints(playerSlot, player, hints);

        var isStunned = IsPlayerTransformed(player) || player.Statuses.Any(s => s.ID is (uint)SID.Silence or (uint)SID.Pacification);
        var isOccupied = player.InCombat || isStunned;

        Actor? coffer = null;
        Actor? hoardLight = null;
        Actor? passage = null;
        List<Func<WPos, float>> revealedTraps = [];

        PomanderID? pomanderToUseHere = null;

        foreach (var a in World.Actors)
        {
            if (_chestContentsGold.TryGetValue(a.InstanceID, out var pid) && Palace.GetPomanderState(pid).Count == 3 && a.IsTargetable)
            {
                if (CanAutoUse(pid))
                    pomanderToUseHere ??= pid;
                continue;
            }

            if (_chestContentsSilver.ContainsKey(a.InstanceID) && Palace.Magicite.All(m => m > 0))
                // TODO use magicite/demiclone to prevent overcap
                continue;

            if (_openedChests.Contains(a.InstanceID) || _fakeExits.Contains(a.InstanceID))
                continue;

            var oid = a.OID;
            if (a.IsTargetable && (
                oid == (uint)OID.GoldCoffer && OpenGold ||
                oid == (uint)OID.SilverCoffer && OpenSilver && player.HPMP.CurHP > player.HPMP.MaxHP * 0.7f ||
                BronzeChestIDs.Contains(a.OID) && OpenBronze ||
                oid == (uint)OID.BandedCoffer
            ))
            {
                if ((coffer?.DistanceToHitbox(player) ?? float.MaxValue) > a.DistanceToHitbox(player))
                    coffer = a;
            }

            if (a.OID == (uint)OID.BandedCofferIndicator)
                hoardLight = a;

            if (a.OID is (uint)OID.CairnPalace or (uint)OID.BeaconHoH or (uint)OID.PylonEO && (passage?.DistanceToHitbox(player) ?? float.MaxValue) > a.DistanceToHitbox(player))
                passage = a;

            if (RevealedTrapOIDs.Contains(a.OID))
                revealedTraps.Add(ShapeDistance.Circle(a.Position, 2f));
        }

        var fullClear = false;
        if (Config.FullClear)
        {
            var unexplored = Array.FindIndex(Palace.Rooms, d => (byte)d > 0 && !d.HasFlag(RoomFlags.Revealed));
            if (unexplored > 0)
            {
                // ⚠️ 這是**無條件覆寫**，使用者剛剛在小地圖上點的目標會被蓋掉。
                //    以前這件事完全沒有回饋（點了沒反應），現在記下來由狀態列說明。
                if (DesiredRoom != unexplored)
                    _destinationSource = DestinationSource.FullClear;
                DesiredRoom = unexplored;
                fullClear = true;
            }
        }
        if (Config.TrapHints && _trapsHidden)
        {
            var countTraps = _trapsCurrentZone.Length;
            var traps = new List<Func<WPos, float>>(countTraps);

            for (var i = 0; i < countTraps; ++i)
            {
                var trap = _trapsCurrentZone[i];
                if (trap.InCircle(player.Position, 30f))
                {
                    var shouldIgnore = false;
                    var countIgnoreTraps = IgnoreTraps.Count;
                    for (var j = 0; j < countIgnoreTraps; ++j)
                    {
                        if (IgnoreTraps[j].AlmostEqual(trap, 1f))
                        {
                            shouldIgnore = true;
                            break;
                        }
                    }

                    if (!shouldIgnore)
                    {
                        var trapCircle = ShapeDistance.Circle(trap, 2f);
                        traps.Add(trapCircle);
                    }
                }
            }

            if (traps.Count != 0)
                hints.AddForbiddenZone(ShapeDistance.Union(traps));
        }

        if (coffer != null)
        {
            if (_lastChestContentsGold is PomanderID p)
            {
                _chestContentsGold[coffer.InstanceID] = p;
                _lastChestContentsGold = null;
                return;
            }

            if (_lastChestMagicite)
            {
                // TODO figure out why the system log args arent working
                _chestContentsSilver[coffer.InstanceID] = 1;
                _lastChestMagicite = false;
                return;
            }
        }

        if (Config.AllowPomander && !isStunned && pomanderToUseHere is PomanderID p2 && player.FindStatus((uint)SID.ItemPenalty) == null)
            hints.ActionsToExecute.Push(new ActionID(ActionType.Pomander, (uint)p2), null, ActionQueue.Priority.VeryHigh);

        Actor? wantCoffer = null;
        if (coffer is Actor t && !IsPlayerTransformed(player) && (Config.AutoMoveTreasure && canNavigate || player.DistanceToHitbox(t) < 3.5f))
            wantCoffer = t;

        if (!player.InCombat && Config.AutoPassage && Palace.PassageActive)
        {
            if (DesiredRoom == 0)
            {
                // 📌 這個只在使用者沒指定時才填，不會蓋掉使用者自己點的目標
                DesiredRoom = Array.FindIndex(Palace.Rooms, d => d.HasFlag(RoomFlags.Passage));
                if (DesiredRoom > 0)
                    _destinationSource = DestinationSource.AutoPassage;
            }

            if (passage is Actor c && !fullClear)
            {
                hints.GoalZones.Add(hints.GoalSingleTarget(c.Position, 2f, 0.5f));
                // give pathfinder a little help lmao
                hints.GoalZones.Add(hints.GoalSingleTarget(c.Position, 25f, 0.25f));
                if (player.DistanceToHitbox(c) < player.DistanceToHitbox(coffer) && !Config.OpenChestsFirst)
                    wantCoffer = null;
            }
        }

        if (wantCoffer is Actor xxx)
        {
            wantCoffer = xxx;
            hints.GoalZones.Add(hints.GoalSingleTarget(xxx.Position, 25f));
            hints.InteractWithTarget = coffer;
        }

        if (revealedTraps.Count > 0)
            hints.AddForbiddenZone(ShapeDistance.Union(revealedTraps));

        if (!IsPlayerTransformed(player) && canNavigate && Config.AutoMoveTreasure && hoardLight is Actor h && Palace.GetPomanderState(PomanderID.Intuition).Active)
            hints.GoalZones.Add(hints.GoalSingleTarget(h.Position, 2f, 10f));

        var shouldTargetMobs = Config.AutoClear switch
        {
            AutoDDConfig.ClearBehavior.Passage => !Palace.PassageActive,
            AutoDDConfig.ClearBehavior.Leveling => player.Level < LevelCap || !Palace.PassageActive,
            AutoDDConfig.ClearBehavior.All => true,
            _ => false
        };

        if (player.InCombat || World.Actors.Find(player.TargetID) is Actor t2 && !t2.IsAlly)
            return;

        Actor? bestTarget = null;

        void pickBetterTarget(Actor t)
        {
            if (player.DistanceToHitbox(t) < player.DistanceToHitbox(bestTarget))
                bestTarget = t;
        }

        var counttargets = hints.PotentialTargets.Count;
        for (var i = 0; i < counttargets; ++i)
        {
            var pp = hints.PotentialTargets[i];
            // enemy is petrified, any damage will kill
            if (pp.Actor.FindStatus((uint)SID.StoneCurse)?.ExpireAt > World.FutureTime(1.5d))
                pickBetterTarget(pp.Actor);

            // pomander of storms was used, enemy can't autoheal; any damage will kill
            else if (pp.Actor.FindStatus((uint)SID.AutoHealPenalty) != null && pp.Actor.HPMP.CurHP < 10)
                pickBetterTarget(pp.Actor);

            // if player does not have a target, prioritize everything so that AI picks one - skip dangerous enemies
            else if (shouldTargetMobs)
            {
                var hasDangerousStatus = false;
                var len = pp.Actor.Statuses.Length;
                ref var statuses = ref pp.Actor.Statuses;
                for (var j = 0; j < len; ++j)
                {
                    if (IsDangerousOutOfCombatStatus(statuses[j].ID))
                    {
                        hasDangerousStatus = true;
                        break;
                    }
                }

                if (!hasDangerousStatus)
                {
                    pickBetterTarget(pp.Actor);
                }
            }
        }
        hints.ForcedTarget = bestTarget;
    }

    private void DrawAOEs(int playerSlot, Actor player, AIHints hints)
    {
        IterAndExpire(HintDisabled, g => g.CastInfo == null, g =>
        {
            var count = hints.ForbiddenZones.Count;
            for (var i = 0; i < count; ++i)
            {
                var fz = hints.ForbiddenZones[i];
                if (fz.Source == g.InstanceID)
                {
                    hints.ForbiddenZones.Remove(fz);
                    break;
                }
            }
        });

        IterAndExpire(Gazes, g => g.Source.CastInfo == null, d =>
        {
            if (d.Shape.Check(player.Position, d.Source))
                hints.ForbiddenDirections.Add((player.AngleTo(d.Source), 45f.Degrees(), CastFinishAt(d.Source)));
        });

        IterAndExpire(Donuts, d => d.Source.CastInfo == null, d =>
        {
            hints.AddForbiddenZone(ShapeDistance.Donut(d.Source.Position.Quantized(), d.Inner, d.Outer), CastFinishAt(d.Source));
        });

        IterAndExpire(Circles, d => d.Source.CastInfo == null, d =>
        {
            hints.AddForbiddenZone(ShapeDistance.Circle(d.Source.Position.Quantized(), d.Radius), CastFinishAt(d.Source));

            // some enrages are way bigger than pathfinding map size (e.g. slime explosion is 60y)
            // in these cases, if the player is inside the aoe, add a goal zone telling it to GTFO as far as possible
            if (d.Radius >= 30)
            {
                var distToSource = (player.Position - d.Source.Position).Length();
                if (distToSource <= d.Radius)
                {
                    var desiredDistance = distToSource + 10f;
                    hints.GoalZones.Add(p =>
                    {
                        var dist = (p - d.Source.Position).Length();
                        return dist >= desiredDistance ? 100f : default;
                    });
                }
            }
        });

        IterAndExpire(Interrupts, d => d.CastInfo == null, d =>
        {
            if (hints.FindEnemy(d) is { } e)
                e.ShouldBeInterrupted = true;
        });

        IterAndExpire(Stuns, d => d.CastInfo == null, d =>
        {
            if (hints.FindEnemy(d) is { } e)
                e.ShouldBeStunned = true;
        });

        IterAndExpire(LOS, d => d.CastInfo == null, caster =>
        {
            if (!_losCache.TryGetValue(caster.InstanceID, out var dangermap))
                return;

            var origin = dangermap.Item1;
            var map = dangermap.Item2;

            hints.AddForbiddenZone(p =>
            {
                var offset = (p - origin) / map.PixelSize;
                return map[(int)offset.X, (int)offset.Z] ? -10 : 10;
            }, CastFinishAt(caster));
        }, d => _losCache.Remove(d.InstanceID));

        IterAndExpire(Voidzones, d => d.Source.IsDeadOrDestroyed, d =>
        {
            hints.AddForbiddenZone(d.Zone, d.Source.Position.Quantized(), d.Source.Rotation);
        });

        IterAndExpire(KnockbackZones, d => d.Source.CastInfo == null, kb =>
        {
            var castFinish = CastFinishAt(kb.Source);
            if (_playerImmunes[playerSlot].ImmuneAt(castFinish))
                return;

            hints.AddForbiddenZone(ShapeDistance.Circle(kb.Source.Position, kb.Radius), castFinish);
        });

        IterAndExpire(Spikes, t => t.Timeout <= World.CurrentTime, t =>
        {
            if (hints.FindEnemy(t.Actor) is { } enemy)
                enemy.Spikes = true;
        });
    }

    private static bool IsPlayerTransformed(Actor player) => player.Statuses.Any(Autorotation.RotationModuleManager.IsTransformStatus);
    private static bool IsDangerousOutOfCombatStatus(uint statusRaw) => statusRaw is (uint)SID.DamageUp or (uint)SID.DreadBeastAura or (uint)SID.PhysicalDamageUp;

    /// <summary>
    /// 本人目前在第幾間房。
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>不能用 <c>Palace.Party[0]</c>。</b>那個陣列是遊戲的隊伍名單順序，本人不保證在 0 號槽——
    /// 組隊進深牢時 0 號很可能是別人，於是整個樓層尋路會從<b>別人的房間</b>算起，
    /// 得到的方向指示是錯的而且完全不報錯（角色只是往奇怪的方向走）。
    /// 正解是拿 EntityId 比對，小地圖畫玩家標記時本來就是這樣找的。
    /// </remarks>
    /// <returns>房號 0..24；找不到本人時回 -1（剛換層、資料尚未同步等）。</returns>
    protected int FindPlayerRoom(Actor player)
    {
        var len = Palace.Party.Length;
        for (var i = 0; i < len; ++i)
        {
            ref readonly var p = ref Palace.Party[i];
            if (p.EntityId == player.InstanceID)
                return p.Room < DeepDungeonState.NumRooms ? p.Room : -1;
        }
        return -1;
    }

    /// <summary>房間座標校驗閘門的結果。</summary>
    protected enum RoomCoordState
    {
        /// <summary>還不知道——版面資料還沒載入，或遊戲還沒回報本人所在的房號。</summary>
        Unknown,
        /// <summary>本人的世界座標與遊戲回報的房號對得起來，可以拿座標做房間歸屬。</summary>
        Ok,
        /// <summary>對不起來：這一層的寫死座標不適用，任何依賴它的顯示都必須退化。</summary>
        Mismatch
    }

    /// <summary>
    /// 房間中心座標的容許誤差（單位 y）。房間間距實測約 55~58y，
    /// 半幅約 27.5y，取 35y 留一點餘裕又不至於跨到隔壁房。
    /// </summary>
    private const float RoomCenterTolerance = 35f;

    private bool _coordGateLogged;

    /// <summary>
    /// 🔴 <b>台服座標校驗閘門。</b>
    /// </summary>
    /// <remarks>
    /// <see cref="LoadedFloors"/> 那份房間座標是上游從國際服 dump 出來寫死的，
    /// <b>台服會不會一樣沒有人驗過</b>。假設不成立時的失敗形式是「寶箱點畫在錯的房間」
    /// ——看起來像功能正常運作，只是位置不對，比不畫還糟。
    /// <para>
    /// 做法：拿遊戲自己回報的「本人在第幾間房」對上那一間的中心座標。兩者對得上，
    /// 才准用這份座標做房間歸屬。除了距離，還要求<b>離本人最近的房間中心就是遊戲說的那一間</b>
    /// ——後者才是子格點位映射真正依賴的性質（座標整體平移時距離可能還過得了關，
    /// 但最近的會變成別間）。
    /// </para>
    /// <para>📌 校驗沒過不是錯誤，是「這一層退化成房級標示」，而且必須讓使用者看得見原因。</para>
    /// </remarks>
    protected RoomCoordState CheckRoomCoords(Actor player, out float distance, out int reportedRoom)
    {
        distance = -1f;
        reportedRoom = FindPlayerRoom(player);
        if (reportedRoom < 0 || RoomCenters[reportedRoom] is not WPos center)
            return RoomCoordState.Unknown;

        distance = (center - player.Position).Length();

        var nearest = NearestRoom(player.Position, float.MaxValue);
        var ok = distance <= RoomCenterTolerance && nearest == reportedRoom;

        if (!_coordGateLogged)
        {
            _coordGateLogged = true;
            // 要使用者回報才查得出台服座標對不對，所以走 Information（使用者的 LogLevel 是 2）。
            // 一層只印一次。
            Service.Logger.Information(
                $"[DD] 房間座標校驗：樓層 {Palace.Floor}、版面 {Palace.Progress.Tileset}、遊戲回報房號 {reportedRoom}、" +
                $"與該房中心距離 {distance:f1}y、最近的房間是 {nearest} ⇒ {(ok ? "通過" : "不通過")}");
        }

        return ok ? RoomCoordState.Ok : RoomCoordState.Mismatch;
    }

    /// <summary>
    /// 離某個世界座標最近的房間格子。
    /// </summary>
    /// <param name="maxDistance">超過這個距離就當作不屬於任何房間。</param>
    /// <returns>房號 0..24；沒有任何房間中心資料、或全都太遠時回 -1。</returns>
    protected int NearestRoom(WPos p, float maxDistance)
    {
        var best = -1;
        var bestSq = maxDistance == float.MaxValue ? float.MaxValue : maxDistance * maxDistance;
        for (var i = 0; i < RoomCenters.Length; ++i)
        {
            if (RoomCenters[i] is not WPos c)
                continue;
            var dsq = (c - p).LengthSq();
            if (dsq < bestSq)
            {
                bestSq = dsq;
                best = i;
            }
        }
        return best;
    }

    /// <summary>
    /// 房間格子的像素／碼換算。
    /// </summary>
    /// <remarks>
    /// ⚠️ 房間中心的實測間距在 55~68y 之間跳（<see cref="LoadedFloors"/> 的座標本來就
    /// <b>不在完美網格上</b>），所以這是個近似值，不是精確換算。間距偏大的房間裡，
    /// 靠邊的寶箱會被下面的夾邊處理擋在格子內——寧可貼邊也不要溢出到隔壁格，
    /// 溢出會讓人以為寶箱在別間房。
    /// </remarks>
    private const float CellPixelsPerYalm = Minimap.CellPixels / 55f;

    /// <summary>
    /// 把 <c>ObjectTable</c> 裡看得到的寶箱實體歸屬到房間，並換算成格內的像素位置。
    /// </summary>
    /// <returns>
    /// <b>座標校驗沒過時回 null</b>——寧可整層退化成「地圖說有、位置不明」，
    /// 也不要把寶箱畫在錯的房間。
    /// </returns>
    /// <remarks>
    /// 📌 遠處房間還沒串流進 <c>ObjectTable</c> 時這裡自然就數不到，
    /// 那些寶箱會留在上排的「還沒找到」摘要裡——這是預期行為，不是缺陷。
    /// </remarks>
    private List<ChestSpot>? ComputeChestSpots(RoomCoordState coords)
    {
        if (coords != RoomCoordState.Ok)
            return null;

        // 留邊，免得圖示被格子邊緣切掉
        const float limit = Minimap.CellHalfPixels - 11f;

        List<ChestSpot> res = [];
        foreach (var a in World.Actors)
        {
            if (_openedChests.Contains(a.InstanceID))
                continue;
            var slot = ChestSlotForOID(a.OID);
            if (slot < 0)
                continue;
            var room = NearestRoom(a.Position, RoomCenterTolerance);
            if (room < 0 || RoomCenters[room] is not WPos center)
                continue;

            // 世界座標 +X＝東＝畫面右、+Z＝南＝畫面下（拿 LoadedFloors 的相鄰房中心對照過：
            // 房號 +1 的中心 X 較大、房號 +5 的中心 Z 較大）
            var d = a.Position - center;
            var off = new Vector2(d.X * CellPixelsPerYalm, d.Z * CellPixelsPerYalm);
            off = Vector2.Clamp(off, new Vector2(-limit), new Vector2(limit));
            res.Add(new(room, slot, off));
        }
        return res;
    }

    /// <summary>寶箱實體的 OID 對應到哪一個型別槽；不是寶箱回 -1。</summary>
    /// <remarks>
    /// 📌 綁帶寶箱（藏寶庫）刻意不算——它不在遊戲的深牢寶箱清單裡，
    /// 混進來會讓「地圖說有幾個」與「看到幾個」對不起來。
    /// </remarks>
    private static int ChestSlotForOID(uint oid) =>
        BronzeChestIDs.Contains(oid) ? 0
        : oid == (uint)OID.SilverCoffer ? 1
        : oid == (uint)OID.GoldCoffer ? 2
        : -1;

    private void HandleFloorPathfind(Actor player, AIHints hints)
    {
        var playerRoom = FindPlayerRoom(player);
        // 找不到本人就不要瞎猜起點——寧可這一幀不給方向提示，也不要從錯的房間算路線
        if (playerRoom < 0)
            return;

        if (DesiredRoom == playerRoom || DesiredRoom == 0)
        {
            DesiredRoom = 0;
            _destinationSource = DestinationSource.User;
            _lastPathfindFailed = false;
            return;
        }

        var path = new FloorPathfind(Palace.Rooms).Pathfind(playerRoom, DesiredRoom);
        _lastPathfindFailed = path.Count == 0;
        if (path.Count == 0)
        {
            // expected while the connecting rooms haven't been explored/revealed yet - only log once per (from, to) pair to avoid spamming every frame
            if (_lastPathfindFailureLogged != (playerRoom, DesiredRoom))
            {
                _lastPathfindFailureLogged = (playerRoom, DesiredRoom);
                Service.Log($"uh-oh, no path from {playerRoom} to {DesiredRoom}");
            }
            return;
        }
        _lastPathfindFailureLogged = (-1, -1);
        var next = path[0];
        Direction d;
        if (next == playerRoom + 1)
            d = Direction.East;
        else if (next == playerRoom - 1)
            d = Direction.West;
        else if (next == playerRoom + 5)
            d = Direction.South;
        else if (next == playerRoom - 5)
            d = Direction.North;
        else
        {
            Service.Log($"pathfinding instructions are nonsense: {string.Join(", ", path)}");
            DesiredRoom = 0;
            return;
        }

        hints.GoalZones.Add(p =>
        {
            var pp = player.Position;
            var improvement = d switch
            {
                Direction.North => pp.Z - p.Z,
                Direction.South => p.Z - pp.Z,
                Direction.East => p.X - pp.X,
                Direction.West => pp.X - p.X,
                _ => 0,
            };
            return improvement > 10 ? 10 : 0;
        });
    }

    private void LoadWalls()
    {
        Service.Log($"loading walls for current floor...");
        Walls.Clear();
        var floorset = Palace.Floor / 10;
        var key = $"{(int)Palace.DungeonId}.{floorset + 1}";
        if (!LoadedFloors.Walls.TryGetValue(key, out var floor))
        {
            Service.Log($"unable to load floorset {key}");
            return;
        }
        Tileset<Wall> tileset;
        switch (Palace.Progress.Tileset)
        {
            case 0:
                tileset = floor.RoomsA;
                break;
            case 1:
                tileset = floor.RoomsB;
                break;
            case 2:
                Service.Log($"hall of fallacies - nothing to do");
                return;
            default:
                Service.Log($"unrecognized tileset number {Palace.Progress.Tileset}");
                return;
        }
        var len = Palace.Rooms.Length;
        for (var i = 0; i < len; ++i)
        {
            ref var room = ref Palace.Rooms[i];
            var roomdata = tileset[i];
            // 中心座標不管房間探索了沒都先存下來（索引＝房號）；牆壁仍然只對已知的房間算
            if (roomdata.Center != default)
                RoomCenters[i] = roomdata.Center.Position;

            if (room > 0)
            {
                if (roomdata.North != default && !room.HasFlag(RoomFlags.ConnectionN))
                    Walls.Add((roomdata.North, false));
                if (roomdata.South != default && !room.HasFlag(RoomFlags.ConnectionS))
                    Walls.Add((roomdata.South, false));
                if (roomdata.East != default && !room.HasFlag(RoomFlags.ConnectionE))
                    Walls.Add((roomdata.East, true));
                if (roomdata.West != default && !room.HasFlag(RoomFlags.ConnectionW))
                    Walls.Add((roomdata.West, true));
            }
        }
    }

    protected void AddLOSFromTerrain(Actor Source, float Range)
    {
        var (entry, data) = _obstacles.Find(Source.PosRot.XYZ());
        if (entry == null || data == null)
        {
            Service.Log($"no bitmap found for {Source}, not adding LOS hints");
            return;
        }

        var pixelRange = (int)(Range / data.PixelSize);
        var casterOff = Source.Position - entry.Origin;
        var casterCell = casterOff / data.PixelSize;
        var casterX = (int)casterCell.X;
        var casterZ = (int)casterCell.Z;

        var bm = new Bitmap(data.Width, data.Height, data.Color0, data.Color1, data.Resolution);
        for (var i = Math.Max(0, casterX - pixelRange); i <= Math.Min(data.Width, casterX + pixelRange); ++i)
        {
            for (var j = Math.Max(0, casterZ - pixelRange); j <= Math.Min(data.Height, casterZ + pixelRange); ++j)
            {
                var pt = new Vector2(i, j);
                var cc = new Vector2(casterX, casterZ);
                if (!IsBlocked(data, pt, cc, pixelRange))
                    bm[i, j] = true;
            }
        }

        _losCache[Source.InstanceID] = (entry.Origin, bm);
        LOS.Add(Source);
    }

    private static bool IsBlocked(Bitmap map, Vector2 point, Vector2 origin, float maxRange)
    {
        var dir = origin - point;
        var dist = dir.Length();
        if (dist >= maxRange)
            return true;

        dir /= dist;

        var ox = point.X;
        var oy = point.Y;
        var vx = dir.X;
        var vy = dir.Y;

        for (var i = 0; i < (int)dist; ++i)
        {
            if (map[(int)ox, (int)oy])
                return true;
            ox += vx;
            oy += vy;
        }

        return false;
    }
}
