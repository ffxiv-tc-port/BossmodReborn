namespace BossMod.AI;

// 到點防過衝：自動移動只給方向、遊戲照全速走，到點那幾幀會走過頭再反向＝原地來回。開啟時到點就停住，目的地離開超過重新起步距離才再走。
// 契約：enabled=false 時恆回 false 且清掉狀態（呼叫端輸出與沒有這個類別時逐位元相同）；主執行緒、每個移動來源一份、每幀呼叫一次；不配置。
// 安全：玩家離任何禁區（不論生效時間）不到 HoldClearance 就不停，與關閉時相同；停住的偏差上限＝重新起步距離。
public sealed class ArrivalHold
{
    public const float HoldClearance = 0.5f;
    public const float MaxFrameSeconds = 0.05f; // 卡頓的一幀不放大提早到點的半徑
    public const int EarlyArrivalFrames = 2; // 方向要隔一兩幀才生效：差兩幀步長就停，滑過去的量才落在重新起步距離內

    private const int Settled = 0, SpeedArrival = 1, Released = 2, SafetyVeto = 3;
    private static readonly MinuteLogDigest _digest = new("[到點防過衝][每分鐘彙總]",
        ["到點停住", "其中靠「兩幀步長」提早視為到點", "目的地移開、重新起步", "身邊有禁區、照舊走（不停住）"],
        [-1, -1, -1, -1], []);

    private WPos? _anchor;
    private bool _active, _vetoLogged;

    /// <param name="baseArriveSq">呼叫端原本的到點門檻（距離平方）：不超過它時呼叫端本來就不走，這裡只記下錨點、回 false。</param>
    /// <param name="releaseDistance">到點之後，目的地要離玩家超過這個距離才重新起步；也是「兩幀步長」提早到點的上限。</param>
    /// <returns>true＝這一幀當作已到點、不往目的地走。</returns>
    public bool ShouldHold(bool enabled, DateTime now, WPos player, WPos destination, float baseArriveSq, float speed, float frameSeconds, float releaseDistance,
        List<(Func<WPos, float> shapeDistance, DateTime activation, ulong Source)> forbiddenZones)
    {
        if (!enabled)
        {
            _anchor = null;
            _active = _vetoLogged = false;
            return false;
        }
        _digest.Tick(now);

        var distSq = (destination - player).LengthSq();
        if (distSq <= baseArriveSq)
        {
            _anchor = destination;
            return false; // 呼叫端自己就不走；只記錨點
        }

        var release = Math.Max(releaseDistance, 0f);
        var step = Math.Min(Math.Max(speed, 0f) * Math.Clamp(frameSeconds, 0f, MaxFrameSeconds) * EarlyArrivalFrames, release);
        var early = distSq <= step * step;
        var near = _anchor is WPos a && (destination - a).LengthSq() <= release * release && distSq <= release * release;
        if (!early && !near)
        {
            if (_active)
                _digest.Record(Released, now);
            _anchor = null;
            _active = _vetoLogged = false;
            return false;
        }

        if (!ClearOfForbiddenZones(player, forbiddenZones))
        {
            if (!_vetoLogged)
                _digest.Record(SafetyVeto, now);
            _vetoLogged = true;
            _anchor = null;
            _active = false;
            return false;
        }
        _vetoLogged = false;

        if (!_active)
        {
            _digest.Record(Settled, now);
            if (!near)
                _digest.Record(SpeedArrival, now);
            _active = true;
        }
        _anchor = destination;
        return true;
    }

    private static bool ClearOfForbiddenZones(WPos player, List<(Func<WPos, float> shapeDistance, DateTime activation, ulong Source)> zones)
    {
        foreach (var z in zones)
            if (!(z.shapeDistance(player) > HoldClearance))
                return false;
        return true;
    }
}
