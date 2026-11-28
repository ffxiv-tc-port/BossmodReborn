namespace BossMod.RealmReborn.Alliance.A33Cerberus;

class PredatorClaws(BossModule module) : Components.Cleave(module, (uint)AID.PredatorClaws, new AOEShapeCone(19.8f, 45f.Degrees()));
class TailBlow(BossModule module) : Components.SimpleAOEs(module, (uint)AID.TailBlow, new AOEShapeCone(19, 45.Degrees()));
class Slabber(BossModule module) : Components.SimpleAOEs(module, (uint)AID.Slabber, 8);
class Mini(BossModule module) : Components.SimpleAOEs(module, (uint)AID.Mini, 9);
class SulphurousBreath1(BossModule module) : Components.SimpleAOEs(module, (uint)AID.SulphurousBreath1, new AOEShapeRect(35, 3));
class SulphurousBreath2(BossModule module) : Components.SimpleAOEs(module, (uint)AID.SulphurousBreath2, new AOEShapeRect(45, 3));
class LightningBolt2(BossModule module) : Components.ChargeAOEs(module, (uint)AID.LightningBolt2, 2);
class HoundOutOfHell(BossModule module) : Components.ChargeAOEs(module, (uint)AID.HoundOutOfHell, 7);
class Ululation(BossModule module) : Components.RaidwideCast(module, (uint)AID.Ululation);

// 閃電沒有讀條也沒有標記，落在隨機一人腳下（半徑 8）；能預測的只有時間：復原讀條開始後，
// 兩條鎖鏈都重新連上王後約 13.2 秒、地獄衝讀條開始後約 22.0 秒各落一次。提前 3 秒標成全員散開。
class LightningBolt1(BossModule module) : Components.UniformStackSpread(module, default, 8f, alwaysShowSpreads: true)
{
    private bool _armed, _fetter32, _fetter33, _fetterUsed;
    private DateTime _predicted;

    public override void OnCastStarted(Actor caster, ActorCastInfo spell)
    {
        if (spell.Action.ID == (uint)AID.Reawakening)
        {
            _armed = true;
            _fetter32 = _fetter33 = _fetterUsed = false;
        }
        else if (_armed && spell.Action.ID == (uint)AID.HoundOutOfHell)
            Schedule(22d);
    }

    public override void OnTethered(Actor source, ActorTetherInfo tether)
    {
        if (!_armed || _fetterUsed || tether.Target != Module.PrimaryActor.InstanceID)
            return;
        if (tether.ID == (uint)TetherID.Tether32)
            _fetter32 = true;
        else if (tether.ID == (uint)TetherID.Tether33)
            _fetter33 = true;
        if (_fetter32 && _fetter33)
        {
            _fetterUsed = true;
            Schedule(13.2d);
        }
    }

    public override void OnEventCast(Actor caster, ActorCastEvent spell)
    {
        if (spell.Action.ID == (uint)AID.LightningBolt1)
        {
            Spreads.Clear();
            _predicted = default;
        }
    }

    public override void Update()
    {
        if (_predicted == default)
            return;
        var now = WorldState.CurrentTime;
        if (now > _predicted.AddSeconds(2d))
        {
            Spreads.Clear();
            _predicted = default;
        }
        else if (Spreads.Count == 0 && now >= _predicted.AddSeconds(-3d))
            AddSpreads(Raid.WithoutSlot(), _predicted);
    }

    private void Schedule(double delay)
    {
        Spreads.Clear();
        _predicted = WorldState.FutureTime(delay);
    }
}

[ModuleInfo(BossModuleInfo.Maturity.WIP, Contributors = "The Combat Reborn Team", GroupType = BossModuleInfo.GroupType.CFC, GroupID = 111, NameID = 3234)]
public class A33Cerberus(WorldState ws, Actor primary) : BossModule(ws, primary, new(0, -197), new ArenaBoundsRect(20, 40))
{
    protected override void DrawEnemies(int pcSlot, Actor pc)
    {
        Arena.Actor(PrimaryActor);
        Arena.Actors(Enemies(OID.GastricJuice));
        Arena.Actors(Enemies(OID.StomachWall));
        Arena.Actors(Enemies(OID.Wolfsbane));
    }
}
