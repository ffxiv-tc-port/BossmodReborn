namespace BossMod;

/// <summary>
/// 一個呼叫點專用的目標區委派快取：參數與上一次逐位元相同就交回上一次的委派，不同才新建。
/// </summary>
/// <remarks>
/// 契約：只給同一條執行緒用（區域模組、循環模組、AI 前半段都在主執行緒），不加鎖。
/// 交出去的委派只讀建立時捕獲的值，所以沿用與新建在每一點算出來的值逐位元相同；
/// 背景尋路拿著上一幀的同一個委派也沒關係（委派不可變）。
/// </remarks>
public sealed class GoalMemo
{
    private Func<WPos, float>? _fn;
    private int _kind;
    private uint _x, _z, _a, _b, _c;

    // 沒中就先清掉舊委派再記新鍵：建委派的那一步擲例外時不會把舊委派配上新鍵
    private bool Hit(int kind, WPos p, float a, float b, float c)
    {
        var x = BitConverter.SingleToUInt32Bits(p.X);
        var z = BitConverter.SingleToUInt32Bits(p.Z);
        var ua = BitConverter.SingleToUInt32Bits(a);
        var ub = BitConverter.SingleToUInt32Bits(b);
        var uc = BitConverter.SingleToUInt32Bits(c);
        if (_fn != null && _kind == kind && _x == x && _z == z && _a == ua && _b == ub && _c == uc)
            return true;
        _fn = null;
        (_kind, _x, _z, _a, _b, _c) = (kind, x, z, ua, ub, uc);
        return false;
    }

    /// <summary>等同 <see cref="AIHints.GoalSingleTarget(WPos, float, float)"/>。</summary>
    public Func<WPos, float> SingleTarget(AIHints hints, WPos target, float radius, float weight = 1f)
        => Hit(1, target, radius, weight, default) ? _fn! : _fn = hints.GoalSingleTarget(target, radius, weight);

    /// <summary>等同 <see cref="AIHints.GoalSingleTarget(Actor, float, float)"/>。</summary>
    public Func<WPos, float> SingleTarget(AIHints hints, Actor target, float range, float weight = 1f)
        => SingleTarget(hints, target.Position, range + target.HitboxRadius, weight);

    /// <summary>等同 <see cref="AIHints.GoalSingleTarget(Actor, Positional, float, float)"/>（Any 與其他方位各自走原本的那一支）。</summary>
    public Func<WPos, float> SingleTarget(AIHints hints, Actor target, Positional positional, float range = 2.6f, float cushion = 0f)
    {
        if (positional == Positional.Any)
            return SingleTarget(hints, target.Position, range + target.HitboxRadius);
        return Hit(2 + (int)positional, target.Position, target.Rotation.Rad, range + target.HitboxRadius, cushion) ? _fn! : _fn = hints.GoalSingleTarget(target, positional, range, cushion);
    }

    /// <summary>任意「位置＋兩個參數」的目標區；<paramref name="factory"/> 必須只依這三個值建委派（每個呼叫點固定同一支）。</summary>
    public Func<WPos, float> Get(WPos p, float a, float b, Func<WPos, float, float, Func<WPos, float>> factory)
        => Hit(0, p, a, b, default) ? _fn! : _fn = factory(p, a, b);
}
