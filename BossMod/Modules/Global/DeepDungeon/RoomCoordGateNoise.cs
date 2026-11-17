namespace BossMod.Global.DeepDungeon;

/// <summary>
/// 房間座標校驗閘門的兩種「不通過」假象：剛進樓層（還在傳送落點）與房間邊界（遲滯到期）。
/// </summary>
/// <remarks>純函式、不碰遊戲狀態；量測台直接編譯這個檔。</remarks>
internal static class RoomCoordGateNoise
{
    /// <summary>載入本層版面後這麼久（秒）以內、本層還沒通過過的「不通過」不算數。</summary>
    /// <remarks>傳送落點離房中心數百碼，實機從載入到第一次通過最長約 3 秒；先通過就提早結束。</remarks>
    public const double SettleSeconds = 5d;

    /// <summary>還在進樓層的靜置期：這時的「不通過」只代表人還沒落地，不評估。</summary>
    public static bool IsSettling(bool everOkThisFloor, double secondsSinceLoad)
        => !everOkThisFloor && secondsSinceLoad >= 0d && secondsSinceLoad < SettleSeconds;

    /// <summary>本層通過過、遲滯到期、距離仍在邊界量級 ⇒ 過房間邊界的翻轉，不是座標錯。</summary>
    /// <remarks>距離超過兩倍容許誤差就不算邊界翻轉，照「真的不通過」記錄。</remarks>
    public static bool IsBoundaryFlip(bool everOkThisFloor, float distance, float tolerance)
        => everOkThisFloor && distance >= 0f && distance <= tolerance * 2f;

    /// <summary>走廊遲滯的上限（秒）：邊界翻轉而且最近的是回報房的上下左右鄰房時，最多撐這麼久才降級。</summary>
    public const double CorridorHoldSeconds = 30d;

    /// <summary>邊界翻轉、最近的房是回報房本身或其上下左右鄰房（5×5 房號）⇒ 人在兩房之間的走廊，不是座標錯。</summary>
    /// <remarks>只延長「本層已通過過」之後的降級等待（上限 <see cref="CorridorHoldSeconds"/>）；第一次通過的條件不變。</remarks>
    public static bool IsNeighbourCorridor(bool everOkThisFloor, int reportedRoom, int nearestRoom, float distance, float tolerance)
    {
        if (!IsBoundaryFlip(everOkThisFloor, distance, tolerance) || (uint)reportedRoom >= 25u || (uint)nearestRoom >= 25u)
            return false;
        return Math.Abs(reportedRoom / 5 - nearestRoom / 5) + Math.Abs(reportedRoom % 5 - nearestRoom % 5) <= 1;
    }
}
