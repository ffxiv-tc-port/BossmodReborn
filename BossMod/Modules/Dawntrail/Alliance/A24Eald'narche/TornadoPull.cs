namespace BossMod.Dawntrail.Alliance.A24Ealdnarche;

sealed class TornadoPull(BossModule module) : Components.SimpleKnockbacks(module, (uint)AID.TornadoPull, 16f, minDistance: 1f, kind: Kind.TowardsOrigin, stopAfterWall: true)
{
    private readonly TornadoFlareBurst _aoe = module.FindComponent<TornadoFlareBurst>()!;
    private readonly ArenaChanges _arena = module.FindComponent<ArenaChanges>()!;

    public override void AddAIHints(int slot, Actor actor, PartyRolesConfig.Assignment assignment, AIHints hints)
    {
        if (Casters.Count != 0)
        {
            ref var c = ref Casters.Ref(0);
            var act = c.Activation;
            if (!IsImmune(slot, act))
            {
                var aoes = CollectionsMarshal.AsSpan(_aoe.Casters);
                var len = aoes.Length;
                var origins = new WPos[len];
                for (var i = 0; i < len; ++i)
                {
                    ref var aoe = ref aoes[i];
                    origins[i] = aoe.Origin;
                }
                var center = c.Origin;
                var arenaTile = _arena.RemovedTiles.Count != 0 ? _arena.RemovedTiles[0] : 0;
                var centerTile = new WPos(784f + arenaTile % 3 * 16f, -816f + arenaTile / 3 * 16f);
                hints.AddForbiddenZone(p =>
                {
                    var offsetCenter = p - center;
                    var length = offsetCenter.Length();
                    var lengthAdj = length > 16f ? 16f : length;
                    var offsetSource = length > 0f ? -offsetCenter / length : default;
                    var projected = lengthAdj * offsetSource;
                    for (var i = 0; i < len; ++i)
                    {
                        if ((p + projected).InCircle(origins[i], 6f))  // pretend circles are 1y bigger than real for less suspect knockbacks
                        {
                            return default;
                        }
                    }

                    if (Intersect.RayAABB(centerTile - center, offsetSource, 9f, 9f) > lengthAdj) // pull must not intersect missing tiles...
                    {
                        return 1f;
                    }
                    return default;
                }, act);
            }
        }
    }

    public override bool DestinationUnsafe(int slot, Actor actor, WPos pos)
    {
        var aoes = CollectionsMarshal.AsSpan(_aoe.Casters);
        var len = aoes.Length;
        for (var i = 0; i < len; ++i)
        {
            ref var aoe = ref aoes[i];
            if (aoe.Check(pos))
            {
                return true;
            }
        }
        return !Module.InBounds(pos);
    }
}
