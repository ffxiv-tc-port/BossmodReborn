using System.Runtime.InteropServices;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;

namespace BossMod.Global.DeepDungeon;

/// <summary>
/// 「嘗試踩可能的寶箱點」的站位規劃：純函式，不碰遊戲狀態（量測台直接呼叫）。
/// </summary>
/// <remarks>
/// 同樣的輸入一定得到同樣的站位與順序：候選點依建立順序編號，平手一律取編號小的。
/// </remarks>
internal static class HoardWalkPlanner
{
    /// <summary>離點多近算「站在上面」（碼）。錄影裡沒用感知寶藏時，寶藏現形那一刻人離點都在這個範圍內。</summary>
    public const float RevealRadius = 1.5f;

    /// <summary>規劃站位時離每個涵蓋點的上限：比 <see cref="RevealRadius"/> 少 0.5y，留給 AI 停步的誤差（尋路格子 0.5y）。</summary>
    public const float PlanRadius = RevealRadius - 0.5f;

    /// <summary>站位離陷阱圓邊緣至少要有的距離（碼）。</summary>
    public const float StandClearance = 0.5f;

    /// <summary>連續站滿幾秒才算確認過的預設值（與 <see cref="AutoDDConfig.HoardWalkDwellSeconds"/> 的預設相同；實際用的是設定值）。</summary>
    public const double DwellSeconds = 4.5d;

    public enum Skip : byte
    {
        None,
        /// <summary>只被資料庫陷阱（內建表／PalacePal）蓋住；「無視陷阱」打開就能去。</summary>
        DbTrap,
        /// <summary>被已現形的陷阱實體蓋住，「無視陷阱」也照樣跳過。</summary>
        RevealedTrap,
    }

    /// <summary>資料庫陷阱離候選點這麼近（碼）就算「候選點本身」那一個；離得更遠的照閃。</summary>
    /// <remarks>寶藏點與它自己那個陷阱點幾乎都是 0.00y 重合；0.5y 內的陷阱，站上那一點本來就會踩到。</remarks>
    public const float OwnSpotRange = 0.5f;

    /// <param name="Own">≥0＝它就是候選點 <c>points[Own]</c> 本身（只放寬候選點本身）：涵蓋得到那一點的站位不把它算進去；-1＝一律照閃。</param>
    public readonly record struct Trap(WPos Center, float Radius, bool Db, int Own)
    {
        public Trap(WPos center, float radius, bool db) : this(center, radius, db, -1) { }
    }

    /// <summary>一個站位與它涵蓋的點（<c>covers[CoverStart..CoverStart+CoverCount)</c>，點的索引由小到大）。</summary>
    public readonly record struct Stand(WPos Pos, int CoverStart, int CoverCount);

    /// <summary>
    /// 對 <paramref name="eligible"/> 為真的點規劃站位（貪婪：一次涵蓋最多點的優先；同數時離最遠那個涵蓋點最近的優先，再同才比離陷阱遠）。
    /// </summary>
    /// <param name="traps">全部都當成不能站的圓；<see cref="Trap.Db"/> 只用來分辨跳過的原因。</param>
    /// <param name="skip">長度至少 <paramref name="points"/>.Count；不可涵蓋的點填原因，其餘填 <see cref="Skip.None"/>。</param>
    public static void Plan(IReadOnlyList<WPos> points, IReadOnlyList<bool> eligible, IReadOnlyList<Trap> traps, List<Stand> stands, List<int> covers, Skip[] skip)
    {
        stands.Clear();
        covers.Clear();
        var n = points.Count;
        Array.Fill(skip, Skip.None, 0, n);

        List<WPos> cand = [];
        void AddRing(WPos c, float radius, int count, float phase)
        {
            for (var k = 0; k < count; ++k)
            {
                var a = phase + k * (2f * MathF.PI / count);
                cand.Add(new WPos(c.X + radius * MathF.Cos(a), c.Z + radius * MathF.Sin(a)));
            }
        }

        const float pairReach = 2f * PlanRadius;
        for (var i = 0; i < n; ++i)
        {
            if (!eligible[i])
                continue;
            var p = points[i];
            cand.Add(p);
            AddRing(p, 0.5f * PlanRadius, 8, 0f);
            AddRing(p, 0.95f * PlanRadius, 12, MathF.PI / 12f);
        }
        for (var i = 0; i < n; ++i)
        {
            if (!eligible[i])
                continue;
            for (var j = i + 1; j < n; ++j)
            {
                if (!eligible[j])
                    continue;
                var d = points[j] - points[i];
                var len = d.Length();
                if (len > pairReach)
                    continue;
                var mid = points[i] + d * 0.5f;
                cand.Add(mid);
                // 兩圓交集是透鏡：沿垂直方向的半寬 sqrt(R²-(d/2)²)，沿連線方向只剩 R-d/2
                var axis = len > 1e-4f ? d * (1f / len) : new WDir(1f, 0f);
                var ortho = axis.OrthoL();
                var halfWidth = MathF.Sqrt(MathF.Max(0f, PlanRadius * PlanRadius - len * len * 0.25f));
                var slack = PlanRadius - len * 0.5f;
                cand.Add(mid + ortho * (0.5f * halfWidth));
                cand.Add(mid - ortho * (0.5f * halfWidth));
                cand.Add(mid + ortho * (0.9f * halfWidth));
                cand.Add(mid - ortho * (0.9f * halfWidth));
                cand.Add(mid + axis * (0.5f * slack));
                cand.Add(mid - axis * (0.5f * slack));
                for (var k = j + 1; k < n; ++k)
                {
                    if (!eligible[k] || (points[k] - points[i]).Length() > pairReach || (points[k] - points[j]).Length() > pairReach)
                        continue;
                    cand.Add(new WPos((points[i].X + points[j].X + points[k].X) / 3f, (points[i].Z + points[j].Z + points[k].Z) / 3f));
                }
            }
        }

        // 每個候選的安全度與涵蓋清單
        var m = cand.Count;
        var clear = new float[m];
        var valid = new bool[m];
        var coverStart = new int[m];
        var coverCount = new int[m];
        List<int> coverPool = [];
        var coverableAny = new bool[n];
        var coverableIgnoringDb = new bool[n];
        const float reachSq = (PlanRadius + 1e-3f) * (PlanRadius + 1e-3f);
        for (var c = 0; c < m; ++c)
        {
            var pos = cand[c];
            var clearAll = float.MaxValue;
            var clearRevealed = float.MaxValue;
            for (var t = 0; t < traps.Count; ++t)
            {
                var tr = traps[t];
                var dist = (pos - tr.Center).Length() - tr.Radius;
                if (!tr.Db && dist < clearRevealed)
                    clearRevealed = dist;
                if ((uint)tr.Own < (uint)n && eligible[tr.Own] && (points[tr.Own] - pos).LengthSq() <= reachSq)
                    continue;
                if (dist < clearAll)
                    clearAll = dist;
            }
            var ok = clearAll >= StandClearance;
            var okIgnoringDb = clearRevealed >= StandClearance;
            valid[c] = ok;
            clear[c] = clearAll;
            coverStart[c] = coverPool.Count;
            for (var i = 0; i < n; ++i)
            {
                if (!eligible[i] || (points[i] - pos).LengthSq() > reachSq)
                    continue;
                if (ok)
                {
                    coverPool.Add(i);
                    coverableAny[i] = true;
                }
                if (okIgnoringDb)
                    coverableIgnoringDb[i] = true;
            }
            coverCount[c] = coverPool.Count - coverStart[c];
        }

        for (var i = 0; i < n; ++i)
            if (eligible[i] && !coverableAny[i])
                skip[i] = coverableIgnoringDb[i] ? Skip.DbTrap : Skip.RevealedTrap;

        var covered = new bool[n];
        while (true)
        {
            var best = -1;
            var bestNew = 0;
            var bestClear = 0f;
            var bestFar = 0f;
            for (var c = 0; c < m; ++c)
            {
                if (!valid[c])
                    continue;
                var fresh = 0;
                var far = 0f;
                for (var k = 0; k < coverCount[c]; ++k)
                {
                    var i = coverPool[coverStart[c] + k];
                    if (covered[i])
                        continue;
                    ++fresh;
                    far = MathF.Max(far, (points[i] - cand[c]).Length());
                }
                if (fresh == 0)
                    continue;
                if (best < 0 || fresh > bestNew || fresh == bestNew && (far < bestFar - 1e-3f || far <= bestFar + 1e-3f && clear[c] > bestClear))
                {
                    best = c;
                    bestNew = fresh;
                    bestClear = clear[c];
                    bestFar = far;
                }
            }
            if (best < 0)
                break;
            var start = covers.Count;
            for (var k = 0; k < coverCount[best]; ++k)
            {
                var i = coverPool[coverStart[best] + k];
                if (covered[i])
                    continue;
                covered[i] = true;
                covers.Add(i);
            }
            stands.Add(new(cand[best], start, covers.Count - start));
        }
    }

