namespace BossMod;

// clipping shapes to bounds and triangulating them is a serious time sink, so we want to cache that
// to avoid requiring tracking cache lifetime by users, we use a heuristic - we assume that if something isn't drawn for a frame, it's no longer relevant
public sealed class TriangulationCache
{
    private class CacheEntry(List<RelTriangle>? triangulation)
    {
        public List<RelTriangle>? Triangulation = triangulation;
        public bool IsCurrent = true;
    }

    private readonly Dictionary<int, CacheEntry> _cache = [];
    private readonly List<int> _keysToRemove = [];

    public static int GetKeyHash(int keyType, params object[] keyParts)
    {
        var hash = keyType;
        var len = keyParts.Length;
        for (var i = 0; i < len; ++i)
        {
            hash = hash * 31 + keyParts[i].GetHashCode();
        }
        return hash;
    }

    // 與上面 params 版逐一同值（同一條 hash * 31 + GetHashCode() 公式），只是不配陣列、不裝箱。
    public static int GetKeyHash<T1>(int keyType, T1 a) where T1 : notnull
        => keyType * 31 + a.GetHashCode();
    public static int GetKeyHash<T1, T2>(int keyType, T1 a, T2 b) where T1 : notnull where T2 : notnull
        => GetKeyHash(keyType, a) * 31 + b.GetHashCode();
    public static int GetKeyHash<T1, T2, T3>(int keyType, T1 a, T2 b, T3 c) where T1 : notnull where T2 : notnull where T3 : notnull
        => GetKeyHash(keyType, a, b) * 31 + c.GetHashCode();
    public static int GetKeyHash<T1, T2, T3, T4>(int keyType, T1 a, T2 b, T3 c, T4 d) where T1 : notnull where T2 : notnull where T3 : notnull where T4 : notnull
        => GetKeyHash(keyType, a, b, c) * 31 + d.GetHashCode();
    public static int GetKeyHash<T1, T2, T3, T4, T5>(int keyType, T1 a, T2 b, T3 c, T4 d, T5 e) where T1 : notnull where T2 : notnull where T3 : notnull where T4 : notnull where T5 : notnull
        => GetKeyHash(keyType, a, b, c, d) * 31 + e.GetHashCode();

    public ref List<RelTriangle>? this[int keyType, params object[] keyParts]
    {
        get
        {
            var keyHash = GetKeyHash(keyType, keyParts);
            if (!_cache.TryGetValue(keyHash, out var entry))
            {
                entry = new CacheEntry(null);
                _cache[keyHash] = entry;
                return ref entry.Triangulation;
            }

            entry.IsCurrent = true;
            return ref entry.Triangulation;
        }
    }

    public void NextFrame()
    {
        if (_cache.Count == 0)
            return;
        var keysToRemove = _keysToRemove;
        keysToRemove.Clear();

        foreach (var kvp in _cache)
        {
            if (!kvp.Value.IsCurrent)
                keysToRemove.Add(kvp.Key);
            else
                kvp.Value.IsCurrent = false;
        }
        var count = keysToRemove.Count;
        for (var i = 0; i < count; ++i)
            if (_cache.Remove(keysToRemove[i], out var removed) && removed.Triangulation != null)
                ArenaBounds.RecycleTriangulation(removed.Triangulation); // 結果只放進過這個快取，淘汰後沒有人再讀
    }

    public void Invalidate()
    {
        _cache.Clear();
    }
}
