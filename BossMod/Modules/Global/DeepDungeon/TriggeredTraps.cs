namespace BossMod.Global.DeepDungeon;

// 本檔假設與契約：
// - 陷阱一層只會觸發一次；觸發當下施放者（陷阱 BattleChara）已移到真正的陷阱位置。
// - 事件與所有讀取點都在主執行緒（WorldState 更新與 AI 提示建構），不加鎖。
// - 只存座標，換層清空、不存檔；開關關著時 IsTriggeredTrap 恆為 false，與沒有這個功能時相同。
public abstract partial class AutoClear
{
    /// <summary>陷阱觸發時的動作（與 PalacePal 踩到陷阱的判定同一組）；寶箱爆炸 6284、武器／防具強化 6291／6292 的施放者也叫「陷阱」，不在這裡。</summary>
    private static readonly HashSet<uint> TriggeredTrapActionIds = [6275, 6276, 6277, 6278, 6279, 11287, 32375, 44038];

    /// <summary>觸發位置周圍這麼近的資料庫點視為同一個陷阱：同一陷阱在各來源的紀錄差不到 1y，同一層兩個真陷阱相距遠大於此。</summary>
    private const float TriggeredTrapRadius = TrapCircleRadius;

    /// <summary>這一層已經觸發過的陷阱位置。</summary>
    private readonly List<WPos> _triggeredTraps = [];

    private bool IsTriggeredTrap(WPos trap)
    {
        if (_triggeredTraps.Count == 0 || !Config.SkipTriggeredTraps)
            return false;
        foreach (var t in _triggeredTraps)
            if (trap.InCircle(t, TriggeredTrapRadius))
                return true;
        return false;
    }

    private void OnTrapCastEvent(Actor caster, ActorCastEvent ev)
    {
        if (ev.Action.Type != ActionType.Spell || !TriggeredTrapActionIds.Contains(ev.Action.ID) || Palace.DungeonId == DeepDungeonState.DungeonType.None)
            return;
        var pos = caster.Position;
        // 施放者還沒移過去時座標是原點；地雷／妨礙／誘餌的目標座標就是陷阱位置，其餘是原點
        if (AtOrigin(pos))
            pos = ev.TargetXZ;
        if (AtOrigin(pos))
        {
            Service.Logger.Information($"[DD] 踩到陷阱：{ev.Action}，第 {Palace.Progress.Floor} 層、區域 {World.CurrentZone}，但施放者與目標都沒有座標，什麼都不壓");
            return;
        }
        _triggeredTraps.Add(pos);
        var builtIn = CountNear(_trapsCurrentZone, pos);
        var pal = CountNear(_palTraps, pos);
        var confirmed = CountNear(_palConfirmedTraps, pos);
        var total = builtIn + pal + confirmed;
        var where = $"[DD] 踩到陷阱：{ev.Action}，位置 ({pos.X:f2}, {pos.Z:f2})、第 {Palace.Progress.Floor} 層、區域 {World.CurrentZone}；";
        var sources = $"{total} 點（內建表 {builtIn}、PalacePal 去重後 {pal}、我方確認 {confirmed}）";
        var what = total == 0
            ? $"資料庫裡 {TriggeredTrapRadius}y 內沒有任何陷阱點，沒有可壓的"
            : Config.SkipTriggeredTraps
                ? $"這一層不再閃避 {TriggeredTrapRadius}y 內的 {sources}"
                : $"{TriggeredTrapRadius}y 內有 {sources}，但「踩過的陷阱本層不再閃避」關著，照舊閃避";
        Service.Logger.Information(where + what);
        LogTrapTriggerContext(pos);
    }

    private enum TrapZoneDiag : byte { NotReached, Off, Revealed, HoardWalkIgnore, Added }

    // 上一幀 CalculateAIHints 的快照，只給踩到陷阱那一刻的診斷用：事件在 WorldState 更新時觸發，早於下一次提示重建，
    // 所以 _trapCircles 與 hints 裡的禁區都還是「踩下去前那一幀」的內容。全部在主執行緒。
    private TrapZoneDiag _diagTrapZones;
    private WPos _diagPlayerPos;
    private AIHints? _diagHints;

    private void RecordTrapDiagFrame(Actor player, AIHints hints)
    {
        _diagHints = hints;
        _diagPlayerPos = player.Position;
        _diagTrapZones = TrapZoneDiag.NotReached;
    }