    /// <summary>連續站在範圍內的計時；離開範圍就歸零。回 true＝這一刻站滿了 <see cref="DwellSeconds"/>。</summary>
    public static bool AdvanceDwell(ref DateTime since, bool inRange, DateTime now) => AdvanceDwellFor(ref since, inRange, now, DwellSeconds);

    /// <summary>同 <see cref="AdvanceDwell"/>，站滿 <paramref name="seconds"/> 秒才回 true。</summary>
    public static bool AdvanceDwellFor(ref DateTime since, bool inRange, DateTime now, double seconds)
    {
        if (!inRange)
        {
            since = default;
            return false;
        }
        if (since == default)
            since = now;
        return (now - since).TotalSeconds >= seconds;
    }
}

// 本檔假設與契約：
// - 全部狀態只活在這個模組實例裡，不寫設定檔；預設關，只有使用者勾選才會開始（載入／進區／換層都不會自己開）。
// - 只在主執行緒（Update／CalculateAIHints／DrawExtra）碰；交給尋路的目標區只捕獲值型別。
// - PalacePal 只讀；資料庫陷阱的禁區只在「模式開著且勾了無視陷阱」時拿掉，已現形的陷阱實體永遠照閃。
// - 看過的已現形陷阱以位置記到換層（模式開始前看過的也算）：實體離開物件表不算消失，只有本層觸發過的那個位置才不再算。
// - 「只放寬候選點本身」只拿掉與候選點重合的資料庫陷阱（規劃與目前站位的 AI 禁區），其他資料庫陷阱與已現形的陷阱照閃。
public abstract partial class AutoClear
{
    private enum HoardWalkPointState : byte { Pending, Checked, Skipped, GaveUp }

    private enum HoardWalkStop { User, FloorChanged, Intuition, NoHoardOnFloor, Revealed, AllDone, AllSkipped }

    private struct HoardWalkPoint
    {
        public WPos Pos;
        public int Room;
        public HoardWalkPointState State;
        public HoardWalkPlanner.Skip SkipReason;
        public DateTime DwellSince;
    }

    private bool _hwActive;
    /// <summary>可以在開始前先勾（只對勾的那一層有效）；只有模式開著時才真的影響走位與跳過。</summary>
    private bool _hwIgnoreTraps;
    private byte _hwIgnoreFloor;
    /// <summary>與 <see cref="_hwIgnoreTraps"/> 同一套生命週期；無視陷阱開著時沒有作用。</summary>
    private bool _hwRelaxOwnSpot;
    private byte _hwRelaxFloor;
    private byte _hwFloor;
    /// <summary>開始的那一次呼叫裡：跳過／關閉不各自印 log，併進開始那一行。</summary>
    private bool _hwStarting;
    private HoardWalkStop? _hwStartStop;
    private int _hwLogSkipDb, _hwLogSkipRevealed;
    private string? _hwProgressText;
    private (int, int, int) _hwProgressKey = (-1, -1, -1);
    private string? _hwTargetText;
    private (int, int, int) _hwTargetKey = (-1, -1, -1);
    private string? _hwToggleText;
    private double _hwToggleSeconds = -1d;
    /// <summary>上一次自動關閉／無法開始的原因（介面顯示到下一次勾選為止）。</summary>
    private string? _hwMessage;
    private readonly List<HoardWalkPoint> _hwPoints = [];
    private readonly List<WPos> _hwPlanPos = [];
    private readonly List<bool> _hwPlanEligible = [];
    private HoardWalkPlanner.Skip[] _hwPlanSkip = [];
    private readonly List<HoardWalkPlanner.Trap> _hwTraps = [];
    private readonly List<HoardWalkPlanner.Stand> _hwStands = [];
    private readonly List<int> _hwStandRoom = [];
    private readonly List<int> _hwCovers = [];
    private int _hwTarget = -1;
    private WPos _hwTargetPos;
    private bool _hwDirty;
    private int _hwTrapSig;
    /// <summary>樓層 <see cref="_hwSeenTrapsFloor"/> 看過的已現形陷阱位置；只存座標，換層在開始時清掉。</summary>
    private readonly List<WPos> _hwSeenTraps = [];
    private byte _hwSeenTrapsFloor = 255;
    private bool _hwSeenTrapsLogged;
    /// <summary>收到「這一層似乎有／沒有寶藏」（感知寶藏）時的樓層；255＝沒收到過。</summary>
    private byte _hwIntuitionFloor = 255;
    private uint _hwIntuitionMsg;
    private DateTime _hwNextPomanderCheck;
    private float _hwBestDist = float.MaxValue;
    private double _hwNoProgress;
    private DateTime _hwLastSteer;

