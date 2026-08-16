namespace BossMod.Stormblood.Ultimate.UWU;

class P1Plumes(BossModule module) : BossComponent(module)
{
    private readonly List<Actor> _razor = module.Enemies((uint)OID.RazorPlume);
    private readonly List<Actor> _spiny = module.Enemies((uint)OID.SpinyPlume);
    private readonly List<Actor> _satin = module.Enemies((uint)OID.SatinPlume);

    public bool Active => _razor.Any(p => p.IsTargetable) || _spiny.Any(p => p.IsTargetable) || _satin.Any(p => p.IsTargetable);

    public override void DrawArenaForeground(int pcSlot, Actor pc)
    {
        Arena.Actors(_razor);
        Arena.Actors(_spiny);
        Arena.Actors(_satin);
    }
}

// shows shield as a safezone -- this isn't how the mechanic works entirely but is intuitive.
sealed class P1PlumeShield(BossModule module) : BossComponent(module)
{
    private readonly List<Actor> _shield = module.Enemies((uint)OID.SpinyShield);

    public override void DrawArenaForeground(int pcSlot, Actor pc)
    {
        if (_shield.Count != 0)
        {
            var shieldPos = _shield[0].Position;
            Arena.AddCircle(shieldPos, 6f, (pc.Position - shieldPos).LengthSq() <= 36f ? Colors.Safe : Colors.Danger);
        }
    }
}
