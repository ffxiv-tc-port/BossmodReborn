namespace BossMod.Global.DeepDungeon;

// IPC BossMod.DeepDungeon.IsPullSafe（Func<ulong, bool>）的快照。名稱與型別是 WrathCombo 消費端的契約，不可改。
// 契約：框架執行緒每幀由 AutoClear 重算並發佈；端點可從任何執行緒呼叫，只讀這份不可變快照、不碰 WorldState、不阻塞。
// 不在深宮、快照過期、查不到、目標已在戰鬥中 ⇒ true（照舊可以打）。
public static class DeepDungeonPullSafety
{
    // 目標周圍會一起被帶上的沒仇恨怪達到這個數就算不安全（含目標＝3 隻）。
    public const int UnsafeCompanions = 2;

    private const long StaleMs = 2000;
    private const long LogIntervalMs = 10000;

    public sealed record class Entry(ulong Id, string Name, int Floor, int Companions, ulong CompanionKey, string Detail);

    private static volatile Entry[] _unsafe = [];
    private static long _publishedAtMs;
    private static readonly Dictionary<ulong, long> _lastLogMs = [];

    public static bool IsPullSafe(ulong gameObjectId)
    {
        try
        {
            var snap = _unsafe;
            if (gameObjectId == 0 || snap.Length == 0)
                return true;
            if (Environment.TickCount64 - System.Threading.Interlocked.Read(ref _publishedAtMs) > StaleMs)
                return true;
            foreach (var e in snap)
            {
                if (e.Id == gameObjectId)
                {
                    LogUnsafe(e);
                    return false;
                }
            }
            return true;
        }
        catch
        {
            return true;
        }
    }

    internal static Entry[] Current => _unsafe;

    internal static void Publish(Entry[] entries)
    {
        _unsafe = entries;
        System.Threading.Interlocked.Exchange(ref _publishedAtMs, Environment.TickCount64);
    }

    internal static void Clear() => _unsafe = [];

    private static void LogUnsafe(Entry e)
    {
        var now = Environment.TickCount64;
        lock (_lastLogMs)
        {
            if (_lastLogMs.TryGetValue(e.Id, out var last) && now - last < LogIntervalMs)
                return;
            _lastLogMs[e.Id] = now;
            if (_lastLogMs.Count > 256)
                _lastLogMs.Clear();
        }
        Service.Logger.Information($"[DD] 拉怪安全：樓層 {e.Floor}，拉「{e.Name}」（{e.Id:X}）會一起帶上 {e.Companions} 隻沒仇恨的怪（{e.Detail}），回報不安全");
    }
}

public abstract partial class AutoClear
{
    private const float PullSafetyFallbackRadius = 10f;
    private const float PullSafetyCandidateRange = 50f;

    private readonly List<Actor> _pullSleeping = [];
    private readonly List<(Actor Target, int Count, ulong Key)> _pullUnsafe = [];
    private readonly List<string> _pullNames = [];

    // 一隻沒仇恨的怪 M 會被一起帶上＝玩家位置或目標位置落在 M 的偵測圓內（hitbox＋仇恨迴避半徑，死者宮殿擬態怪另加）。
    // 刻意用整圓而不是視覺怪的面向扇形：開打之後人會移動、風箏、轉向，開打當下的朝向預測不了。
    private void PublishPullSafety(Actor player)
    {
        if (Palace.IsBossFloor || BetweenFloors)
        {
            DeepDungeonPullSafety.Publish([]);
            return;
        }

        var radius = Config.AggroAvoidRadius > 0f ? Config.AggroAvoidRadius : PullSafetyFallbackRadius;
        var inPotd = Palace.DungeonId == DeepDungeonState.DungeonType.POTD;
        _pullSleeping.Clear();
        foreach (var a in World.Actors.Actors.Values)
        {
            if (a.Type != ActorType.Enemy || a.IsAlly || !a.IsTargetable || a.IsDeadOrDestroyed || a.AggroPlayer || a.InCombat || a.TargetID == player.InstanceID)
                continue;
            _pullSleeping.Add(a);
        }

        _pullUnsafe.Clear();
        var pp = player.Position;
        var n = _pullSleeping.Count;
        for (var i = 0; i < n; ++i)
        {
            var t = _pullSleeping[i];
            if ((t.Position - pp).LengthSq() > PullSafetyCandidateRange * PullSafetyCandidateRange)
                continue;
            var count = 0;
            var key = 0ul;
            for (var j = 0; j < n; ++j)
            {
                if (j == i)
                    continue;
                var m = _pullSleeping[j];
                var r = m.HitboxRadius + (inPotd && IsMimicOid(m.OID) ? radius + MimicExtraRadius : radius);
                if ((m.Position - pp).LengthSq() <= r * r || (m.Position - t.Position).Length() <= r + t.HitboxRadius)
                {
                    ++count;
                    key = key * 31 + m.InstanceID;
                }
            }
            if (count >= DeepDungeonPullSafety.UnsafeCompanions)
                _pullUnsafe.Add((t, count, key));
        }

        var cur = DeepDungeonPullSafety.Current;
        var same = cur.Length == _pullUnsafe.Count;
        for (var i = 0; same && i < cur.Length; ++i)
            same = cur[i].Id == _pullUnsafe[i].Target.InstanceID && cur[i].Companions == _pullUnsafe[i].Count && cur[i].CompanionKey == _pullUnsafe[i].Key && cur[i].Floor == Palace.Floor;
        if (same)
        {
            DeepDungeonPullSafety.Publish(cur);
            return;
        }

        var next = new DeepDungeonPullSafety.Entry[_pullUnsafe.Count];
        for (var i = 0; i < next.Length; ++i)
        {
            var (t, count, key) = _pullUnsafe[i];
            _pullNames.Clear();
            for (var j = 0; j < n; ++j)
            {
                var m = _pullSleeping[j];
                if (m == t)
                    continue;
                var r = m.HitboxRadius + (inPotd && IsMimicOid(m.OID) ? radius + MimicExtraRadius : radius);
                if ((m.Position - pp).LengthSq() <= r * r || (m.Position - t.Position).Length() <= r + t.HitboxRadius)
                    _pullNames.Add($"{m.Name} {m.InstanceID:X}");
            }
            next[i] = new(t.InstanceID, t.Name, Palace.Floor, count, key, string.Join("、", _pullNames));
        }
        DeepDungeonPullSafety.Publish(next);
    }
}
