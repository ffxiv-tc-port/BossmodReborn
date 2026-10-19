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
