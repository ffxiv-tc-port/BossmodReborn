namespace BossMod.Stormblood.Trial.T09Seiryu;

// 第二階段平台沒有牆、外圈是水面：人在水裡用含水面的場地，回到台上用平台。
// 在 Update 切而不是在畫雷達時切：AI 的尋路視窗取自模組場地，雷達沒畫時也要跟著換。
sealed class ArenaChange(BossModule module) : BossComponent(module)
{
    private bool _phase2;

    public override void Update()
    {
        if (!_phase2 || Raid.Player() is not { } pc)
            return;
        Arena.Bounds = pc.Position.InCircle(Arena.Center, 20f) ? Seiryu.Phase2Bounds : Seiryu.Phase2WaterBounds;
    }

    public override void AddAIHints(int slot, Actor actor, PartyRolesConfig.Assignment assignment, AIHints hints)
    {
        if (_phase2 && !actor.Position.InCircle(Arena.Center, 20f))
        {
            hints.AddForbiddenZone(ShapeDistance.InvertedCircle(Arena.Center, 19f), DateTime.MaxValue);
        }
    }

    public override void OnCastFinished(Actor caster, ActorCastInfo spell)
    {
        if (spell.Action.ID == (uint)AID.StrengthOfSpirit) // in phase 2 the arena no longer got a wall and we need to add back the player hitboxradius
        {
            _phase2 = true;
            Arena.Bounds = Seiryu.Phase2Bounds;
        }
    }
}

// 荒波只打水面那圈：人在台上（場地＝平台）時不列出。
sealed class GreatTyphoonDonut(BossModule module) : Components.GenericAOEs(module)
{
    private AOEInstance[] _aoe = [];
    private static readonly AOEShapeDonut donut1 = new(20f, 28f), donut2 = new(26f, 34f), donut3 = new(32f, 40f);

    public override ReadOnlySpan<AOEInstance> ActiveAOEs(int slot, Actor actor) => Arena.Bounds.Radius > 20f ? _aoe : [];

    public override void OnCastStarted(Actor caster, ActorCastInfo spell)
    {
        AOEShape? shape = spell.Action.ID switch
        {
            (uint)AID.GreatTyphoon1 => donut1,
            (uint)AID.GreatTyphoon2 => donut2,
            (uint)AID.GreatTyphoon3 => donut3,
            _ => null
        };
        if (shape != null)
        {
            _aoe = [new(shape, spell.LocXZ, default, Module.CastFinishAt(spell), actorID: caster.InstanceID)];
        }
    }

    public override void OnCastFinished(Actor caster, ActorCastInfo spell)
    {
        if (spell.Action.ID is (uint)AID.GreatTyphoon1 or (uint)AID.GreatTyphoon2 or (uint)AID.GreatTyphoon3)
        {
            if (_aoe.Length != 0 && caster.InstanceID == _aoe[0].ActorID) // depending on latency a new cast can start in the same frame as previous ended
            {
                _aoe = [];
            }
        }
    }
}
