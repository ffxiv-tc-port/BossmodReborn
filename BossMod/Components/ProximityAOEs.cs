namespace BossMod.Components;

// Damage-falloff circle: avoided like a normal AOE, but nobody is ever fully out of it, so the whole raid also gets a damage prediction.
// Upstream version minus arena projection layers (we have no multi-floor projection); with no layers upstream also
// emits one prediction for all casters, using the first caster's activation.
public class ProximityAOEs(BossModule module, uint aid, float radius) : SimpleAOEs(module, aid, radius)
{
    public override void AddAIHints(int slot, Actor actor, PartyRolesConfig.Assignment assignment, AIHints hints)
    {
        base.AddAIHints(slot, actor, assignment, hints);
        if (Casters.Count != 0)
        {
            hints.AddPredictedDamage(Raid.WithSlot().Mask(), Casters.Ref(0).Activation);
        }
    }
}