    /// <summary>直線目標區的作用半徑（碼）：蓋得住一整間房。</summary>
    private const float HoardWalkApproachRadius = 60f;

    /// <summary>戰鬥外累計這麼多秒都沒有更接近目前站位（0.5y 以上）就放棄它。</summary>
    private const double HoardWalkGiveUpSeconds = 30d;

    private const uint ColorHoardWalkChecked = 0xFF9A9A9Au;
    private const uint ColorHoardWalkTarget = 0xFFFFFFFFu;

    private bool HoardWalkSteering => _hwActive && _hwTarget >= 0;
    private bool HoardWalkIgnoringTraps => _hwActive && _hwIgnoreTraps;
    private static double HoardWalkDwellSeconds => Math.Clamp(Config.HoardWalkDwellSeconds, 1f, 30f);

    private void HoardWalkOnSystemLog(uint id)
    {
        // 7272「這一層似乎有寶藏……」／7273「這一層似乎沒有寶藏……」＝這一層用過了魔陶器：感知寶藏
        if (id is 7272 or 7273)
        {
            _hwIntuitionFloor = Palace.Floor;
            _hwIntuitionMsg = id;
        }
    }

    private static string HoardWalkStopText(HoardWalkStop reason) => reason switch
    {
        HoardWalkStop.FloorChanged => Loc.T("DD_HoardWalkStopFloor", "Stopped: you changed floors."),
        HoardWalkStop.Intuition => Loc.T("DD_HoardWalkStopIntuition", "Stopped: a Pomander of Intuition was used on this floor - the normal hoard handling takes over."),
        HoardWalkStop.NoHoardOnFloor => Loc.T("DD_HoardWalkStopNoHoard", "Stopped: the Pomander of Intuition says this floor has no hoard."),
        HoardWalkStop.Revealed => Loc.T("DD_HoardWalkStopRevealed", "Stopped: the Accursed Hoard has appeared."),
        HoardWalkStop.AllDone => Loc.T("DD_HoardWalkStopAllDone", "Stopped: every possible spot on this floor has been checked or skipped."),
        _ => "",
    };

    private static string HoardWalkStopLog(HoardWalkStop reason) => reason switch
    {
        HoardWalkStop.User => "使用者關閉",
        HoardWalkStop.FloorChanged => "換層",
        HoardWalkStop.Intuition => "這一層用了魔陶器：感知寶藏",
        HoardWalkStop.NoHoardOnFloor => "感知寶藏說這一層沒有寶藏",
        HoardWalkStop.Revealed => "埋藏的寶藏已現形",
        HoardWalkStop.AllDone => "所有點都確認過或跳過",
        HoardWalkStop.AllSkipped => "一點都沒確認就全部跳過",
        _ => reason.ToString(),
    };

    private string HoardWalkAllSkippedText()
    {
        int db = 0, revealed = 0, gaveUp = 0;
        foreach (ref readonly var p in CollectionsMarshal.AsSpan(_hwPoints))
        {
            if (p.State == HoardWalkPointState.GaveUp)
                ++gaveUp;
            else if (p.SkipReason == HoardWalkPlanner.Skip.RevealedTrap)
                ++revealed;
            else
                ++db;
        }
        if (revealed == 0 && gaveUp == 0)
            return string.Format(Loc.T("DD_HoardWalkStopAllDbTraps", "Stopped right away: all {0} spots on this floor are covered by trap locations from the databases (built-in table / PalacePal), so there is nowhere to stand. Clear or reveal the traps first (Pomander of Safety or Sight), or tick \"Ignore traps\" before starting."), db);
        return string.Format(Loc.T("DD_HoardWalkStopAllSkipped", "Stopped: none of the {0} spots could be checked - {1} covered by database traps (\"Ignore traps\" would go there), {2} covered by visible traps, {3} not reachable."), _hwPoints.Count, db, revealed, gaveUp);
    }

    private void StopHoardWalk(HoardWalkStop reason)
    {
        if (!_hwActive)
            return;
        var (checkedN, skipped, total) = HoardWalkCounts();
        _hwMessage = reason switch
        {
            HoardWalkStop.User => null,
            HoardWalkStop.AllSkipped => HoardWalkAllSkippedText(),
            _ => HoardWalkStopText(reason),
        };
        _hwActive = false;
        _hwIgnoreTraps = false;
        _hwRelaxOwnSpot = false;
        _hwTarget = -1;
        // 跨房趕路途中關掉時，別讓 AI 繼續走去那間房
        if (_destinationSource == DestinationSource.HoardWalk)
        {
            DesiredRoom = 0;
            _destinationSource = DestinationSource.User;
        }
        if (_hwStarting)
        {
            _hwStartStop = reason;
            return;
        }
        Service.Logger.Information($"[DD] 試踩寶藏點：關閉（{HoardWalkStopLog(reason)}）；樓層 {_hwFloor}、已確認 {checkedN}／跳過 {skipped}／共 {total} 點");
    }

    private (int Checked, int Skipped, int Total) HoardWalkCounts()
    {
        int c = 0, s = 0;
        foreach (ref readonly var p in CollectionsMarshal.AsSpan(_hwPoints))
        {
            if (p.State == HoardWalkPointState.Checked)
                ++c;
            else if (p.State is HoardWalkPointState.Skipped or HoardWalkPointState.GaveUp)
                ++s;
        }
        return (c, s, _hwPoints.Count);
    }

