using BossMod.Pathfinding;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Conditions;

using static FFXIVClientStructs.FFXIV.Client.Game.InstanceContent.InstanceContentDeepDungeon;

namespace BossMod.Global.DeepDungeon;

enum OID : uint
{
    CairnPalace = 0x1EA094,
    BeaconHoH = 0x1EA9A3,
    PylonEO = 0x1EB867,
    SilverCoffer = 0x1EA13D,
    GoldCoffer = 0x1EA13E,

    /// <summary>
    /// 埋藏的寶藏本體（<b>還埋著、看不見的那個點</b>）。
    /// </summary>
    /// <remarks>
    /// ⚠️ 名字取得容易誤導：它<b>不是</b>「魔陶器：感知寶藏照出來的光點」這種衍生特效物件，
    /// 而是寶藏埋藏處本身的事件物件。台服 <c>EObjName</c> 第 2007542 列的名稱是<b>空的</b>
    /// （＝沒有互動提示、不可選取），與 <see cref="BandedCoffer"/> 有名字（「埋藏的寶藏」）
    /// 形成對比；<c>EObj</c> 那一列的 <c>Data</c> 與 <c>EventHighAddition</c> 也都是 0，
    /// 而銅／銀／金寶箱與 <see cref="BandedCoffer"/> 都是 <c>Data=983600</c>（開箱事件）。
    /// <para>
    /// 交叉驗證：NecroLens 把同一個值命名為 <c>AccursedHoard</c>、PalacePal 則把 2007542
    /// 與 2007543 一起歸為 <c>EType.Hoard</c>。三份來源一致。
    /// </para>
    /// </remarks>
    BandedCofferIndicator = 0x1EA1F6,

    /// <summary>
    /// 已現形、可以互動取得的埋藏寶藏（台服 <c>EObjName</c> 2007543 ＝「埋藏的寶藏」）。
    /// </summary>
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
    /// <summary>
    /// 已現形（魔陶器：全景／踩過）的陷阱事件物件。
    /// </summary>
    /// <remarks>
    /// 📌 <c>0x1EBEDB</c>（2014939）是 NecroLens 有、這裡原本沒有的一個。台服 <c>EObjName</c>
    /// 第 2014939 列存在、名稱與其他六個陷阱一樣是空的（＝不可選取的裝飾/機關物件），
    /// 形狀一致所以補進來。
    /// ⚠️ 這是<b>離線查表</b>的結論，不是實機看過：假設不成立時的失敗方向是
    /// 「多一個永遠不會命中的比對」，不會誤標任何東西。
    /// </remarks>
    public static readonly HashSet<uint> RevealedTrapOIDs = [0x1EA08E, 0x1EA08F, 0x1EA090, 0x1EA091, 0x1EA092, 0x1EA9A0, 0x1EB864, 0x1EBEDB];

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

    /// <summary>
    /// 這一層的埋藏寶藏已經被挖出來了（系統訊息 7274「發現了埋藏的寶藏！」）。
    /// </summary>
    /// <remarks>
    /// 這只是<b>其中一個</b>停止標示的條件，不是唯一條件——挖出來之後實體通常也會離開
    /// <c>ObjectTable</c>，而且 <see cref="_openedChests"/> 也會收到 <c>EventOpenTreasure</c>。
    /// 三者任一成立就不再標，所以就算台服這條系統訊息沒有觸發（同檔 7248 就有前例），
    /// 失敗形式也只是「多標一下下」，不會標到錯的地方。
    /// </remarks>
    private bool _hoardFound;

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

    /// <summary>進深牢時請 WrathCombo 讓出自動循環的橋接（軟依賴，沒裝就靜默跳過）。</summary>
    private readonly WrathComboBridge _wrathCombo = new();

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

                // 🔴 換層時該歸零的那組狀態原本**只**掛在系統訊息 7248（傳送開始）上，
                //    而 7248 在台服不保證觸發（同一個成因已經害過房間座標沿用上一層）。
                //    後果最嚴重的是 _trapsHidden：用過一次「魔陶器：咒印解除／全景」之後
                //    它被設成 false，若沒有任何東西把它設回 true，
                //    **之後每一層的資料庫陷阱迴避都靜默關閉**——設定頁照樣打勾。
                //    這裡改用「遊戲回報的樓層變了」當驅動，與 7248 互為備援（誰先到都只跑一次）。
                if (_floorStateFor != Palace.Floor)
                {
                    _floorStateFor = Palace.Floor;
                    ResetFloorState();
                }
                // 🔴 判準是「載入的是不是這一層這個版面」，不是「Walls 是不是空的」。
                //    舊的 `if (Walls.Count == 0)` 只在第一次載入，之後完全依賴 ClearState() 去清空，
                //    而 ClearState 是由系統訊息 7248（傳送開始）驅動的 —— 那條在台服沒有觸發，
                //    於是整輪探索都沿用第一層的房間座標。
                //    實測後果：19:23 那場天之御柱 31~40，只有第 31 層（版面 1）通過座標校驗，
                //    32~39 層全部不通過、偏差 300~760 碼 —— 而同一組樓層的兩份鏡像版面
                //    （RoomsA／RoomsB）中心相距約 845 碼，正好對得上。
                //    寶箱點位與各房敵人數因此在九層裡有八層完全不顯示。
                if (_loadedLayout != (Palace.Floor, Palace.Progress.Tileset))
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

        // 🔴 離開深牢／卸載外掛都要把租約還回去。WrathComboBridge 內部已經把
        //    「WrathCombo 先卸載了」的情況包起來（IpcError 與非 IpcError 都接）。
        _wrathCombo.Dispose();

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
            // 「發現了埋藏的寶藏！」——台服 LogMessage 第 7274 列逐字查表確認。
            // NecroLens 做同一件事是比對訊息字串結尾，這裡直接用訊息 id，不受語系影響。
            case 7274:
                _hoardFound = true;
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

    /// <summary>
    /// 這一層已經跑過 <see cref="ResetFloorState"/> 的樓層號；255＝還沒跑過。
    /// </summary>
    /// <remarks>
    /// 🔴 沒有這個記號就分不出「換層了」與「同一層又揭開一間房」——
    /// <c>MapDataChanged</c> 每揭開一間房就會觸發一次。
    /// </remarks>
    private byte _floorStateFor = 255;

    /// <summary>
    /// <b>換一層</b>就該歸零的東西。
    /// </summary>
    /// <remarks>
    /// 從兩條路進來：系統訊息 7248（傳送開始，走 <see cref="ClearState"/>）與
    /// <c>MapDataChanged</c> 偵測到樓層變化。兩者互為備援，<see cref="_floorStateFor"/> 保證
    /// 同一層只跑一次。刻意<b>不</b>含房間座標／牆壁／AOE 清單——那幾樣是
    /// 「真的傳送」才該作廢的，而且座標與牆壁由 <see cref="LoadWalls"/> 自己負責重載。
    /// </remarks>
    private void ResetFloorState()
    {
        // 換層了，上一層算出來的路徑一律作廢；我們發起的移動也停掉
        // （角色會被傳走，繼續照舊路徑走是沒有意義而且可能有害的）
        if (WalkActive)
            RequestWalkStop();
        _walkMessage = null;
        _walkTargetRoom = -1;
        _walkCorridor.Clear();
        _stuckMessage = null;
        _stuckSince = default;

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
        _hoardFound = false;
        _openedChests.Clear();
        _fakeExits.Clear();
        OnChangeFloors();
    }

