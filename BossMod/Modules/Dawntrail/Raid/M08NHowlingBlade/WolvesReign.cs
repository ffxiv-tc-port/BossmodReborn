namespace BossMod.Dawntrail.Raid.M08NHowlingBlade;

sealed class WolvesReignRect(BossModule module) : Components.GenericAOEs(module)
{
    private static readonly AOEShapeRect rectLong = new(36f, 5f), rectShort = new(28f, 5f);
    private AOEInstance? _aoe;

    public override ReadOnlySpan<AOEInstance> ActiveAOEs(int slot, Actor actor) => Utils.ZeroOrOne(ref _aoe);

    // 直線從最後一個圓圈（讀條最長那發）往後 4 碼、沿它的方向出；圓圈結束後約 3.5／2.4 秒判定（同零式版 WolvesReignRect）。
    // 直線自己開始讀條後改用它的位置、方向與結束時間。
    public override void OnCastStarted(Actor caster, ActorCastInfo spell)
    {
        switch (spell.Action.ID)
        {
            case (uint)AID.WolvesReignCircle4:
            case (uint)AID.WolvesReignCircle8:
                var isLong = spell.Action.ID == (uint)AID.WolvesReignCircle4;
                var rot = spell.Rotation;
                _aoe = new(isLong ? rectLong : rectShort, (spell.LocXZ - 4f * rot.ToDirection()).Quantized(), rot, Module.CastFinishAt(spell, isLong ? 3.5d : 2.4d));
                break;
            case (uint)AID.WolvesReignRect1:
            case (uint)AID.WolvesReignRect2:
                _aoe = new(spell.Action.ID == (uint)AID.WolvesReignRect1 ? rectLong : rectShort, spell.LocXZ, spell.Rotation, Module.CastFinishAt(spell), actorID: caster.InstanceID);
                break;
        }
    }

    public override void OnCastFinished(Actor caster, ActorCastInfo spell)
    {
        if (spell.Action.ID is (uint)AID.WolvesReignRect1 or (uint)AID.WolvesReignRect2)
            _aoe = null;
    }
}

sealed class WolvesReignCone(BossModule module) : Components.GenericAOEs(module)
{
    private static readonly AOEShapeCone cone = new(40f, 60f.Degrees());
    private AOEInstance? _aoe;

    public override ReadOnlySpan<AOEInstance> ActiveAOEs(int slot, Actor actor) => Utils.ZeroOrOne(ref _aoe);

    // 瞬移到扇形判定：長瞬移約 5 秒、短瞬移約 4.1 秒；扇形自己開始讀條後改用它的結束時間與方向。
    public override void OnCastStarted(Actor caster, ActorCastInfo spell)
    {
        switch (spell.Action.ID)
        {
            case (uint)AID.WolvesReignTeleport2:
            case (uint)AID.WolvesReignTeleport4:
                var pos = spell.LocXZ;
                var delay = spell.Action.ID == (uint)AID.WolvesReignTeleport4 ? 4.1d : 5.1d;
                _aoe = new(cone, pos, Angle.FromDirection(Arena.Center - pos), Module.CastFinishAt(spell, delay));
                break;
            case (uint)AID.WolvesReignCone1:
            case (uint)AID.WolvesReignCone2:
                _aoe = new(cone, spell.LocXZ, spell.Rotation, Module.CastFinishAt(spell));
                break;
        }
    }

    public override void OnCastFinished(Actor caster, ActorCastInfo spell)
    {
        if (spell.Action.ID is (uint)AID.WolvesReignCone1 or (uint)AID.WolvesReignCone2)
            _aoe = null;
    }
}

sealed class WolvesReignCircle(BossModule module) : Components.SimpleAOEGroups(module, [(uint)AID.WolvesReignCircle1, (uint)AID.WolvesReignCircle2,
(uint)AID.WolvesReignCircle3, (uint)AID.WolvesReignCircle4, (uint)AID.WolvesReignCircle5, (uint)AID.WolvesReignCircle6, (uint)AID.WolvesReignCircle7, (uint)AID.WolvesReignCircle8], 6f);