    private bool HoardActorPresent()
    {
        foreach (var a in World.Actors)
            if (a.OID is (uint)OID.BandedCoffer or (uint)OID.BandedCofferIndicator)
                return true;
        return false;
    }

    private void TryStartHoardWalk(Actor player, RoomCoordState coords)
    {
        _hwMessage = null;
        string? refuse = null;
        if (Palace.DungeonId == DeepDungeonState.DungeonType.None || Palace.IsBossFloor || BetweenFloors)
            refuse = Loc.T("DD_HoardWalkCannotHere", "Cannot start here (boss floor, or between floors).");
        else if (_hoardFound || HoardActorPresent())
            refuse = Loc.T("DD_HoardWalkCannotRevealed", "Cannot start: the Accursed Hoard on this floor has already appeared.");
        else if (_hwIntuitionFloor == Palace.Floor || Palace.GetPomanderState(PomanderID.Intuition).Active)
            refuse = Loc.T("DD_HoardWalkCannotIntuition", "Cannot start: a Pomander of Intuition has already been used on this floor.");
        else if (coords != RoomCoordState.Ok)
            refuse = Loc.T("DD_HoardWalkCannotCoords", "Cannot start: the room coordinates for this floor are not confirmed (yet), so the spots cannot be matched to this floor's rooms. Walk into a room and try again.");
        else if (PalacePalIpc.GetHoards((ushort)World.CurrentZone) is not { } all)
            refuse = Loc.T("DD_HoardWalkCannotPal", "Cannot start: PalacePal is not installed, or its data is not available.");
        else if (StartHoardWalkCore(all, PalacePalIpc.GetOwnConfirmedHoards((ushort)World.CurrentZone), player) == 0)
            refuse = Loc.T("DD_HoardWalkCannotNoPoints", "Cannot start: none of PalacePal's possible spots fall inside this floor's rooms.");

        if (refuse == null)
            return;
        _hwMessage = refuse;
        Service.Logger.Information($"[DD] 試踩寶藏點：無法開始（樓層 {Palace.Floor}）：{refuse}");
    }

    /// <summary>用給定的兩份座標開始（量測台直接呼叫）。回傳落在這一層房間裡的點數；0＝沒有開始。</summary>
    private int StartHoardWalkCore(List<Vector3> all, List<Vector3>? ownConfirmed, Actor? player)
    {
        List<WPos> raw = new(all.Count + (ownConfirmed?.Count ?? 0));
        if (ownConfirmed != null)
            foreach (var v in ownConfirmed)
                raw.Add(new WPos(v.X, v.Z));
        foreach (var v in all)
            raw.Add(new WPos(v.X, v.Z));
        raw.Sort((a, b) => a.X != b.X ? a.X.CompareTo(b.X) : a.Z.CompareTo(b.Z));

        _hwPoints.Clear();
        var deduped = 0;
        for (var i = 0; i < raw.Count; ++i)
        {
            var p = raw[i];
            var dup = false;
            for (var j = 0; j < deduped; ++j)
                if (raw[j].AlmostEqual(p, PalaceDedupeRange))
                {
                    dup = true;
                    break;
                }
            if (dup)
                continue;
            raw[deduped++] = p;
        }
        for (var i = 0; i < deduped; ++i)
        {
            var p = raw[i];
            // 跨十層的聯集：只留落在這一層真的有的房間裡的（最近的房心是這一層沒有的房＝別層的點）
            var room = NearestRoom(p, RoomTolerance);
            if (room < 0 || (byte)Palace.Rooms[room] == 0)
                continue;
            _hwPoints.Add(new() { Pos = p, Room = room });
        }

        if (_hwPoints.Count == 0)
            return 0;

        _hwActive = true;
        if (_hwIgnoreFloor != Palace.Floor)
            _hwIgnoreTraps = false;
        if (_hwRelaxFloor != Palace.Floor)
            _hwRelaxOwnSpot = false;
        _hwFloor = Palace.Floor;
        if (_hwSeenTrapsFloor != _hwFloor)
        {
            _hwSeenTraps.Clear();
            _hwSeenTrapsFloor = _hwFloor;
            _hwSeenTrapsLogged = false;
        }
        _hwTarget = -1;
        _hwDirty = true;
        _hwTrapSig = 0;
        var ignoreAtStart = _hwIgnoreTraps;
        var relaxAtStart = _hwRelaxOwnSpot;
        _hwStarting = true;
        _hwStartStop = null;
        _hwLogSkipDb = _hwLogSkipRevealed = 0;
        try
        {
            if (player != null)
                UpdateHoardWalkCore(player);
        }
        finally
        {
            _hwStarting = false;
        }
        var (checkedN, skipped, _) = HoardWalkCounts();
        Service.Logger.Information($"[DD] 試踩寶藏點：開始；樓層 {_hwFloor}、PalacePal 全部 {all.Count} 點＋我方確認 {(ownConfirmed == null ? "（沒有端點）" : ownConfirmed.Count.ToString())} 點、去重後 {deduped} 點、落在這一層房間裡 {_hwPoints.Count} 點；"
            + $"站上半徑 {HoardWalkPlanner.RevealRadius}y、站滿 {HoardWalkDwellSeconds:0.0#}s、無視陷阱 {(ignoreAtStart ? "開" : "關")}；"
            + $"規劃：可去 {_hwPoints.Count - checkedN - skipped} 點、跳過 {skipped} 點（資料庫陷阱 {_hwLogSkipDb}／已現形的陷阱 {_hwLogSkipRevealed}）"
            + (_hwStartStop is { } stop ? $"；立刻關閉（{HoardWalkStopLog(stop)}）" : "")
            + $"；只放寬候選點本身 {(relaxAtStart ? "開" : "關")}");
        return _hwPoints.Count;
    }

    /// <summary>每幀（<see cref="Update"/>）：自動關閉條件、站立計時、需要時重新規劃。</summary>
    private void UpdateHoardWalk()
    {
        if (!_hwActive)
            return;
        if (World.Party.Player() is not { } player)
            return;
        UpdateHoardWalkCore(player);
    }

