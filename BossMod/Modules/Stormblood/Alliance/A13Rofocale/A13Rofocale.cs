namespace BossMod.Stormblood.Alliance.A13Rofocale;

sealed class Chariot(BossModule module) : Components.BaitAwayChargeCast(module, (uint)AID.Chariot, 5f);
sealed class CryOfVictory(BossModule module) : Components.SimpleAOEGroups(module, [(uint)AID.CryOfVictory1, (uint)AID.CryOfVictory2], new AOEShapeCone(60f, 90f.Degrees()));
sealed class CrushWeapon(BossModule module) : Components.SimpleAOEs(module, (uint)AID.CrushWeapon, 6f);
sealed class Karma(BossModule module) : Components.SimpleAOEs(module, (uint)AID.Karma, new AOEShapeCone(30f, 45f.Degrees()));
sealed class UnholyDarkness(BossModule module) : Components.SimpleAOEs(module, (uint)AID.UnholyDarkness, 8f);

// 對地點讀條的矩形（CastType 12）：從施放者起算、朝讀條落點，長度固定 60（EXD），不是到落點為止。
sealed class Maverick(BossModule module) : Components.GenericAOEs(module)
{
    private readonly List<AOEInstance> _aoes = [];
    private static readonly AOEShapeRect _shape = new(60f, 8f);

    public override ReadOnlySpan<AOEInstance> ActiveAOEs(int slot, Actor actor) => CollectionsMarshal.AsSpan(_aoes);

    public override void OnCastStarted(Actor caster, ActorCastInfo spell)
    {
        if (spell.Action.ID is (uint)AID.Maverick1 or (uint)AID.Maverick2 or (uint)AID.Maverick3)
        {
            var dir = spell.LocXZ - caster.Position;
            var rot = dir.LengthSq() > 0.01f ? Angle.FromDirection(dir) : spell.Rotation;
            _aoes.Add(new(_shape, caster.Position.Quantized(), rot, Module.CastFinishAt(spell), actorID: caster.InstanceID));
        }
    }

    public override void OnCastFinished(Actor caster, ActorCastInfo spell)
    {
        if (spell.Action.ID is (uint)AID.Maverick1 or (uint)AID.Maverick2 or (uint)AID.Maverick3)
        {
            var id = caster.InstanceID;
            for (var i = _aoes.Count - 1; i >= 0; --i)
                if (_aoes[i].ActorID == id)
                    _aoes.RemoveAt(i);
        }
    }
}

// 場地＝碰撞資料的圓形（中心 (106,-191)、半徑約 29.8）。
[ModuleInfo(BossModuleInfo.Maturity.WIP, Contributors = "The Combat Reborn Team, ffxiv-tc-port", GroupType = BossModuleInfo.GroupType.CFC, GroupID = 281, NameID = 6941)]
public sealed class A13Rofocale(WorldState ws, Actor primary) : BossModule(ws, primary, new(106f, -191f), new ArenaBoundsCircle(29.5f));