    private void ClearState()
    {
        ResetFloorState();

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
        ResetCoordGate();
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

        int[]? roomEnemies = null;
        if (Config.ShowRoomEnemies)
        {
            UpdateRoomEnemies(coords, World.CurrentTime);
            if (_roomEnemiesValid)
                roomEnemies = _roomEnemies;
        }

        var hoardSpots = ComputeHoardSpots(coords, out var hoardDetected);

        var targetRoom = new Minimap(Palace, player, DesiredRoom, Config, ComputeChestSpots(coords), roomEnemies, hoardSpots).Draw();
        if (targetRoom >= 0)
        {
            DesiredRoom = targetRoom;
            _destinationSource = DestinationSource.User;
            // 使用者重新指定目標＝那次放棄的說明已經沒有意義了
            _stuckMessage = null;
            _stuckSince = default;
        }

        // 🔴 偵測到寶藏但放不上小地圖時要講出來，而且要講清楚「世界標記還在」——
        //    否則使用者只會看到小地圖上什麼都沒有，而以為整個功能壞了。
        if (hoardDetected > 0 && hoardSpots == null)
            ImGui.TextColored(ColorUnknownText,
                Loc.T("DD_HoardPositionUnavailable", "Accursed Hoard: detected on this floor, but it cannot be placed on the minimap here (the built-in room coordinates do not match this map). The marker in the world is unaffected."));

        // 座標對不上時要說出來，否則使用者只會看到「寶箱一直是半透明的」而不知道為什麼
        if (coords == RoomCoordState.Mismatch)
            ImGui.TextColored(ColorUnknownText,
                string.Format(Loc.T("DD_CoordMismatch", "Coffer positions unavailable on this floor: the built-in room coordinates do not match this map (you are {0:f0}y from the centre of the room the game says you are in)."), coordDistance));

        // 🔴 誠實性：偵測範圍外的房間根本不在 ObjectTable 裡，「沒有數字」不等於「已清空」。
        //    這一行是常駐的，因為它說的是這個標示的根本限制，不是偶發狀況。
        if (Config.ShowRoomEnemies)
            ImGui.TextColored(ColorUnknownText, roomEnemies != null
                ? Loc.T("DD_EnemyCountCaveat", "Enemy counts only cover what is currently loaded around you - no number does not mean the room is clear.")
                : Loc.T("DD_EnemyCountUnavailable", "Enemies per room cannot be shown on this floor (room coordinates do not match)."));

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

    #region 卡住偵測

    /// <summary>認定「沒在動」的位移門檻（碼）。</summary>
    private const float StuckMoveThreshold = 1.5f;

    /// <summary>連續沒動這麼久（秒）就放棄本次目標。</summary>
    private const float StuckSeconds = 6f;

    private WPos _stuckLastPos;
    private DateTime _stuckSince;
    private string? _stuckMessage;

    /// <summary>
    /// 一直沒走到就放棄，並且說出來。
    /// </summary>
    /// <remarks>
    /// 🔑 這同時是「台服障礙物地圖可能對不上」的緩解措施：那份地圖是上游從國際服產的，
    /// 對不上時的表現是尋路把角色頂在牆上原地磨，<b>不會有任何錯誤訊息</b>，
    /// 使用者只看到角色卡住不動。與其猜地圖對不對，不如直接觀測「有沒有真的在前進」。
    /// <para>
    /// ⚠️ 只在「AI 開著、不在戰鬥、而且確實有目標房間」時計時：
    /// AI 沒開時角色本來就不會動，戰鬥中站著打也不是卡住 —— 把那兩種算成卡住是說謊。
    /// </para>
    /// </remarks>
    private void UpdateStuckDetection()
    {
        var player = World.Party.Player();
        var navigating = player != null
            && Config.Enable
            && !BetweenFloors
            && !Palace.IsBossFloor
            && DesiredRoom > 0
            && !player.InCombat
            && AI.AIManager.Instance?.Beh != null;

        if (!navigating)
        {
            _stuckSince = default;
            return;
        }

        var now = World.CurrentTime;
        if (_stuckSince == default || (player!.Position - _stuckLastPos).LengthSq() > StuckMoveThreshold * StuckMoveThreshold)
        {
            _stuckLastPos = player!.Position;
            _stuckSince = now;
            return;
        }

        if ((now - _stuckSince).TotalSeconds < StuckSeconds)
            return;

        var abandoned = DesiredRoom;
        _stuckSince = default;
        DesiredRoom = 0;
        _destinationSource = DestinationSource.User;
        _stuckMessage = string.Format(
            Loc.T("DD_StuckAbandoned", "Gave up on room {0}: no progress for {1:f0}s. The floor's obstacle map may not match this map; pick a destination again to retry."),
            abandoned, StuckSeconds);
        Service.Logger.Information($"[DD] 卡住偵測：{StuckSeconds} 秒內位移不足 {StuckMoveThreshold}y，放棄前往房間 {abandoned}（樓層 {Palace.Floor}、版面 {Palace.Progress.Tileset}）");
    }

    #endregion

    public override void Update()
    {
        base.Update();

        UpdateStopWatchdog();
        UpdateStuckDetection();

        // 純顯示，不影響任何決策。放在這裡是因為它必須每幀跑，
        // 而且要早於 Plugin.DrawUI 尾端的 Camera.DrawWorldPrimitives。
        // 📌 刻意不受 Config.Enable（＝自動化模組總開關）影響，與小地圖同一個立場：
        //    只想看標示、不想被自動移動的人才是這個功能的主要對象。
        DrawHoardOverlay();

        // 只有真的在深牢裡才壓住 WrathCombo；模組本身只在深牢區域存在，
        // 但過場／讀取中 DungeonId 會是 0，那時不該接管
        _wrathCombo.Update(Config.SuspendWrathCombo && Palace.DungeonId != DeepDungeonState.DungeonType.None, World.CurrentTime);

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
                if (NearestRoom(a.Position, RoomTolerance) != room)
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
                ImGui.SetTooltip(string.Format(Loc.T("DD_WalkToRoomTooltip", "Walks there and stops on arrival. It does not open coffers, does not use the {0}, and does not start the next leg by itself.\n\nThe route does not avoid mobs and does not avoid trap hints.\nIf you run NecroLens with automatic coffer opening, walking past a coffer will make both plugins reach for it."), PassageName));
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
            // 深牢內就用這一座傳送裝置的真名（每座不同），沒看過實體才退回通用詞
            DestinationSource.AutoPassage => string.Format(Loc.T("DD_DestFromAutoPassage", " (set by \"navigate to {0}\")"), PassageName),
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
        else if (TravelBlockedByHP(player))
            blocked = string.Format(Loc.T("DD_BlockedLowHP", "Travelling is paused below {0}% HP."), Config.StopTravelBelowHPPercent);
        else if (_lastPathfindFailed)
            blocked = Loc.T("DD_BlockedNoPath", "No route to that room yet - the rooms in between have not been revealed.");

        if (blocked != null)
            ImGui.TextColored(ColorUnknownText, blocked);

        // 🔑 「戰鬥中不趕路」與「戰鬥中不走位」是兩回事，而 MaxPull 的舊文案讀起來像後者。
        //    這一行是為了讓使用者不必猜：閃避走位永遠開著。
        if (Config.MaxPull == 0 && player.InCombat)
            ImGui.TextColored(ColorUnknownText, Loc.T("DD_CombatMovementNote", "(Dodging and combat positioning are always on - this only pauses travelling to the destination room.)"));

        if (_stuckMessage != null)
            ImGui.TextColored(ColorUnknownText, _stuckMessage);

        DrawKiteStatus();

        // 🔑 使用者回報過「BMR 鎖住我的設定」。租約持有期間 WrathCombo 側的設定確實會被鎖，
        //    那是租約機制的正常表現——但在此之前完全看不出來是誰鎖的、怎麼解。
        //    握著租約就直說，並寫出解除方式。
        if (_wrathCombo.Active)
            ImGui.TextColored(ColorUnknownText, Loc.T("DD_WrathLeaseHeld",
                "Holding WrathCombo's lease - its settings stay locked while this is active. Untick \"pause WrathCombo's auto-rotation\" above to hand control back immediately."));
    }

    /// <summary>
    /// 風箏對當前目標被停用時說出來。
    /// </summary>
    /// <remarks>
    /// 🔑 使用者的體感是「風箏壞了」，實際上是「這隻怪被判定為遠程平砍所以刻意不風箏」。
    /// 不寫出來的話兩者長得一模一樣，只能去翻 log。
    /// ⚠️ 只在資訊夠新時顯示（自動循環模組沒在跑時那個狀態會凍住，顯示過期狀態＝說謊）。
    /// </remarks>
    private void DrawKiteStatus()
    {
        if ((World.CurrentTime - Autorotation.xan.DeepDungeonAI.SuppressionAt).TotalSeconds > 2d)
            return;

        var text = Autorotation.xan.DeepDungeonAI.Suppression switch
        {
            Autorotation.xan.DeepDungeonAI.KiteSuppression.HardcodedList
                => Loc.T("DD_KiteOffList", "Kiting: off for this enemy (listed as not using melee autos)."),
            Autorotation.xan.DeepDungeonAI.KiteSuppression.ObservedRanged
                => Loc.T("DD_KiteOffObserved", "Kiting: off for this enemy (observed hitting you from out of melee range)."),
            _ => null,
        };
        if (text != null)
            ImGui.TextColored(ColorUnknownText, text);
    }

    #region 傳送裝置的名稱

    /// <summary>
    /// 目前這座深牢的傳送裝置叫什麼，以及那是哪一座的。
    /// </summary>
    /// <remarks>
    /// 🔴 <b>每一座深牢的傳送裝置官方名稱都不一樣</b>，寫死任何一個都會在別座變成錯的：
    /// 死者宮殿是「傳送石塚」、天之逆焰是「傳送燈籠」、厄運迷宮是「傳送裝置」
    /// （已逐一對台服 EObjName 表核對過 OID：0x1EA094／0x1EA9A3／0x1EB867）。
    /// <para>
    /// 🔑 名稱直接取自遊戲物件本身（<c>Actor.Name</c> ← <c>GameObject.NameString</c>），
    /// <b>不查 Lumina</b>：<c>Service.LuminaSheet</c> 寫死 <c>Language.English</c>，
    /// 在台服客戶端上要不到繁中字串。物件自己的名字就是遊戲當下顯示的那個，最可信。
    /// </para>
    /// <para>
    /// 📌 記住看過的名字（以深牢種類為鍵）：傳送裝置只有靠近時才會串流進 ObjectTable，
    /// 不快取的話大部分時間都只能顯示通用詞。換一座深牢就重新解析。
    /// </para>
    /// </remarks>
    private (DeepDungeonState.DungeonType Dungeon, string Name)? _passageName;

    private void RememberPassageName(Actor passage)
    {
        if (string.IsNullOrEmpty(passage.Name))
            return;
        if (_passageName is { } cached && cached.Dungeon == Palace.DungeonId && cached.Name == passage.Name)
            return;
        _passageName = (Palace.DungeonId, passage.Name);
    }

    /// <summary>
    /// 顯示用的傳送裝置名稱；還沒看過實體時退回通用詞。
    /// </summary>
    /// <remarks>⚠️ 通用詞刻意不是任何一座的專名，寧可籠統也不要在別座說錯。</remarks>
    protected string PassageName =>
        _passageName is { } n && n.Dungeon == Palace.DungeonId
            ? n.Name
            : Loc.T("DD_PassageGeneric", "the exit to the next floor");

    #endregion

    #region 保命藥水

    /// <summary>
    /// 保命藥水的候選，<b>依偏好順序</b>。
    /// </summary>
    /// <remarks>
    /// 📌 全部是 <c>ActionDefinitions</c> 已經註冊過的藥水（<c>RegisterPotion</c>），
    /// 所以冷卻、詠唱時間、動作鎖都有現成定義可查，不必自己算。
    /// 每個 ActionID 的 <c>ID</c> 若 ≥ 1000000 代表 HQ 版本。
    /// <para>
    /// 🔑 之所以要「一串」而不是「該座專屬那一瓶」：原本的寫法是
    /// <c>DungeonId switch { POTD =&gt; 頂級治療劑, HOH =&gt; 上級治療劑, EO =&gt; 聖級治療劑 }</c>，
    /// 於是人在天之御柱、包裡有 366 個 HQ 頂級治療劑，卻只會去找上級治療劑 ——
    /// 沒有就靜默什麼都不做。一般治療劑在深牢內是可以用的，沒有理由排除。
    /// </para>
    /// <para>⚠️ 順序是「效果由大到小」：頂級 &gt; 聖級 &gt; 上級，同一階 HQ 優先。</para>
    /// </remarks>
    private static readonly ActionID[] EmergencyPotions = [
        ActionDefinitions.IDPotionMax,     // HQ 頂級治療劑（死者宮殿專屬，但一般場合也能用）
        new(ActionType.Item, 13637),       // NQ 頂級治療劑
        ActionDefinitions.IDPotionHyper,   // HQ 聖級治療劑（厄運迷宮）
        new(ActionType.Item, 38956),
        ActionDefinitions.IDPotionSuper,   // HQ 上級治療劑（天之逆焰）
        new(ActionType.Item, 23167),
    ];

    private bool _loggedNoPotion;

    /// <summary>
    /// 血量過低時推一瓶藥水。
    /// </summary>
    /// <remarks>
    /// 門檻沿用原本寫死的 30%（與搬移前完全相同，不趁機改行為）。
    /// 冷卻用 <c>ActionDefinition.ReadyIn</c> 查，不自己記時間——藥品共用冷卻群組，
    /// 自己記一定會跟遊戲脫節。
    /// </remarks>
    private unsafe void UpdateEmergencyPotion(Actor player, AIHints hints)
    {
        if (player.HPMP.MaxHP == 0 || player.HPRatio > 0.3f)
            return;
        if (player.FindStatus((uint)SID.ItemPenalty) != null)
            return; // 藥品封印層

        var defs = ActionDefinitions.Instance;
        foreach (var aid in EmergencyPotions)
        {
            var def = defs[aid];
            if (def == null)
                continue;
            if (def.ReadyIn(World.Client.Cooldowns, World.Client.DutyActions) > 0f)
                continue; // 共用冷卻還沒好

            // ⚠️ 這是唯讀的原生查詢，不保存任何指標；換區途中會回 0，
            //    那時就當作沒有這瓶（安全退化，不會誤用）。
            var baseId = aid.ID % 1000000u;
            var hq = aid.ID >= 1000000u;
            if (FFXIVClientStructs.FFXIV.Client.Game.InventoryManager.Instance()->GetInventoryItemCount(baseId, hq, false, false) <= 0)
                continue;

            hints.ActionsToExecute.Push(aid, player, ActionQueue.Priority.VeryHigh);
            _loggedNoPotion = false;
            return;
        }

        if (!_loggedNoPotion)
        {
            _loggedNoPotion = true;
            Service.Logger.Information("[DD] 血量低於 30% 但背包裡沒有任何可用的治療劑（或全部還在冷卻），無法自動補血。");
        }
    }

    #endregion

    /// <summary>血量低於門檻就不趕路（門檻 0＝停用）。</summary>
    private bool TravelBlockedByHP(Actor player)
    {
        var pct = Config.StopTravelBelowHPPercent;
        if (pct <= 0 || player.HPMP.MaxHP == 0)
            return false;
        return player.HPMP.CurHP * 100f < player.HPMP.MaxHP * pct;
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
        if (!Config.Enable)
            return;

        // 🔴 保命藥水排在其他閘門**之前**。
        //    它原本掛在 DeepDungeonAI（自動循環模組）上，而那整條管線被
        //    `AIBehaviour` 的 `Preset = target.Target != null ? … : null` 關掉——沒有目標就不跑。
        //    踩到陷阱多半正是趕路、沒有目標的時候，也就是說它在最需要的那一刻保證不會觸發。
        //    實機 log 直證：整場 1091 行風箏診斷裡「沒有主要目標」出現 0 次
        //    ＝這個模組從來沒有在無目標時執行過。
        //    Boss 層與換層途中一樣會被打，所以也不受下面兩個條件限制。
        UpdateEmergencyPotion(player, hints);

        if (Palace.IsBossFloor || BetweenFloors)
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

        // 血量低於門檻就不趕路（只擋趕路，戰鬥走位與閃避不受影響）
        if (canNavigate && !TravelBlockedByHP(player))
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
                // ⚠️ 埋藏的寶藏以前這裡沒有接任何條件，銅銀金三個框全關也照樣處理
                oid == (uint)OID.BandedCoffer && Config.BandedCoffer
            ))
            {
                if ((coffer?.DistanceToHitbox(player) ?? float.MaxValue) > a.DistanceToHitbox(player))
                    coffer = a;
            }