    private void UpdateHoardWalkCore(Actor player)
    {
        if (Palace.Floor != 0 && Palace.Floor != _hwFloor)
        {
            StopHoardWalk(HoardWalkStop.FloorChanged);
            return;
        }
        if (_hwIntuitionFloor == _hwFloor)
        {
            StopHoardWalk(_hwIntuitionMsg == 7273 ? HoardWalkStop.NoHoardOnFloor : HoardWalkStop.Intuition);
            return;
        }
        var now = World.CurrentTime;
        if (now >= _hwNextPomanderCheck && Palace.DungeonId != DeepDungeonState.DungeonType.None)
        {
            _hwNextPomanderCheck = now.AddSeconds(0.5d);
            if (Palace.GetPomanderState(PomanderID.Intuition).Active)
            {
                StopHoardWalk(HoardWalkStop.Intuition);
                return;
            }
        }

        // 一次掃完：寶藏現形、已現形的陷阱（改變了就要重新規劃）
        var revealed = false;
        var sig = new HashCode();
        sig.Add(Config.TrapHints && _trapsHidden);
        sig.Add(_hwIgnoreTraps);
        sig.Add(_hwRelaxOwnSpot);
        sig.Add(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(_hardTraps));
        sig.Add(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(_lightTraps));
        sig.Add(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(_communityHardTraps));
        sig.Add(IgnoreTraps.Count);
        // 不看開關：記住的陷阱被觸發後就不再算（見 CollectHoardWalkTraps）
        sig.Add(_triggeredTraps.Count);
        foreach (var a in World.Actors)
        {
            if (a.OID is (uint)OID.BandedCoffer or (uint)OID.BandedCofferIndicator)
                revealed = true;
            else if (RevealedTrapOIDs.Contains(a.OID))
            {
                sig.Add(a.Position);
                RememberHoardWalkTrap(a.Position);
            }
        }
        sig.Add(_hwSeenTraps.Count);
        if (revealed || _hoardFound)
        {
            StopHoardWalk(HoardWalkStop.Revealed);
            return;
        }

        var points = CollectionsMarshal.AsSpan(_hwPoints);
        var radiusSq = HoardWalkPlanner.RevealRadius * HoardWalkPlanner.RevealRadius;
        var dwellSeconds = HoardWalkDwellSeconds;
        for (var i = 0; i < points.Length; ++i)
        {
            ref var p = ref points[i];
            if (p.State == HoardWalkPointState.Checked)
                continue;
            var inRange = (player.Position - p.Pos).LengthSq() <= radiusSq;
            if (HoardWalkPlanner.AdvanceDwellFor(ref p.DwellSince, inRange, now, dwellSeconds))
            {
                p.State = HoardWalkPointState.Checked;
                _hwDirty = true;
                Service.Logger.Information($"[DD] 試踩寶藏點：確認完一點（房間 {p.Room}、({p.Pos.X:f1}, {p.Pos.Z:f1})），站滿 {dwellSeconds:0.0#}s 沒有現形；本人 ({player.Position.X:f1}, {player.Position.Z:f1})");
            }
        }

        var sigValue = sig.ToHashCode();
        if (_hwDirty || sigValue != _hwTrapSig)
        {
            _hwTrapSig = sigValue;
            _hwDirty = false;
            ReplanHoardWalk(player);
        }

        var (checkedN, skipped, total) = HoardWalkCounts();
        if (checkedN + skipped >= total)
            StopHoardWalk(checkedN == 0 ? HoardWalkStop.AllSkipped : HoardWalkStop.AllDone);
    }

    private void ReplanHoardWalk(Actor player)
    {
        var points = CollectionsMarshal.AsSpan(_hwPoints);
        _hwPlanPos.Clear();
        _hwPlanEligible.Clear();
        foreach (ref readonly var p in points)
        {
            _hwPlanPos.Add(p.Pos);
            _hwPlanEligible.Add(p.State is HoardWalkPointState.Pending or HoardWalkPointState.Skipped);
        }
        if (_hwPlanSkip.Length < points.Length)
            _hwPlanSkip = new HoardWalkPlanner.Skip[points.Length];

        CollectHoardWalkTraps();
        HoardWalkPlanner.Plan(_hwPlanPos, _hwPlanEligible, _hwTraps, _hwStands, _hwCovers, _hwPlanSkip);

        int newDb = 0, newRevealed = 0, reopened = 0, firstSkip = -1, firstReopen = -1;
        for (var i = 0; i < points.Length; ++i)
        {
            ref var p = ref points[i];
            if (!_hwPlanEligible[i])
                continue;
            var reason = _hwPlanSkip[i];
            if (reason != HoardWalkPlanner.Skip.None && p.State == HoardWalkPointState.Pending)
            {
                p.State = HoardWalkPointState.Skipped;
                p.SkipReason = reason;
                if (reason == HoardWalkPlanner.Skip.DbTrap)
                    ++newDb;
                else
                    ++newRevealed;
                if (firstSkip < 0)
                    firstSkip = i;
            }
            else if (reason == HoardWalkPlanner.Skip.None && p.State == HoardWalkPointState.Skipped)
            {
                p.State = HoardWalkPointState.Pending;
                p.SkipReason = HoardWalkPlanner.Skip.None;
                ++reopened;
                if (firstReopen < 0)
                    firstReopen = i;
            }
        }
        // 一次重新規劃最多一行（開始那一次併進「開始」那一行）
        if (_hwStarting)
        {
            _hwLogSkipDb += newDb;
            _hwLogSkipRevealed += newRevealed;
        }
        else if (newDb + newRevealed + reopened > 0)
        {
            var s = firstSkip >= 0 ? points[firstSkip] : default;
            var r = firstReopen >= 0 ? points[firstReopen] : default;
            Service.Logger.Information($"[DD] 試踩寶藏點：重新規劃；新跳過 {newDb + newRevealed} 點（資料庫陷阱 {newDb}／已現形的陷阱 {newRevealed}）"
                + (firstSkip >= 0 ? $"，例：房間 {s.Room} ({s.Pos.X:f1}, {s.Pos.Z:f1})" : "")
                + $"；先前跳過、重新可去 {reopened} 點" + (firstReopen >= 0 ? $"，例：房間 {r.Room} ({r.Pos.X:f1}, {r.Pos.Z:f1})" : ""));
        }

        _hwStandRoom.Clear();
        var keep = -1;
        for (var s = 0; s < _hwStands.Count; ++s)
        {
            var stand = _hwStands[s];
            var room = NearestRoom(stand.Pos, RoomTolerance);
            _hwStandRoom.Add(room >= 0 ? room : points[_hwCovers[stand.CoverStart]].Room);
            if (_hwTarget >= 0 && stand.Pos == _hwTargetPos)
                keep = s;
        }
        if (keep < 0)
        {
            // 最近鄰：下一個站位挑離本人最近的
            var bestSq = float.MaxValue;
            for (var s = 0; s < _hwStands.Count; ++s)
            {
                var dsq = (_hwStands[s].Pos - player.Position).LengthSq();
                if (dsq < bestSq)
                {
                    bestSq = dsq;
                    keep = s;
                }
            }
            _hwBestDist = float.MaxValue;
            _hwNoProgress = 0d;
            _hwLastSteer = default;
        }
        _hwTarget = keep;
        if (keep >= 0)
            _hwTargetPos = _hwStands[keep].Pos;
    }

