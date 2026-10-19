namespace BossMod.Dawntrail.Alliance.A22UltimaOmega;

sealed class GuidedMissile(BossModule module) : Components.SimpleAOEs(module, (uint)AID.GuidedMissile, 6f);

// circle that lands at a fixed offset from the baiting player, since GenericBaitAway in this fork has no per-bait offset
sealed record class AOEShapeOffsetCircle(float Radius, WDir Offset) : AOEShape
{
    public override bool Check(WPos position, WPos origin, Angle rotation) => position.InCircle(origin + Offset, Radius);
    public override void Draw(MiniArena arena, WPos origin, Angle rotation, uint color = default) => arena.ZoneCircle(origin + Offset, Radius, color);
    public override void Outline(MiniArena arena, WPos origin, Angle rotation, uint color = default, float thickness = 1f) => arena.AddCircle(origin + Offset, Radius, color, thickness);
    public override Func<WPos, float> Distance(WPos origin, Angle rotation) => ShapeDistance.Circle(origin + Offset, Radius);
    public override Func<WPos, float> InvertedDistance(WPos origin, Angle rotation) => ShapeDistance.InvertedCircle(origin + Offset, Radius);
}

sealed class TrajectoryProjection(BossModule module) : Components.GenericBaitAway(module, centerAtTarget: true)
{
    private DateTime activation;
    private static readonly AOEShapeOffsetCircle circleEast = new(6f, new(5f, default)), circleWest = new(6f, new(-5f, default)),
    circleSouth = new(6f, new(default, 5f)), circleNorth = new(6f, new(default, -5f));

    public override void OnEventIcon(Actor actor, uint iconID, ulong targetID)
    {
        switch (iconID)
        {
            case (uint)IconID.GuidedMissileEast:
                AddBait(circleEast);
                break;
            case (uint)IconID.GuidedMissileWest:
                AddBait(circleWest);
                break;
            case (uint)IconID.GuidedMissileSouth:
                AddBait(circleSouth);
                break;
            case (uint)IconID.GuidedMissileNorth:
                AddBait(circleNorth);
                break;
        }
        void AddBait(AOEShapeOffsetCircle shape) => CurrentBaits.Add(new(Module.PrimaryActor, actor, shape, activation));
    }

    public override void AddAIHints(int slot, Actor actor, PartyRolesConfig.Assignment assignment, AIHints hints)
    {
        base.AddAIHints(slot, actor, assignment, hints);
        // the base class only knows plain shapes for the baiter's own hints, so keep the offset missile away from everyone else here
        var baits = CollectionsMarshal.AsSpan(CurrentBaits);
        var len = baits.Length;
        var id = actor.InstanceID;
        for (var i = 0; i < len; ++i)
        {
            ref var b = ref baits[i];
            if (b.Target.InstanceID != id || b.Shape is not AOEShapeOffsetCircle shape)
            {
                continue;
            }
            var raid = Raid.WithoutSlot();
            var lenR = raid.Length;
            for (var j = 0; j < lenR; ++j)
            {
                var a = raid[j];
                if (a.InstanceID != id)
                {
                    hints.AddForbiddenZone(ShapeDistance.Circle(a.Position - shape.Offset, shape.Radius), b.Activation);
                }
            }
        }
    }

    public override void OnCastStarted(Actor caster, ActorCastInfo spell)
    {
        switch (spell.Action.ID)
        {
            case (uint)AID.TrajectoryProjection:
                activation = Module.CastFinishAt(spell, 9.8d);
                break;
            case (uint)AID.GuidedMissile:
                CurrentBaits.Clear();
                break;
        }
    }
}
