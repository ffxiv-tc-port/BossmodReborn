namespace BossMod.Endwalker.Raid.P12NAthena;

// 女神的護佑跳躍（王不可選取的小怪階段）：點名標記 139 約 5.3 秒後王跳到被點名者（圓 6），途中衝擊波打到王與被點名者之間的人（寬 4）。
// 標記 139 也用在對話的分散（王可選取時，由 DialogosSpread 處理），所以只收王不可選取時的標記；最後一個標記可能不跳，逾時就清。
sealed class PalladionJump(BossModule module) : Components.UniformStackSpread(module, default, 6f, alwaysShowSpreads: true, raidwideOnResolve: false)
{
    public override void OnEventIcon(Actor actor, uint iconID, ulong targetID)
    {
        if (iconID == (uint)IconID.Spread && !Module.PrimaryActor.IsTargetable)
            AddSpread(actor, WorldState.FutureTime(5.3d));
    }

    public override void OnEventCast(Actor caster, ActorCastEvent spell)
    {
        if (spell.Action.ID != (uint)AID.PalladionJump || Spreads.Count == 0)
            return;
        var targets = spell.Targets;
        for (var j = 0; j < targets.Count; ++j)
        {
            for (var i = 0; i < Spreads.Count; ++i)
            {
                if (Spreads[i].Target.InstanceID == targets[j].ID)
                {
                    Spreads.RemoveAt(i);
                    return;
                }
            }
        }
        Spreads.RemoveAt(0);
    }

    public override void Update()
    {
        var expire = WorldState.CurrentTime.AddSeconds(-1.5d);
        for (var i = Spreads.Count - 1; i >= 0; --i)
        {
            if (Spreads[i].Activation < expire || Spreads[i].Target.IsDead)
                Spreads.RemoveAt(i);
        }
    }
}

// 只畫下一跳：起點＝上一跳落點（第一跳＝王當下位置），終點＝被點名者當下位置；被點名者自己躲不掉，不畫給他。
sealed class PalladionShockwave(BossModule module) : Components.GenericAOEs(module, (uint)AID.PalladionShockwave)
{
    private readonly List<(Actor Target, DateTime Activation)> _jumps = [];
    private readonly AOEInstance[] _aoe = new AOEInstance[1];
    private AOEShapeRect _shape = new(1f, 2f);
    private WPos? _origin;

    public override ReadOnlySpan<AOEInstance> ActiveAOEs(int slot, Actor actor)
    {
        if (_jumps.Count == 0)
            return [];
        var (target, activation) = _jumps[0];
        if (target == actor)
            return [];
        var origin = _origin ?? Module.PrimaryActor.Position;
        var dir = target.Position - origin;
        var length = dir.Length();
        if (length < 0.5f)
            return [];
        if (Math.Abs(_shape.LengthFront - length) > 0.1f)
            _shape = new(length, 2f);
        _aoe[0] = new(_shape, origin, Angle.FromDirection(dir), activation);
        return _aoe;
    }

    public override void OnEventIcon(Actor actor, uint iconID, ulong targetID)
    {
        if (iconID == (uint)IconID.Spread && !Module.PrimaryActor.IsTargetable)
            _jumps.Add((actor, WorldState.FutureTime(5.3d)));
    }

    public override void OnEventCast(Actor caster, ActorCastEvent spell)
    {
        if (spell.Action.ID == (uint)AID.PalladionJump)
        {
            _origin = spell.TargetXZ;
            if (_jumps.Count != 0)
                _jumps.RemoveAt(0);
        }
        else if (spell.Action.ID == WatchedAction)
        {
            ++NumCasts;
        }
    }

    public override void Update()
    {
        var expire = WorldState.CurrentTime.AddSeconds(-1.5d);
        for (var i = _jumps.Count - 1; i >= 0; --i)
        {
            if (_jumps[i].Activation < expire || _jumps[i].Target.IsDead)
                _jumps.RemoveAt(i);
        }
        if (_jumps.Count == 0)
            _origin = null;
    }
}