    /// <summary>站位要避開的圓：已現形的陷阱永遠算；資料庫陷阱與 AI 迴避同一組條件（且沒勾無視陷阱）才算。</summary>
    private void CollectHoardWalkTraps()
    {
        _hwTraps.Clear();
        foreach (var a in World.Actors)
            if (RevealedTrapOIDs.Contains(a.OID))
                _hwTraps.Add(new(a.Position, TrapCircleRadius, false));
        var visible = _hwTraps.Count;
        foreach (var t in _hwSeenTraps)
        {
            if (HoardWalkTrapNear(visible, t) || HoardWalkTrapTriggered(t))
                continue;
            _hwTraps.Add(new(t, TrapCircleRadius, false));
            if (!_hwSeenTrapsLogged)
            {
                _hwSeenTrapsLogged = true;
                Service.Logger.Information($"[DD] 試踩寶藏點：已現形的陷阱 ({t.X:f1}, {t.Z:f1}) 目前不在物件表裡，照記住的位置繼續避開；樓層 {_hwFloor}、本層記住 {_hwSeenTraps.Count} 個（本層只印一次）");
            }
        }

        if (_hwIgnoreTraps || !Config.TrapHints || !_trapsHidden)
            return;
        AddHoardWalkDbTraps(_hardTraps, TrapCircleRadius);
        AddHoardWalkDbTraps(_lightTraps, LightTrapCircleRadius);
        AddHoardWalkDbTraps(_communityHardTraps, TrapCircleRadius);
    }

    /// <summary>已現形的陷阱實體進物件表時（模式開不開都呼叫）：記到它所在的這一層，換層的第一筆先清掉上一層的。</summary>
    private void HoardWalkSawTrap(WPos pos)
    {
        var floor = Palace.Floor;
        if (floor == 0 || Palace.DungeonId == DeepDungeonState.DungeonType.None)
            return;
        if (_hwSeenTrapsFloor != floor)
        {
            _hwSeenTraps.Clear();
            _hwSeenTrapsFloor = floor;
            _hwSeenTrapsLogged = false;
        }
        RememberHoardWalkTrap(pos);
    }

    private void RememberHoardWalkTrap(WPos pos)
    {
        foreach (var t in _hwSeenTraps)
            if (t.AlmostEqual(pos, 1f))
                return;
        _hwSeenTraps.Add(pos);
    }

    /// <summary>前 <paramref name="count"/> 個 <see cref="_hwTraps"/> 裡有沒有同一個陷阱。</summary>
    private bool HoardWalkTrapNear(int count, WPos pos)
    {
        for (var i = 0; i < count; ++i)
            if (_hwTraps[i].Center.AlmostEqual(pos, 1f))
                return true;
        return false;
    }

    /// <summary>不看「踩過的陷阱本層不再閃避」開關：觸發過的陷阱實體真的消失了，與沒記住時一樣算可去。</summary>
    private bool HoardWalkTrapTriggered(WPos pos)
    {
        foreach (var t in _triggeredTraps)
            if (pos.InCircle(t, TriggeredTrapRadius))
                return true;
        return false;
    }

    /// <summary>AI 禁區用：只放寬候選點本身開著時，目前站位涵蓋、還沒確認的點上的那個資料庫陷阱不擋（其餘照閃）。</summary>
    private bool HoardWalkOwnSpotTrap(WPos trap)
    {
        if (!HoardWalkSteering || !_hwRelaxOwnSpot || _hwIgnoreTraps)
            return false;
        var stand = _hwStands[_hwTarget];
        var points = CollectionsMarshal.AsSpan(_hwPoints);
        for (var k = 0; k < stand.CoverCount; ++k)
        {
            ref readonly var p = ref points[_hwCovers[stand.CoverStart + k]];
            if (p.State != HoardWalkPointState.Checked && trap.InCircle(p.Pos, HoardWalkPlanner.OwnSpotRange))
                return true;
        }
        return false;
    }

    private void AddHoardWalkDbTraps(WPos[] source, float radius)
    {
        var reach = HoardWalkPlanner.PlanRadius + radius + HoardWalkPlanner.StandClearance + 0.1f;
        var reachSq = reach * reach;
        var points = CollectionsMarshal.AsSpan(_hwPoints);
        var relax = _hwRelaxOwnSpot;
        foreach (var trap in source)
        {
            var near = false;
            var own = -1;
            var ownSq = HoardWalkPlanner.OwnSpotRange * HoardWalkPlanner.OwnSpotRange;
            for (var i = 0; i < points.Length; ++i)
            {
                var dsq = (points[i].Pos - trap).LengthSq();
                if (dsq > reachSq)
                    continue;
                near = true;
                if (!relax)
                    break;
                if (dsq <= ownSq)
                {
                    own = i;
                    ownSq = dsq;
                }
            }
            if (!near || IsTriggeredTrap(trap))
                continue;
            var ignored = false;
            foreach (var ig in IgnoreTraps)
                if (ig.AlmostEqual(trap, 1f))
                {
                    ignored = true;
                    break;
                }
            if (!ignored)
                _hwTraps.Add(new(trap, radius, true, own));
        }
    }

