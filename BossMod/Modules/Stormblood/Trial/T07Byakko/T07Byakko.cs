namespace BossMod.Stormblood.Trial.T07Byakko;

class StormPulse(BossModule module) : Components.RaidwideCast(module, (uint)AID.StormPulse);
class HeavenlyStrike(BossModule module) : Components.SingleTargetCast(module, (uint)AID.HeavenlyStrike);
class HeavenlyStrikeSpread(BossModule module) : Components.SpreadFromCastTargets(module, (uint)AID.HeavenlyStrike, 3f);
class SweepTheLeg1(BossModule module) : Components.SimpleAOEs(module, (uint)AID.SweepTheLeg1, new AOEShapeCone(28.5f, 135f.Degrees()));
class SweepTheLeg3(BossModule module) : Components.SimpleAOEs(module, (uint)AID.SweepTheLeg3, new AOEShapeDonut(5f, 30f));
class TheRoarOfThunder(BossModule module) : Components.RaidwideCast(module, (uint)AID.TheRoarOfThunder);
class ImperialGuard(BossModule module) : Components.SimpleAOEs(module, (uint)AID.ImperialGuard, new AOEShapeRect(44.75f, 2.5f));
class FireAndLightning(BossModule module) : Components.SimpleAOEGroups(module, [(uint)AID.FireAndLightning1, (uint)AID.FireAndLightning2], new AOEShapeRect(50f, 10f));

// outer radius used to be 3f, i.e. smaller than the inner radius. AOEShapeDonut.Check() forwards to
// InDonut(inner, outer), which can never be true when outer < inner, so this component was inert: it never
// flagged anyone as being in the AOE. 25f is the outer radius recorded in T07ByakkoEnums.cs's own generated
// comment for AID 10800 ("range ?-25 donut") and is what Ex6Byakko uses for the same mechanic.
// Inner radius deliberately left at 5f: in the TC replays every cast by the boss itself reports 0 damage
// against the (over-levelled, unsynced) player, so they carry no evidence about where the safe hole ends.
class DistantClap(BossModule module) : Components.SimpleAOEs(module, (uint)AID.DistantClap, new AOEShapeDonut(5f, 25f));

class HighestStakes(BossModule module) : Components.StackWithIcon(module, (uint)IconID.Stackmarker, (uint)AID.HighestStakes2, 6f, 5f, 7, 7);

class AratamaForce(BossModule module) : Components.Voidzone(module, 2f, GetVoidzones, 2)
{
    private static Actor[] GetVoidzones(BossModule module)
    {
        var enemies = module.Enemies((uint)OID.AratamaForce);
        var count = enemies.Count;
        if (count == 0)
            return [];

        var voidzones = new Actor[count];
        var index = 0;
        for (var i = 0; i < count; ++i)
        {
            var z = enemies[i];
            if (!z.IsDead)
                voidzones[index++] = z;
        }
        return voidzones[..index];
    }
}

class HundredfoldHavoc(BossModule module) : Components.Exaflare(module, 5f)
{
    public override void OnCastStarted(Actor caster, ActorCastInfo spell)
    {
        if (spell.Action.ID == (uint)AID.HundredfoldHavocFirst)
        {
            Lines.Add(new(caster.Position, 5f * caster.Rotation.ToDirection(), Module.CastFinishAt(spell), 1d, 10, 2));
        }
    }

    public override void OnEventCast(Actor caster, ActorCastEvent spell)
    {
        if (spell.Action.ID is (uint)AID.HundredfoldHavocFirst or (uint)AID.HundredfoldHavocRest)
        {
            var count = Lines.Count;
            var pos = caster.Position;
            for (var i = 0; i < count; ++i)
            {
                var line = Lines[i];
                if (line.Next.AlmostEqual(pos, 1f))
                {
                    AdvanceLine(line, pos);
                    if (line.ExplosionsLeft == 0)
                        Lines.RemoveAt(i);
                    return;
                }
            }
        }
    }
}

// NameID was 6221, which is Susano's BNpcName row (T01Susano declares the same value); it is only used by
// ModuleViewer to label the entry, so the module list showed this trial under the wrong boss name.
// 7092 is verified three ways: BNpcName row 7092 is 白虎 while 6221 is 須佐之男 in the TC sheet, the live
// TC replays report nameId 7092 on the primary actor (OID 0x20F7), and Ex6Byakko already uses 7092.
[ModuleInfo(BossModuleInfo.Maturity.WIP, Contributors = "The Combat Reborn Team", GroupType = BossModuleInfo.GroupType.CFC, GroupID = 290, NameID = 7092)]
public class T07Byakko(WorldState ws, Actor primary) : BossModule(ws, primary, default, new ArenaBoundsCircle(20f));
