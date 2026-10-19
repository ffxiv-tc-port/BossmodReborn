namespace BossMod.Dawntrail.Extreme.Ex5Necron;

sealed class Intermission(BossModule module) : BossComponent(module)
{
    public bool Started;

    public override void OnEventDirectorUpdate(uint updateID, uint param1, uint param2, uint param3, uint param4)
    {
        if (param2 == default && updateID == 0x8000000C)
        {
            Started = true;
        }
    }
}

// 上游為這場改寫了共用 TankSwap（讀條一開始就預告兩擊，第一擊不必有自己的讀條）；
// 本 fork 的共用 TankSwap 仍是舊版而且別的模組在用，所以這裡放一份模組內的副本
sealed class BlueShockwave(BossModule module) : Components.GenericBaitAway(module, damageType: AIHints.PredictedDamageType.Tankbuster)
{
    private readonly AOEShapeCone cone = new(100f, 50f.Degrees());
    private Actor? _source;
    private ulong _prevTarget; // before first cast, this is the target of the first hit

    public override void Update()
    {
        if (_source != null)
        {
            var count = CurrentBaits.Count;
            if (count != 0 && WorldState.Actors.Find(_source.TargetID) is Actor t)
            {
                if (count == 1 && NumCasts == 1)
                {
                    CurrentBaits.Ref(0).Target = t;
                }
                else if (count == 2)
                {
                    CurrentBaits.Ref(1).Target = t;
                }
            }
        }
    }

    public override void AddHints(int slot, Actor actor, TextHints hints)
    {
        if (_source?.TargetID == _prevTarget && actor.Role == Role.Tank)
        {
            hints.Add(Loc.T(_prevTarget != actor.InstanceID ? "Provoke!" : "Pass aggro!"));
        }
        base.AddHints(slot, actor, hints);
    }

    public override void OnCastStarted(Actor caster, ActorCastInfo spell)
    {
        var id = spell.Action.ID;
        if (id == (uint)AID.BlueShockwaveVisual1)
        {
            _source = caster;
            if (WorldState.Actors.Find(caster.TargetID) is Actor t)
            {
                CurrentBaits.Add(new(caster, t, cone, Module.CastFinishAt(spell, 1.2d)));
                CurrentBaits.Add(new(caster, t, cone, Module.CastFinishAt(spell, 5.2d)));
            }
        }
        else if (id == (uint)AID.BlueShockwave)
        {
            NumCasts = 0;
            _prevTarget = spell.TargetID;
        }
    }

    public override void OnEventCast(Actor caster, ActorCastEvent spell)
    {
        if (spell.Action.ID == (uint)AID.BlueShockwave)
        {
            ++NumCasts;
            _prevTarget = spell.MainTargetID == caster.InstanceID && spell.Targets.Count != 0 ? spell.Targets.Ref(0).ID : spell.MainTargetID;
            if (CurrentBaits.Count != 0)
            {
                CurrentBaits.RemoveAt(0);
            }
        }
    }
}
sealed class ChokingGrasp(BossModule module) : Components.SimpleAOEGroups(module, [(uint)AID.ChokingGraspAOE1, (uint)AID.ChokingGraspAOE2], Rect)
{
    public static readonly AOEShapeRect Rect = new(24f, 3f);
}

sealed class CircleOfLives(BossModule module) : Components.SimpleAOEs(module, (uint)AID.CircleOfLives, new AOEShapeDonut(3f, 50f), 1);

[ModuleInfo(BossModuleInfo.Maturity.Verified, Contributors = "The Combat Reborn Team (Malediktus)", PrimaryActorOID = (uint)OID.Necron, GroupType = BossModuleInfo.GroupType.CFC, GroupID = 1062u, NameID = 14093u, PlanLevel = 100)]
public sealed class Ex5Necron(WorldState ws, Actor primary) : Trial.T05Necron.Necron(ws, primary);