    /// <summary>
    /// 在趕路閘門內呼叫（與 <see cref="HandleFloorPathfind"/> 同一組條件）：別的房間交給樓層尋路，同一間房直接加目標區。
    /// </summary>
    private void AddHoardWalkGoal(Actor player, AIHints hints)
    {
        if (!HoardWalkSteering)
            return;
        var stand = _hwStands[_hwTarget];
        var standRoom = _hwStandRoom[_hwTarget];
        var playerRoom = FindPlayerRoom(player);
        if (standRoom >= 0 && playerRoom >= 0 && standRoom != playerRoom)
        {
            // 中間的房還沒揭開＝連通旗標不知道、樓層尋路找不到路 ⇒ 先去最近的未探索房（與「探索所有房間」同一個選法）
            var room = standRoom;
            if (FloorPathfind.FirstStep(Palace.Rooms, playerRoom, standRoom, out _) == 0 && FindNextUnexploredRoom(player) is var unexplored && unexplored > 0)
                room = unexplored;
            DesiredRoom = room;
            _destinationSource = DestinationSource.HoardWalk;
        }
        else
        {
            DesiredRoom = 0;
            hints.GoalZones.Add(GoalProximityLinear(stand.Pos, HoardWalkApproachRadius, player.InCombat ? 0.5f : 10f));
            _travelGoalAdded = true;
        }

        var now = World.CurrentTime;
        if (player.InCombat)
        {
            _hwLastSteer = now;
            return;
        }
        var dt = _hwLastSteer == default ? 0d : Math.Min(0.5d, (now - _hwLastSteer).TotalSeconds);
        _hwLastSteer = now;
        var d = (player.Position - stand.Pos).Length();
        if (d <= HoardWalkPlanner.RevealRadius || d < _hwBestDist - 0.5f)
        {
            _hwBestDist = MathF.Min(d, _hwBestDist);
            _hwNoProgress = 0d;
            return;
        }
        _hwNoProgress += dt;
        if (_hwNoProgress < HoardWalkGiveUpSeconds)
            return;

        var points = CollectionsMarshal.AsSpan(_hwPoints);
        for (var k = 0; k < stand.CoverCount; ++k)
        {
            ref var p = ref points[_hwCovers[stand.CoverStart + k]];
            if (p.State == HoardWalkPointState.Pending)
                p.State = HoardWalkPointState.GaveUp;
        }
        Service.Logger.Information($"[DD] 試踩寶藏點：放棄站位 ({stand.Pos.X:f1}, {stand.Pos.Z:f1})（房間 {standRoom}、涵蓋 {stand.CoverCount} 點）：戰鬥外 {HoardWalkGiveUpSeconds:f0} 秒沒有更接近，最近 {_hwBestDist:f1}y；這幾點算跳過");
        _hwTarget = -1;
        _hwDirty = true;
    }

