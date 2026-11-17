namespace BossMod.AI;

// 自動移動「原地來回」分型診斷：純觀察、不改任何移動輸出；每分鐘彙總一行 Information（沒發生就不印）。
// 契約：只在主執行緒、每幀最多呼叫一次 Update；不配置（只有抖動段結束時組一次說明字串）。
// 分型：決策翻轉＝輸出方向相反（兩邊都離目的地 >1 碼）；位置來回再依當下狀況分成剛翻轉／決策穩定（撞牆等）／目的地 1 碼內（逐幀過衝）／沒有自動輸出。
public sealed class MovementJitterLog
{
    private const int FlipCombat = 0, FlipIdle = 1, RevAfterFlip = 2, RevSteady = 3, RevNearDest = 4, RevNoOutput = 5, SegCombat = 6, SegIdle = 7;
    private const float MinStep = 0.02f; // 一幀位移小於這個值不算「在動」（站著時的座標抖動）
    private const float MaxStep = 3f; // 一幀位移大於這個值＝傳送／換區，重新起算
    private const float FlipMinDistance = 1f;
    private const double PairWindow = 0.6d; // 反轉要和 0.6 秒內上一步比；決策翻轉 0.6 秒內的來回算「剛翻轉」
    private const double SegmentWindow = 2.5d; // 2.5 秒內 3 次來回＝一段抖動
    private const float SegmentRadius = 3f;

    private static readonly MinuteLogDigest _digest = new("[閃避抖動][每分鐘彙總]",
        ["決策方向反轉（交戰）", "決策方向反轉（脫戰）", "位置來回・決策剛翻轉", "位置來回・決策穩定（撞牆型）", "位置來回・目的地 1 碼內（逐幀過衝型）", "位置來回・沒有自動移動輸出", "抖動段（交戰）", "抖動段（脫戰）"],
        [-1, -1, -1, -1, -1, -1, -1, -1], []);

    private bool _havePos;
    private WPos _lastPos;
    private WDir _lastStep;
    private DateTime _lastStepTime;
    private WDir _lastOutput;
    private DateTime _lastOutputTime, _lastFlipTime;
    // 最近三次來回（環狀）：時間、位置、分型
    private readonly DateTime[] _revTime = new DateTime[3];
    private readonly WPos[] _revPos = new WPos[3];
    private readonly int[] _revKind = new int[3];
    private int _revNext;
    // 進行中的抖動段
    private bool _segOpen, _segCombat;
    private DateTime _segStart, _segLast;
    private WPos _segPos;
    private readonly int[] _segKinds = new int[4];

    /// <param name="output">這一幀交給移動覆寫的方向（xz＝玩家到目的地的向量）；null／零＝沒有自動移動。</param>
    /// <param name="outputSuppressed">使用者正在手動移動、按著暫停鍵、或讓位給 vnav 路徑＝自動輸出這一幀不生效。</param>
    public void Update(DateTime now, WPos position, bool inCombat, Vector3? output, bool outputSuppressed)
    {
        if (_segOpen && (now - _segLast).TotalSeconds > SegmentWindow)
            CloseSegment();
        _digest.Tick(now);

        var outDir = !outputSuppressed && output is { } o ? new WDir(o.X, o.Z) : default;
        var hasOutput = outDir != default;
        if (hasOutput)
        {
            if (_lastOutputTime != default && (now - _lastOutputTime).TotalSeconds <= PairWindow
                && outDir.LengthSq() > FlipMinDistance * FlipMinDistance && _lastOutput.LengthSq() > FlipMinDistance * FlipMinDistance && outDir.Dot(_lastOutput) < 0f)
            {
                _lastFlipTime = now;
                _digest.Record(inCombat ? FlipCombat : FlipIdle, now);
            }
            _lastOutput = outDir;
            _lastOutputTime = now;
        }

        if (!_havePos)
        {
            _havePos = true;
            _lastPos = position;
            return;
        }
        var step = position - _lastPos;
        var len = step.Length();
        if (len > MaxStep)
        {
            _lastPos = position;
            _lastStepTime = default;
            return;
        }
        if (len < MinStep)
            return; // 不更新 _lastPos：慢速移動累積到門檻才算一步
        _lastPos = position;
        if (_lastStepTime != default && (now - _lastStepTime).TotalSeconds <= PairWindow && step.Dot(_lastStep) < -0.5f * len * _lastStep.Length())
        {
            var kind = !hasOutput ? RevNoOutput
                : outDir.LengthSq() <= FlipMinDistance * FlipMinDistance ? RevNearDest
                : _lastFlipTime != default && (now - _lastFlipTime).TotalSeconds <= PairWindow ? RevAfterFlip
                : RevSteady;
            _digest.Record(kind, now);
            OnReversal(now, position, inCombat, kind);
        }
        _lastStep = step;
        _lastStepTime = now;
    }

    /// <summary>自動移動沒在跑：清掉狀態，下次重新起算（進行中的抖動段照樣結算）。</summary>
    public void Reset(DateTime now)
    {
        _digest.Tick(now);
        if (!_havePos && !_segOpen)
            return;
        if (_segOpen)
            CloseSegment();
        _havePos = false;
        _lastStepTime = _lastOutputTime = _lastFlipTime = default;
        Array.Clear(_revTime);
    }

    private void OnReversal(DateTime now, WPos position, bool inCombat, int kind)
    {
        if (_segOpen)
        {
            _segLast = now;
            ++_segKinds[kind - RevAfterFlip];
            return;
        }
        _revTime[_revNext] = now;
        _revPos[_revNext] = position;
        _revKind[_revNext] = kind;
        _revNext = (_revNext + 1) % 3;
        var oldest = _revNext; // 環狀：下一個要寫的位置就是最舊的
        if (_revTime[oldest] == default || (now - _revTime[oldest]).TotalSeconds > SegmentWindow)
            return;
        for (var i = 0; i < 3; ++i)
            if ((_revPos[i] - position).LengthSq() > SegmentRadius * SegmentRadius)
                return;
        _segOpen = true;
        _segCombat = inCombat;
        _segStart = _revTime[oldest];
        _segLast = now;
        _segPos = position;
        Array.Clear(_segKinds);
        for (var i = 0; i < 3; ++i)
            ++_segKinds[_revKind[i] - RevAfterFlip];
        Array.Clear(_revTime);
    }

    private void CloseSegment()
    {
        _segOpen = false;
        var n = _segKinds[0] + _segKinds[1] + _segKinds[2] + _segKinds[3];
        _digest.Record(_segCombat ? SegCombat : SegIdle, _segLast,
            $"最近一段抖動：{_segStart:HH:mm:ss} 起 {(_segLast - _segStart).TotalSeconds:f1} 秒、位置 ({_segPos.X:f1},{_segPos.Z:f1})、{(_segCombat ? "交戰" : "脫戰")}、來回 {n} 次（決策剛翻轉 {_segKinds[0]}／決策穩定 {_segKinds[1]}／目的地 1 碼內 {_segKinds[2]}／沒有自動輸出 {_segKinds[3]}）");
    }
}