    private void LogTrapTriggerContext(WPos trap)
    {
        var start = _diagPlayerPos;
        var sb = new StringBuilder($"[DD] 踩到陷阱的前一幀：玩家在 ({start.X:f1}, {start.Z:f1})、離觸發點 {(start - trap).Length():f1}y；");
        if (_diagTrapZones == TrapZoneDiag.Added)
        {
            var covered = 0;
            var startIn = 0;
            var nearestDist = float.MaxValue;
            var nearestRadius = 0f;
            foreach (var (c, r) in _trapCircles)
            {
                var d = (c - trap).Length();
                if (d <= r)
                    ++covered;
                if (start.InCircle(c, r))
                    ++startIn;
                if (d < nearestDist)
                    (nearestDist, nearestRadius) = (d, r);
            }
            sb.Append($"陷阱禁區 {_trapCircles.Count} 圈，觸發點{(covered > 0 ? $"在其中 {covered} 圈內" : "不在任何一圈內")}");
            sb.Append(nearestDist == float.MaxValue ? "" : $"（最近的圈心離觸發點 {nearestDist:f1}y、半徑 {nearestRadius:f1}y）");
            sb.Append($"；起點格{(startIn > 0 ? $"在 {startIn} 個陷阱圈內（危險格）" : "不在陷阱圈內")}；");
        }
        else
        {
            sb.Append(_diagTrapZones switch
            {
                TrapZoneDiag.Off => "陷阱禁區沒加（「陷阱提示」關著）；",
                TrapZoneDiag.Revealed => "陷阱禁區沒加（本層陷阱已現形，只閃現形的陷阱物件）；",
                TrapZoneDiag.HoardWalkIgnore => "陷阱禁區沒加（試踩寶藏點、無視陷阱中）；",
                _ => "陷阱禁區那一段沒跑到（Boss 層、換層中或模組關著）；",
            });
        }

        var zones = _diagHints?.ForbiddenZones;
        int zonesAtTrap = 0, zonesAtStart = 0, zoneCount = zones?.Count ?? 0;
        for (var i = 0; i < zoneCount; ++i)
        {
            var f = zones![i].shapeDistance;
            if (f(trap) <= 0f)
                ++zonesAtTrap;
            if (f(start) <= 0f)
                ++zonesAtStart;
        }
        sb.Append($"全部禁區 {zoneCount} 個：蓋住觸發點 {zonesAtTrap} 個、蓋住起點 {zonesAtStart} 個；");

        var ownsModule = BossMod.Autorotation.MiscAI.NormalMovement.OwnsMovement;
        var vnav = DeepDungeonNav.IsPathRunning();
        var ai = BossMod.AI.AIManager.Instance?.IsEnabled == true;
        sb.Append("移動權：").Append(vnav ? "vnavmesh 路徑進行中" : ownsModule ? "BMR 自動移動模組" : ai ? "BMR AI" : "沒有自動移動（玩家自己或別的插件）");
        sb.Append($"、趕路目標區={(_travelGoalAdded ? "有" : "無")}；");

        var ctx = ownsModule ? BossMod.Autorotation.MiscAI.NormalMovement.NavigationContext : BossMod.AI.AIManager.Instance?.Beh?.NavigationContext;
        if (ctx?.LastDestination is WPos dest)
        {
            var crossed = 0;
            foreach (var (c, r) in _trapCircles)
                if (_diagTrapZones == TrapZoneDiag.Added && SegmentDistance(start, dest, c) <= r)
                    ++crossed;
            var destIn = 0;
            for (var i = 0; i < zoneCount; ++i)
                if (zones![i].shapeDistance(dest) <= 0f)
                    ++destIn;
            sb.Append($"BMR 尋路上次的下一個路點 ({dest.X:f1}, {dest.Z:f1})，在 {destIn} 個禁區裡；起點到它的直線穿過 {crossed} 個陷阱圈、離觸發點最近 {SegmentDistance(start, dest, trap):f1}y。");
        }
        else
            sb.Append("BMR 尋路沒有下一個路點。");
        Service.Logger.Information(sb.ToString());
    }

    private static float SegmentDistance(WPos a, WPos b, WPos p)
    {
        var ab = b - a;
        var lsq = ab.LengthSq();
        var t = lsq > 0f ? Math.Clamp((p - a).Dot(ab) / lsq, 0f, 1f) : 0f;
        return (p - (a + ab * t)).Length();
    }

    private static bool AtOrigin(WPos p) => p.X * p.X + p.Z * p.Z < 0.01f;

    private static int CountNear(WPos[] source, WPos pos)
    {
        var n = 0;
        foreach (var p in source)
            if (p.InCircle(pos, TriggeredTrapRadius))
                ++n;
        return n;
    }
}