    /// <summary>小地圖下方的兩個開關、進度與點位標記（<paramref name="gridOrigin"/>＝小地圖第 0 格左上角的螢幕座標）。</summary>
    private void DrawHoardWalk(Vector2 gridOrigin, Actor player, RoomCoordState coords)
    {
        if (_hwActive)
            DrawHoardWalkMarkers(gridOrigin);

        var dwellSeconds = HoardWalkDwellSeconds;
        if (_hwToggleText == null || dwellSeconds != _hwToggleSeconds)
        {
            _hwToggleSeconds = dwellSeconds;
            _hwToggleText = string.Format(Loc.T("DD_HoardWalkToggle", "Try the possible hoard spots (stand on each for {0:0.#}s)"), dwellSeconds);
        }
        var on = _hwActive;
        if (ImGui.Checkbox(_hwToggleText, ref on))
        {
            if (on)
                TryStartHoardWalk(player, coords);
            else
                StopHoardWalk(HoardWalkStop.User);
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(Loc.T("DD_HoardWalkTooltip",
                "Walks to every spot where PalacePal has seen an Accursed Hoard in this deep dungeon (its full list plus the spots you confirmed yourself) that lies inside a room that exists on this floor, and stands on each one for a few seconds (set in the Auto-DeepDungeon settings) - the hoard appears when you stand on it long enough. Spots close together are covered from one place.\n\n"
                + "Turns itself off (all three boxes) when you change floors, use a Pomander of Intuition, the hoard appears, or every spot has been checked or skipped. Nothing is saved: it is always off when you load in.\n\n"
                + "On the minimap: hollow ring = still to check, white ring = current target, grey dot = checked, grey cross = skipped (traps cover the whole spot, or it could not be reached for 30s out of combat).\n\n"
                + "Needs BMR's AI and this module switched on; fighting works as usual and walking resumes afterwards. The \"stand on it\" range (1.5y) comes from your recordings, not from game data. Read-only towards PalacePal."));

        ImGui.SameLine();
        // 開始前先勾的只算勾的那一層；換層就清掉（與模式開著時換層關閉同一個時機）
        if (!_hwActive && _hwIgnoreTraps && _hwIgnoreFloor != Palace.Floor)
            _hwIgnoreTraps = false;
        var ignore = _hwIgnoreTraps;
        if (ImGui.Checkbox(Loc.T("DD_HoardWalkIgnoreTraps", "Ignore traps"), ref ignore))
        {
            _hwIgnoreTraps = ignore;
            _hwIgnoreFloor = Palace.Floor;
            _hwDirty = true;
            Service.Logger.Information($"[DD] 試踩寶藏點：無視陷阱 {(ignore ? "開" : "關")}{(_hwActive ? "" : "（還沒開始，開始時套用）")}");
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(Loc.T("DD_HoardWalkIgnoreTooltip",
                "While trying the hoard spots, do not steer around trap locations from the databases (built-in table, PalacePal, your own confirmed traps), and do not skip spots they cover. Traps that are actually visible (for example after a Pomander of Sight) are always avoided.\n\nCan be ticked before starting (it then applies when you start on this floor). Only has an effect while the box on the left is on, and is reset together with it - and when you change floors."));

        ImGui.SameLine();
        if (!_hwActive && _hwRelaxOwnSpot && _hwRelaxFloor != Palace.Floor)
            _hwRelaxOwnSpot = false;
        var relax = _hwRelaxOwnSpot;
        if (ImGui.Checkbox(Loc.T("DD_HoardWalkRelaxOwn", "Only allow the spot itself"), ref relax))
        {
            _hwRelaxOwnSpot = relax;
            _hwRelaxFloor = Palace.Floor;
            _hwDirty = true;
            Service.Logger.Information($"[DD] 試踩寶藏點：只放寬候選點本身 {(relax ? "開" : "關")}{(_hwActive ? "" : "（還沒開始，開始時套用）")}{(_hwIgnoreTraps ? "（無視陷阱開著，暫時沒有作用）" : "")}");
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(Loc.T("DD_HoardWalkRelaxOwnTooltip",
                "Hoard spots sit exactly on trap locations from the databases (a spot can spawn either the hoard or a trap), so without a Pomander of Sight or Safety every spot is covered by its own trap location and skipped.\n\n"
                + "With this on, only the database trap location on the spot itself stops counting - for that spot only. Other database trap locations nearby, and traps that are actually visible, are still avoided as usual.\n\n"
                + "Risk: if that spot holds a trap on this floor (not the hoard), you will step on it.\n\n"
                + "No effect while \"Ignore traps\" is ticked (that already ignores every database trap). Same rules as \"Ignore traps\": can be ticked before starting, is reset with the box on the left and when you change floors, and is never saved."));

        if (_hwActive)
        {
            var counts = HoardWalkCounts();
            if (_hwProgressText == null || counts != _hwProgressKey)
            {
                _hwProgressKey = counts;
                _hwProgressText = string.Format(Loc.T("DD_HoardWalkProgress", "Checked {0} / skipped {1} / total {2}"), counts.Checked, counts.Skipped, counts.Total);
            }
            ImGui.TextUnformatted(_hwProgressText);
            if (HoardWalkSteering)
            {
                var stand = _hwStands[_hwTarget];
                var dwell = 0d;
                var points = CollectionsMarshal.AsSpan(_hwPoints);
                for (var k = 0; k < stand.CoverCount; ++k)
                {
                    ref readonly var p = ref points[_hwCovers[stand.CoverStart + k]];
                    if (p.DwellSince != default)
                        dwell = Math.Max(dwell, (World.CurrentTime - p.DwellSince).TotalSeconds);
                }
                ImGui.SameLine();
                var key = (_hwStandRoom[_hwTarget], stand.CoverCount, (int)(dwell * 10d));
                if (_hwTargetText == null || key != _hwTargetKey)
                {
                    _hwTargetKey = key;
                    _hwTargetText = string.Format(Loc.T("DD_HoardWalkTarget", "- next: room {0}, {1} spot(s), standing {2:f1}s"), key.Item1, key.Item2, key.Item3 / 10d);
                }
                ImGui.TextUnformatted(_hwTargetText);
            }
            if (!Config.Enable)
                ImGui.TextColored(ColorUnknownText, Loc.T("DD_BlockedModuleOff", "The Auto-DeepDungeon module is switched off, so nothing will move."));
            else if (AI.AIManager.Instance?.Beh == null)
                ImGui.TextColored(ColorUnknownText, Loc.T("DD_BlockedAIOff", "Navigation only happens while BMR's AI is running; it is currently off."));
        }
        else if (_hwMessage != null)
        {
            ImGui.TextColored(ColorUnknownText, _hwMessage);
        }
    }

    private void DrawHoardWalkMarkers(Vector2 gridOrigin)
    {
        const float limit = Minimap.CellHalfPixels - 6f;
        var dl = ImGui.GetWindowDrawList();
        var points = CollectionsMarshal.AsSpan(_hwPoints);
        for (var i = 0; i < points.Length; ++i)
        {
            ref readonly var p = ref points[i];
            if ((uint)p.Room >= DeepDungeonState.NumRooms || RoomCenters[p.Room] is not WPos center)
                continue;
            var d = p.Pos - center;
            var off = Vector2.Clamp(new Vector2(d.X * CellPixelsPerYalm, d.Z * CellPixelsPerYalm), new Vector2(-limit), new Vector2(limit));
            var at = gridOrigin + new Vector2(p.Room % 5 * Minimap.CellPixels + Minimap.CellHalfPixels, p.Room / 5 * Minimap.CellPixels + Minimap.CellHalfPixels) + off;

            switch (p.State)
            {
                case HoardWalkPointState.Checked:
                    dl.AddCircleFilled(at, 4f, Colors.Shadows);
                    dl.AddCircleFilled(at, 3f, ColorHoardWalkChecked);
                    break;
                case HoardWalkPointState.Skipped or HoardWalkPointState.GaveUp:
                    const float x = 3.5f;
                    dl.AddLine(at + new Vector2(-x, -x), at + new Vector2(x, x), Colors.Shadows, 3f);
                    dl.AddLine(at + new Vector2(-x, x), at + new Vector2(x, -x), Colors.Shadows, 3f);
                    dl.AddLine(at + new Vector2(-x, -x), at + new Vector2(x, x), ColorHoardWalkChecked, 1.5f);
                    dl.AddLine(at + new Vector2(-x, x), at + new Vector2(x, -x), ColorHoardWalkChecked, 1.5f);
                    break;
                default:
                    dl.AddCircle(at, 4.5f, Colors.Shadows, 0, 3f);
                    dl.AddCircle(at, 4.5f, ColorHoard, 0, 1.6f);
                    break;
            }
        }

        if (!HoardWalkSteering)
            return;
        var stand = _hwStands[_hwTarget];
        for (var k = 0; k < stand.CoverCount; ++k)
        {
            ref readonly var p = ref points[_hwCovers[stand.CoverStart + k]];
            if ((uint)p.Room >= DeepDungeonState.NumRooms || RoomCenters[p.Room] is not WPos center)
                continue;
            var d = p.Pos - center;
            var off = Vector2.Clamp(new Vector2(d.X * CellPixelsPerYalm, d.Z * CellPixelsPerYalm), new Vector2(-limit), new Vector2(limit));
            var at = gridOrigin + new Vector2(p.Room % 5 * Minimap.CellPixels + Minimap.CellHalfPixels, p.Room / 5 * Minimap.CellPixels + Minimap.CellHalfPixels) + off;
            dl.AddCircle(at, 8f, Colors.Shadows, 0, 3.5f);
            dl.AddCircle(at, 8f, ColorHoardWalkTarget, 0, 2f);
        }
    }
}