            if (a.OID == (uint)OID.BandedCofferIndicator)
                hoardLight = a;

            if (a.OID is (uint)OID.CairnPalace or (uint)OID.BeaconHoH or (uint)OID.PylonEO && (passage?.DistanceToHitbox(player) ?? float.MaxValue) > a.DistanceToHitbox(player))
            {
                passage = a;
                RememberPassageName(a);
            }

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
                // 🔴 半徑要蓋得住尋路視窗，否則視窗角落的陷阱不會進 forbidden zone，
                //    表現是「大部分陷阱會閃、偶爾一個不閃」而不是整個功能壞掉。
                //    深牢用的是 AIHints.DefaultBounds ＝ ArenaBoundsSquare(30f)，
                //    也就是以玩家為中心、半邊長 30y 的方形；角落離中心 30·√2 ≒ 42.4y
                //    ⇒ 原本的 30y 查詢半徑蓋不到角落，取 45y。
                if (trap.InCircle(player.Position, 45f))
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

        // 「走過去」與「開起來」是兩件事，分開判斷。
        // ⚠️ 拆分前這裡是一條式子：`(AutoMoveTreasure && canNavigate) || 距離 < 3.5f`
        //    （`&&` 比 `||` 優先），而它同時決定移動目標與互動目標 ——
        //    於是關掉「自動移動至寶箱」之後，只要人走到寶箱 3.5y 內仍然會自動開箱，
        //    與那個標籤的字面意思不符。
        // 🔴 `InteractWithTarget` 本身也會讓 AI 走過去（AIBehaviour 把它當 forceDestination），
        //    所以「只開不走」不能無條件設它，必須限制在已經走到旁邊的情況，否則等於從後門
        //    把移動又加回來。
        // 📌 兩個新開關都預設 true，而且這樣拆**對預設值是逐案等價的**，不是「大致一樣」：
        //    拆分前唯一會多加 GoalZone 的情況是「已經走到 3.5y 內、但 wantMove 為 false」，
        //    而 `GoalSingleTarget(pos, 25f)` 是**平台函式**（25y 內一律回傳 weight、外面回 0，
        //    見 AIHints.GoalSingleTarget），人站在 3.5y 處時整個鄰域都在平台內、權重全部相同，
        //    對尋路沒有任何方向性影響 ⇒ 少加這一個 GoalZone 不改變行為。
        Actor? moveToCoffer = null;
        Actor? openCoffer = null;
        if (coffer is Actor t && !IsPlayerTransformed(player))
        {
            var wantMove = Config.AutoMoveTreasure && canNavigate;
            if (wantMove)
                moveToCoffer = t;
            if (Config.AutoOpenTreasure && (wantMove || player.DistanceToHitbox(t) < 3.5f))
                openCoffer = t;
        }

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
                // 通道石比寶箱近就先去通道石——連互動也一起讓開，否則 InteractWithTarget
                // 還是會把 AI 拉回寶箱那邊，等於這個「先去通道石」完全沒效果
                if (player.DistanceToHitbox(c) < player.DistanceToHitbox(coffer) && !Config.OpenChestsFirst)
                {
                    moveToCoffer = null;
                    openCoffer = null;
                }
            }
        }

        if (moveToCoffer is Actor moveTarget)
            hints.GoalZones.Add(hints.GoalSingleTarget(moveTarget.Position, 25f));

        if (openCoffer is Actor openTarget)
            hints.InteractWithTarget = openTarget;

        if (revealedTraps.Count > 0)
            hints.AddForbiddenZone(ShapeDistance.Union(revealedTraps));

        // 直感魔石照出來的埋藏寶藏光點：純移動，所以歸「自動移動至寶箱」管，
        // 但埋藏的寶藏整個關掉時也不必再走過去
        if (!IsPlayerTransformed(player) && canNavigate && Config.AutoMoveTreasure && Config.BandedCoffer && hoardLight is Actor h && Palace.GetPomanderState(PomanderID.Intuition).Active)
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
    /// 房間中心座標容許誤差的<b>下限</b>（單位 y）；實際值由 <see cref="RoomTolerance"/> 逐版面實算。
    /// </summary>
    /// <remarks>
    /// ⚠️ 原本這是唯一的常數，註解寫「房間間距實測約 55~58y」——<b>那個數字是錯的</b>。
    /// 2026-08-10 拿 <see cref="LoadedFloors"/> 全部版面實算最近鄰房距，結果是 31.5~87.4y，
    /// 而 35y 的歸屬半徑在厄運迷宮有 25.2% 的格子不夠用（最大需要 40.2y），
    /// 死者宮殿 0.8%、天之逆焰 0%。⇒ 固定 35y 在厄運迷宮會讓四分之一的寶箱／敵人
    /// <b>歸屬不到任何房間而被靜默丟掉</b>。
    /// <para>
    /// 保留 35f 當下限是刻意的：容許誤差只會變大不會變小，所以今天能正確歸屬的東西
    /// 明天一定還能，這個改動不可能讓既有行為退步。
    /// </para>
    /// </remarks>
    private const float RoomCenterToleranceFloor = 35f;

    /// <summary>容許誤差的上限，避免只載到兩三間房時算出荒謬的大值。</summary>
    private const float RoomCenterToleranceCap = 80f;

    /// <summary>
    /// 目前這個版面的房間歸屬容許誤差（單位 y），由 <see cref="ApplyFace"/> 實算。
    /// </summary>
    private float RoomTolerance = RoomCenterToleranceFloor;

    #region 鏡像版面自我校準

    /// <summary>本層那一組樓層的兩份鏡像版面；null＝這一層沒有可用的座標資料。</summary>
    private Tileset<Wall>? _faceA, _faceB;

    /// <summary>目前套用的是哪一面：0＝RoomsA、1＝RoomsB、-1＝沒有版面資料。</summary>
    private int _activeFace = -1;

    /// <summary>連續幾次評分都顯示「另一面才對」；到門檻才真的換面（遲滯）。</summary>
    private int _faceSwitchStreak;

    /// <summary>
    /// 換面需要連續吻合幾次。
    /// </summary>
    /// <remarks>
    /// 🔴 <b>不能只看一幀。</b>剛換層那一瞬間角色還在傳送途中，位置是上一層的殘值或中間態，
    /// 拿它去選面會<b>主動選錯</b>。兩份鏡像版面中心相距約 845y，正常遊玩時
    /// 不可能連續 8 次都落在錯的那一面附近，所以這個遲滯足以濾掉傳送中的雜訊。
    /// </remarks>
    private const int FaceSwitchConfirmFrames = 8;

    /// <summary>上一次記錄過的閘門狀態；null＝還沒記過。用來做「狀態變化才記一行」。</summary>
    private RoomCoordState? _coordGateLoggedState;

    private void ResetCoordGate()
    {
        _faceA = _faceB = null;
        _activeFace = -1;
        _faceSwitchStreak = 0;
        _coordGateLoggedState = null;
        RoomTolerance = RoomCenterToleranceFloor;
        Array.Clear(_centerFitted);
    }

    /// <summary>對某一面評分：本人離「遊戲回報的那間房」多遠、離本人最近的是哪一間。</summary>
    /// <returns>該面沒有這間房的座標時回 <c>null</c>。</returns>
    private (float Distance, int Nearest, bool Ok)? ScoreFace(Tileset<Wall> face, WPos pos, int reportedRoom)
    {
        var reported = face[reportedRoom].Center;
        if (reported == default)
            return null;

        var distance = (reported.Position - pos).Length();

        var nearest = -1;
        var bestSq = float.MaxValue;
        for (var i = 0; i < DeepDungeonState.NumRooms; ++i)
        {
            var c = face[i].Center;
            if (c == default)
                continue;
            var dsq = (c.Position - pos).LengthSq();
            if (dsq < bestSq)
            {
                bestSq = dsq;
                nearest = i;
            }
        }

        return (distance, nearest, distance <= RoomTolerance && nearest == reportedRoom);
    }

    #endregion

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
        if (reportedRoom < 0 || _activeFace < 0)
            return RoomCoordState.Unknown;

        var pos = player.Position;
        var active = _activeFace == 0 ? _faceA : _faceB;
        var score = active != null ? ScoreFace(active, pos, reportedRoom) : null;

        // ── 鏡像面自我校準 ────────────────────────────────────────────────
        // 目前這一面對不上時，看看另一面對不對得上。兩面中心相距約 845y ⇒ 只有一面可能吻合，
        // 巧合吻合實務上不可能發生。連續 FaceSwitchConfirmFrames 次都指向另一面才真的換，
        // 免得被傳送途中的座標騙走。
        if (score is not { Ok: true } && _faceA != null && _faceB != null)
        {
            var otherIdx = 1 - _activeFace;
            var other = otherIdx == 0 ? _faceA : _faceB;
            var otherScore = ScoreFace(other, pos, reportedRoom);
            if (otherScore is { Ok: true })
            {
                if (++_faceSwitchStreak >= FaceSwitchConfirmFrames)
                {
                    Service.Logger.Information(
                        $"[DD] 鏡像版面自我校準：改用版面 {(otherIdx == 0 ? "A" : "B")}（遊戲回報的 Progress.Tileset 是 {Palace.Progress.Tileset}）。" +
                        $"樓層 {Palace.Floor}、回報房號 {reportedRoom}、本人 ({pos.X:f1}, {pos.Z:f1})、" +
                        $"原本那面距離 {(score?.Distance ?? -1f):f1}y/最近房 {(score?.Nearest ?? -1)}、" +
                        $"改用那面距離 {otherScore.Value.Distance:f1}y/最近房 {otherScore.Value.Nearest}");
                    ApplyFace(otherIdx);
                    _faceSwitchStreak = 0;
                    // ⚠️ ApplyFace 會重算容許誤差，所以要用新的那份重新評分，不能沿用上面那次
                    score = ScoreFace(other, pos, reportedRoom);
                }
            }
            else
            {
                _faceSwitchStreak = 0;
            }
        }
        else
        {
            _faceSwitchStreak = 0;
        }

        if (score is not { } s)
            return RoomCoordState.Unknown;

        distance = s.Distance;
        var state = s.Ok ? RoomCoordState.Ok : RoomCoordState.Mismatch;

        // 🔴 診斷改成「狀態變化才記一行」而不是「一層記一次」。
        //    一層記一次記到的必然是樓層載入後的**第一幀**，而那一幀角色還在傳送中——
        //    量到的是一個與回報房號無關的固定位置（實機 log 裡「最近的房間」恆為 3 或 10
        //    就是這個特徵），於是把「量測時機不對」誤報成「座標對不上」。
        //    改成翻轉才記，下一次實跑就分得出「站定之後其實會通過」與「站定也不過」。
        if (_coordGateLoggedState != state)
        {
            _coordGateLoggedState = state;
            // 要使用者回報才查得出台服座標對不對，所以走 Information（使用者的 LogLevel 是 2）。
            Service.Logger.Information(
                $"[DD] 房間座標校驗{(state == RoomCoordState.Ok ? "通過" : "不通過")}：樓層 {Palace.Floor}、" +
                $"Progress.Tileset {Palace.Progress.Tileset}、實際採用版面 {(_activeFace == 0 ? "A" : "B")}、" +
                $"遊戲回報房號 {reportedRoom}、本人 ({pos.X:f1}, {pos.Z:f1})、與該房中心距離 {s.Distance:f1}y、" +
                $"最近的房間是 {s.Nearest}、容許誤差 {RoomTolerance:f1}y");
        }

        return state;
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
            var room = NearestRoom(a.Position, RoomTolerance);
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

    #region 埋藏的寶藏

    /// <summary>
    /// 這一幀看得到的埋藏寶藏實體。<b>每次使用前都要先呼叫 <see cref="CollectHoardActors"/> 重填。</b>
    /// </summary>
    /// <remarks>
    /// 用一份重複使用的清單而不是每次配置新的：世界疊加層走的是每幀路徑，
    /// 而深牢一層最多也只有一個埋藏寶藏，為它每幀配置一個 <c>List</c> 是白花的。
    /// 📌 裡面放的是 BMR 自己的 <see cref="Actor"/> 鏡像物件（純受管資料），
    /// <b>不是原生指標</b>，所以「不跨幀保存原生指標」那條紅線在這裡不適用；
    /// 即使如此也只在同一次呼叫內用完就丟。
    /// </remarks>
    private readonly List<Actor> _hoardActors = [];

    /// <summary>
    /// 隱藏點與已現形寶箱視為「同一個寶藏」的距離（碼）。
    /// </summary>
    /// <remarks>
    /// 兩者並存時只畫一個，否則同一個位置會疊出兩圈——那看起來像是有兩個寶藏。
    /// 取 5y 是寬鬆值：同一層不會有第二個埋藏寶藏，所以寧可多合併也不要漏合併。
    /// </remarks>
    private const float HoardDedupeRangeSq = 5f * 5f;

    /// <summary>
    /// 重新收集目前該標示的埋藏寶藏實體到 <see cref="_hoardActors"/>。
    /// </summary>
    /// <remarks>
    /// 停止標示的條件有三個，任一成立就不收：
    /// <list type="number">
    /// <item>實體已經不在 <c>ObjectTable</c> 裡（挖走之後的常態）；</item>
    /// <item><see cref="_openedChests"/> 收到過這個實體的 <c>EventOpenTreasure</c>；</item>
    /// <item>這一層已經跳過「發現了埋藏的寶藏！」系統訊息（<see cref="_hoardFound"/>）。</item>
    /// </list>
    /// 🔴 <b>刻意不做「沒用魔陶器：感知寶藏就不標」這種閘門。</b>本函式的唯一資料來源是實體
    /// 在不在 <c>ObjectTable</c> 裡：在就標、不在就什麼都不畫。因此「沒照出來的時候實體到底
    /// 會不會出現在 <c>ObjectTable</c>」這個離線證不了的問題，最壞情況只是<b>這個功能不顯示</b>，
    /// 不會把標記畫到錯的地方。
    /// </remarks>
    private void CollectHoardActors()
    {
        _hoardActors.Clear();
        if (_hoardFound)
            return;

        foreach (var a in World.Actors)
        {
            if (a.OID is not ((uint)OID.BandedCofferIndicator or (uint)OID.BandedCoffer))
                continue;
            if (_openedChests.Contains(a.InstanceID))
                continue;
            _hoardActors.Add(a);
        }

        // 去重：已現形的寶箱優先，旁邊那個隱藏點就不用再畫了。
        for (var i = _hoardActors.Count - 1; i >= 0; --i)
        {
            if (_hoardActors[i].OID != (uint)OID.BandedCofferIndicator)
                continue;
            for (var j = 0; j < _hoardActors.Count; ++j)
            {
                if (j == i || _hoardActors[j].OID != (uint)OID.BandedCoffer)
                    continue;
                if ((_hoardActors[j].Position - _hoardActors[i].Position).LengthSq() <= HoardDedupeRangeSq)
                {
                    _hoardActors.RemoveAt(i);
                    break;
                }
            }
        }
    }

    /// <summary>
    /// 把埋藏寶藏歸屬到房間並換算成格內像素位置，規則與 <see cref="ComputeChestSpots"/> 完全相同。
    /// </summary>
    /// <param name="detected">
    /// 這一幀實際偵測到幾個埋藏寶藏實體，<b>與能不能定位到房間無關</b>。
    /// 用來區分「這層沒有／還沒走到」與「有，但這層的座標對不上所以放不上小地圖」。
    /// </param>
    /// <returns>座標校驗沒過或功能關閉時回 <c>null</c>——寧可不畫，也不要畫在錯的房間。</returns>
    private List<HoardSpot>? ComputeHoardSpots(RoomCoordState coords, out int detected)
    {
        detected = 0;
        if (!Config.ShowAccursedHoard)
            return null;

        CollectHoardActors();
        detected = _hoardActors.Count;

        if (coords != RoomCoordState.Ok)
            return null;

        // 留邊，免得圖示被格子邊緣切掉（與寶箱同一個常數）
        const float limit = Minimap.CellHalfPixels - 11f;

        List<HoardSpot> res = [];
        for (var i = 0; i < _hoardActors.Count; ++i)
        {
            var a = _hoardActors[i];
            var room = NearestRoom(a.Position, RoomTolerance);
            if (room < 0 || RoomCenters[room] is not WPos center)
                continue;

            var d = a.Position - center;
            var off = new Vector2(d.X * CellPixelsPerYalm, d.Z * CellPixelsPerYalm);
            off = Vector2.Clamp(off, new Vector2(-limit), new Vector2(limit));
            res.Add(new(room, off, a.OID == (uint)OID.BandedCoffer));
        }
        return res;
    }

    // ── 世界疊加層 ────────────────────────────────────────────────────────
    // NecroLens 的圈半徑是 2y（它的註解寫「Make Hoards bigger」，一般寶箱是 1y），這裡沿用，
    // 這樣兩個外掛同時開著也不會出現兩種尺寸的圈。
    private const float HoardMarkerRadius = 2f;
    private const float HoardMarkerThickness = 2f;
    private const float HoardOutlineExtra = 2f;

    /// <summary>地面圈之外再往上拉一小段的立柱高度（碼）。</summary>
    /// <remarks>
    /// 埋藏寶藏是隱形的，光有貼地的圈在俯角小的時候會被壓成一條線、遠一點就看不見。
    /// 立柱給這個標記一個明確的「上」，也讓它在人還沒走近時就找得到。
    /// </remarks>
    private const float HoardMarkerStem = 1.6f;

    // 埋藏寶藏的標示色。刻意不用 Colors.* 的語意色（那些是使用者可調的危險／安全色，
    // 借來當「這裡有東西」會在使用者改色之後變成謊話），也刻意選成與 PalacePal 的
    // 埋藏寶藏預設色（青色）同一系，讓兩邊看起來是同一件事。
    // ⚠️ ImGui 的 uint 顏色是 ABGR：這個值是 R=0x30 G=0xE0 B=0xF0。
    private const uint ColorHoard = 0xFFF0E030u;

    /// <summary>
    /// 在世界上畫出埋藏寶藏的位置。
    /// </summary>
    /// <remarks>
    /// 從 <see cref="Update"/> 呼叫：<c>Plugin.DrawUI</c> 的順序是
    /// <c>Camera.Update</c> →（本函式所在的）<c>ZoneModule.Update</c> → … →
    /// <c>Camera.DrawWorldPrimitives</c>，所以矩陣是當幀的、線也一定會被 flush 出去。
    /// <para>
    /// 📌 <c>CalculateAIHints</c> 也在同一個窗口內，但那條路徑在<b>有 boss 模組正在進行中的時候
    /// 整段被跳過</b>（見 <c>AIHintsBuilder.Update</c>），拿它當顯示用的繪製點會多一個
    /// 與顯示無關的失效條件。
    /// </para>
    /// <para>
    /// ⚠️ 隱藏 UI／過場時不畫：<c>DrawWorldPrimitives</c> 本身沒有這個閘門，
    /// 而 BMR 既有的世界繪製都是從 <c>WindowSystem.Draw</c> 底下發出的（那裡有閘門）。
    /// 不自己擋的話，這會是第一個在過場動畫上畫線的東西。
    /// </para>
    /// </remarks>
    private void DrawHoardOverlay()
    {
        if (!Config.ShowAccursedHoard || Palace.IsBossFloor || BetweenFloors)
            return;

        if (Service.GameGui.GameUiHidden
            || Service.Condition[ConditionFlag.OccupiedInCutSceneEvent]
            || Service.Condition[ConditionFlag.WatchingCutscene]
            || Service.Condition[ConditionFlag.WatchingCutscene78])
            return;

        if (Camera.Instance is not { } camera)
            return;

        CollectHoardActors();
        for (var i = 0; i < _hoardActors.Count; ++i)
        {
            var p = _hoardActors[i].PosRot;
            DrawHoardMarker(camera, new Vector3(p.X, p.Y, p.Z));
        }
    }

    /// <summary>
    /// 一個埋藏寶藏的地面標記：圈 ＋ 中心叉 ＋ 立柱，全部先畫深色外框再畫本體。
    /// </summary>
    /// <remarks>
    /// 外框做法與 <c>UIRotationWindow.DrawPathSegment</c> 相同（先粗深色、再細亮色）——
    /// 疊加層底下是 3D 場景，沒有外框的細線在亮地板上會整條消失。
    /// 全部是線，不畫任何半透明色塊，維持與 NecroLens 一致的「不疊顏色」語彙。
    /// </remarks>
    private static void DrawHoardMarker(Camera camera, Vector3 center)
    {
        const float outline = HoardMarkerThickness + HoardOutlineExtra;

        camera.DrawWorldCircle(center, HoardMarkerRadius, Colors.Shadows, outline);
        camera.DrawWorldCircle(center, HoardMarkerRadius, ColorHoard, HoardMarkerThickness);

        // 中心的叉：圈只說「這附近」，交叉點才說「就是這裡挖」。
        const float d = HoardMarkerRadius * 0.5f;
        var c1 = center + new Vector3(-d, 0f, -d);
        var c2 = center + new Vector3(d, 0f, d);
        var c3 = center + new Vector3(-d, 0f, d);
        var c4 = center + new Vector3(d, 0f, -d);
        var top = center + new Vector3(0f, HoardMarkerStem, 0f);

        camera.DrawWorldLine(c1, c2, Colors.Shadows, outline);
        camera.DrawWorldLine(c3, c4, Colors.Shadows, outline);
        camera.DrawWorldLine(center, top, Colors.Shadows, outline);
        camera.DrawWorldLine(c1, c2, ColorHoard, HoardMarkerThickness);
        camera.DrawWorldLine(c3, c4, ColorHoard, HoardMarkerThickness);
        camera.DrawWorldLine(center, top, ColorHoard, HoardMarkerThickness);
    }

    #endregion

    #region 房間內的敵人數

    /// <summary>
    /// 每個房間目前偵測到幾隻活著的敵人。
    /// </summary>
    /// <remarks>
    /// 🔴 <b>「0」的意思是「現在偵測不到」，不是「已經清空」。</b><c>ObjectTable</c> 只含串流進來的實體，
    /// 遠處房間的怪根本不在裡面。UI 因此只畫正向標記（有偵測到才寫數字），
    /// 並常駐一行說明——絕不能讓使用者把空白讀成清完了。
    /// </remarks>
    private readonly int[] _roomEnemies = new int[DeepDungeonState.NumRooms];

    /// <summary>上面那份資料現在可不可信（座標校驗過了、而且掃過至少一次）。</summary>
    private bool _roomEnemiesValid;

    private DateTime _roomEnemiesSweptAt;

    /// <summary>
    /// 重新統計每個房間的敵人數。
    /// </summary>
    /// <remarks>
    /// ⚠️ 節流到 0.4 秒一次。小地圖是每幀重畫的，但敵人數不需要每幀重算——
    /// 全表掃描放在每幀路徑上是白花成本（<c>World.Actors</c> 動輒上百個）。
    /// 📌 判定條件對齊 <c>AIHintsBuilder.FillEnemies</c>（可選取、非友方、沒死），
    /// 另外限定 <c>ActorType.Enemy</c> 以排掉寵物、陸行鳥、事件物件與寶箱。
    /// </remarks>
    private void UpdateRoomEnemies(RoomCoordState coords, DateTime now)
    {
        if (coords != RoomCoordState.Ok)
        {
            _roomEnemiesValid = false;
            return;
        }

        if (_roomEnemiesValid && (now - _roomEnemiesSweptAt).TotalSeconds < 0.4d)
            return;
        _roomEnemiesSweptAt = now;

        Array.Clear(_roomEnemies);
        foreach (var a in World.Actors)
        {
            if (a.Type != ActorType.Enemy || a.IsAlly || !a.IsTargetable || a.IsDeadOrDestroyed)
                continue;
            var room = NearestRoom(a.Position, RoomTolerance);
            if (room < 0)
                continue;
            ++_roomEnemies[room];
        }
        _roomEnemiesValid = true;
    }

    #endregion

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

        // 🔑 趕路場的權重原本固定是 10，壓過場上所有戰鬥走位（風箏 0.05、閃避偏好 0.5…）。
        //    MaxPull > 1 時這會變成「被兩隻怪咬著仍然全速趕路」——使用者設定的是
        //    「還能再拉幾隻」，不是「戰鬥中也照跑」。戰鬥中把權重降到 0.5，
        //    讓它退成一個溫和的方向偏好，戰鬥走位重新拿回主導權。
        //    ⚠️ MaxPull == 0 的人完全不受影響：那種設定下戰鬥中根本不會走到這裡。
        var travelWeight = player.InCombat ? 0.5f : 10f;
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
            return improvement > 10f ? travelWeight : 0f;
        });
    }

    /// <summary>
    /// <see cref="RoomCenters"/>／<see cref="Walls"/> 目前載入的是哪一層、哪個版面。
    /// </summary>
    /// <remarks>
    /// 🔴 沒有這個記號就沒辦法判斷「該不該重載」。同一組樓層的兩份鏡像版面中心相距約 845 碼，
    /// 沿用上一層的版面會讓所有依賴座標的功能（寶箱點位、各房敵人數、手動導航驗證）
    /// 全部靜默失效。
    /// </remarks>
    private (byte Floor, byte Tileset) _loadedLayout = (255, 255);

    private void LoadWalls()
    {
        Service.Log($"loading walls for current floor...");
        Walls.Clear();
        // 🔴 座標也要一起作廢。半套狀態（新樓層 + 舊座標）比完全沒有資料更糟：
        //    校驗閘門會拿舊座標去比，得到「不通過」而不是「不知道」。
        Array.Clear(RoomCenters);
        ResetCoordGate();
        _loadedLayout = (Palace.Floor, Palace.Progress.Tileset);

        var floorset = Palace.Floor / 10;
        var key = $"{(int)Palace.DungeonId}.{floorset + 1}";
        if (!LoadedFloors.Walls.TryGetValue(key, out var floor))
        {
            Service.Log($"unable to load floorset {key}");
            return;
        }

        if (Palace.Progress.Tileset == 2)
        {
            Service.Log($"hall of fallacies - nothing to do");
            return;
        }

        // 兩面都留著，之後由 CheckRoomCoords 拿本人位置自我校準決定用哪一面。
        _faceA = floor.RoomsA;
        _faceB = floor.RoomsB;

        // 起手仍然照遊戲回報的 Progress.Tileset 選面＝維持既有行為；
        // 認不得的值退回 A 面（以前是整個不載入，那會讓所有依賴座標的功能一起消失）。
        var initial = Palace.Progress.Tileset == 1 ? 1 : 0;
        if (Palace.Progress.Tileset > 1)
            Service.Logger.Information($"[DD] 認不得的版面編號 {Palace.Progress.Tileset}，先套用版面 A，交給座標自我校準判斷。");
        ApplyFace(initial);
    }

    /// <summary>
    /// 把某一面鏡像版面套進 <see cref="RoomCenters"/> 與 <see cref="Walls"/>。
    /// </summary>
    /// <remarks>
    /// 🔴 <b>中心座標與牆壁必須同時換</b>。兩者是同一份版面資料，只換一半就會回到
    /// 「新樓層 + 舊座標」那種半套狀態——閘門會得到「不通過」而不是「不知道」。
    /// </remarks>
    private void ApplyFace(int face)
    {
        var tileset = face == 0 ? _faceA : _faceB;
        if (tileset == null)
            return;

        _activeFace = face;
        Walls.Clear();
        Array.Clear(RoomCenters);
        Array.Clear(_centerFitted);

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

        FillMissingRoomCenters();
        RoomTolerance = ComputeRoomTolerance();
    }

    #region 房間中心的補值與容許誤差

    /// <summary>這一格的中心是網格擬合補出來的，不是 <see cref="LoadedFloors"/> 真的有的值。</summary>
    private readonly bool[] _centerFitted = new bool[DeepDungeonState.NumRooms];

    /// <summary>
    /// 網格擬合的最大容許殘差（碼）。已知中心對不上線性網格到這個程度就整個不補。
    /// </summary>
    /// <remarks>
    /// 實測房間中心<b>不在完美網格上</b>，所以擬合本來就有殘差；但殘差大到這個地步
    /// 代表這一面根本不是規則網格，補出來的座標會比沒有更糟（會把寶箱畫進錯的房間）。
    /// ⇒ 寧可留 null 讓那一格退化成「地圖說有、位置不明」。
    /// </remarks>
    private const float GridFitMaxResidual = 15f;

    /// <summary>
    /// 用已知的房間中心做 5×5 線性網格擬合，補上「地圖說有房間但座標表是 default」的格子。
    /// </summary>
    /// <remarks>
    /// <see cref="LoadedFloors"/> 是上游一格一格 dump 出來的，缺格不是「那裡沒有房間」而是
    /// 「當時沒 dump 到」。缺格的後果是 <see cref="NearestRoom"/> 把那間房裡的東西
    /// <b>歸屬到隔壁房</b>——靜默畫錯，比不畫更糟。
    /// <para>
    /// 做法：X 只跟 col 有關、Z 只跟 row 有關（房號＝5×row+col，已由反組譯確認是線性格號），
    /// 兩軸各做一次最小平方直線擬合。殘差超過 <see cref="GridFitMaxResidual"/> 就整個放棄。
    /// </para>
    /// <para>🔴 只補 <c>MapData</c> 說有房間的格子。四角那種連遊戲都說不是房間的格子不補。</para>
    /// </remarks>
    private void FillMissingRoomCenters()
    {
        // 兩軸各自的 (自變數, 應變數) 樣本
        var (nx, sumCol, sumColSq, sumX, sumColX) = (0, 0f, 0f, 0f, 0f);
        var (nz, sumRow, sumRowSq, sumZ, sumRowZ) = (0, 0f, 0f, 0f, 0f);
        var distinctCols = 0;
        var distinctRows = 0;
        Span<bool> seenCol = stackalloc bool[5];
        Span<bool> seenRow = stackalloc bool[5];

        for (var i = 0; i < DeepDungeonState.NumRooms; ++i)
        {
            if (RoomCenters[i] is not WPos c)
                continue;
            var row = (float)(i / 5);
            var col = (float)(i % 5);
            ++nx;
            sumCol += col;
            sumColSq += col * col;
            sumX += c.X;
            sumColX += col * c.X;
            ++nz;
            sumRow += row;
            sumRowSq += row * row;
            sumZ += c.Z;
            sumRowZ += row * c.Z;
            if (!seenCol[i % 5])
            {
                seenCol[i % 5] = true;
                ++distinctCols;
            }
            if (!seenRow[i / 5])
            {
                seenRow[i / 5] = true;
                ++distinctRows;
            }
        }

        // 少於兩個不同的欄／列就擬不出斜率
        if (distinctCols < 2 || distinctRows < 2)
            return;

        var denX = nx * sumColSq - sumCol * sumCol;
        var denZ = nz * sumRowSq - sumRow * sumRow;
        if (Math.Abs(denX) < 1e-3f || Math.Abs(denZ) < 1e-3f)
            return;

        var bx = (nx * sumColX - sumCol * sumX) / denX;
        var ax = (sumX - bx * sumCol) / nx;
        var bz = (nz * sumRowZ - sumRow * sumZ) / denZ;
        var az = (sumZ - bz * sumRow) / nz;

        // 殘差檢查：擬合對不上已知的格子，就不要拿它去補未知的格子
        var maxResidual = 0f;
        for (var i = 0; i < DeepDungeonState.NumRooms; ++i)
        {
            if (RoomCenters[i] is not WPos c)
                continue;
            var dx = Math.Abs(ax + bx * (i % 5) - c.X);
            var dz = Math.Abs(az + bz * (i / 5) - c.Z);
            maxResidual = Math.Max(maxResidual, Math.Max(dx, dz));
        }
        if (maxResidual > GridFitMaxResidual)
        {
            Service.Logger.Information($"[DD] 房間中心網格擬合放棄：最大殘差 {maxResidual:f1}y 超過 {GridFitMaxResidual}y，缺格維持未知。");
            return;
        }

        var filled = 0;
        for (var i = 0; i < DeepDungeonState.NumRooms; ++i)
        {
            if (RoomCenters[i] != null)
                continue;
            if ((byte)Palace.Rooms[i] == 0)
                continue; // 遊戲自己說這一格不是房間（四角就是這種）——不補
            RoomCenters[i] = new WPos(ax + bx * (i % 5), az + bz * (i / 5));
            _centerFitted[i] = true;
            ++filled;
        }

        if (filled > 0)
            Service.Logger.Information(
                $"[DD] 房間中心網格擬合：補了 {filled} 格（樓層 {Palace.Floor}、版面 {(_activeFace == 0 ? "A" : "B")}），" +
                $"最大殘差 {maxResidual:f1}y、X={ax:f1}+{bx:f1}·col、Z={az:f1}+{bz:f1}·row");
    }

    /// <summary>
    /// 逐版面實算房間歸屬的容許誤差：取所有房間裡「最近鄰房距」最大的那一個的一半，再加餘裕。
    /// </summary>
    /// <remarks>
    /// 為什麼取最大而不是最小：這個值是 <see cref="NearestRoom"/> 的<b>截斷距離</b>，
    /// 而不是分辨兩間房的門檻——歸屬本來就是「離誰最近算誰的」，截斷只決定
    /// 「離所有房都太遠就當不在任何房裡」。取最小會讓格子大的那幾間房內側的東西
    /// 被靜默丟掉（實測厄運迷宮有 25.2% 的格子踩到這個）。
    /// </remarks>
    private float ComputeRoomTolerance()
    {
        var maxNearest = 0f;
        for (var i = 0; i < DeepDungeonState.NumRooms; ++i)
        {
            if (RoomCenters[i] is not WPos a)
                continue;
            var bestSq = float.MaxValue;
            for (var j = 0; j < DeepDungeonState.NumRooms; ++j)
            {
                if (i == j || RoomCenters[j] is not WPos b)
                    continue;
                var dsq = (b - a).LengthSq();
                if (dsq < bestSq)
                    bestSq = dsq;
            }
            if (bestSq < float.MaxValue)
                maxNearest = Math.Max(maxNearest, MathF.Sqrt(bestSq));
        }

        // 半幅 + 8y 餘裕；下限維持原本的 35y（只會變寬，不可能讓既有行為退步）
        var tol = Math.Clamp(maxNearest * 0.5f + 8f, RoomCenterToleranceFloor, RoomCenterToleranceCap);
        Service.Logger.Information(
            $"[DD] 房間歸屬容許誤差：樓層 {Palace.Floor}、版面 {(_activeFace == 0 ? "A" : "B")}、" +
            $"最大最近鄰房距 {maxNearest:f1}y ⇒ 採用 {tol:f1}y");
        return tol;
    }

    #endregion

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
