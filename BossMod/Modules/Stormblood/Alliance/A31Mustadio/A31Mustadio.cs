namespace BossMod.Stormblood.Alliance.A31Mustadio;

class EnergyBurst(BossModule module) : Components.RaidwideCast(module, (uint)AID.EnergyBurst);
class ArmShot(BossModule module) : Components.SingleTargetCast(module, (uint)AID.ArmShot);
class LegShot(BossModule module) : Components.Voidzone(module, 3f, GetVoidzones)
{
    private static Actor[] GetVoidzones(BossModule module)
    {
        var enemies = module.Enemies((uint)OID.LegShotVoidzone);
        var count = enemies.Count;
        if (count == 0)
            return [];

        var voidzones = new Actor[count];
        var index = 0;
        for (var i = 0; i < count; ++i)
        {
            var z = enemies[i];
            if (z.EventState != 7)
                voidzones[index++] = z;
        }
        return voidzones[..index];
    }
}

abstract class Handgonne(BossModule module, uint aid) : Components.SimpleAOEs(module, aid, new AOEShapeCone(30f, 105f.Degrees()));
class LeftHandgonne(BossModule module) : Handgonne(module, (uint)AID.LeftHandgonne);
class RightHandgonne(BossModule module) : Handgonne(module, (uint)AID.RightHandgonne);

// 維護讀條開始時，要開火的勞動型鐵巨人／古代機工兵器會連線到姆斯塔迪奧，約 14.5 秒後才讀條 1.7 秒放執行壓縮／衛星光束 ⇒ 從連線起就畫。
// 機體不會移動，範圍就是它自己的位置與面向；讀條開始後改用讀條的落點與結束時間。
sealed class MaintenanceLasers(BossModule module) : Components.GenericAOEs(module)
{
    private static readonly AOEShapeRect _compress = new(100f, 7.5f), _beam = new(30f, 15f);
    private const double TetherToHit = 14.5d, RiskySeconds = 6d;
    private readonly List<AOEInstance> _aoes = [];

    public override ReadOnlySpan<AOEInstance> ActiveAOEs(int slot, Actor actor) => CollectionsMarshal.AsSpan(_aoes);

    private int IndexOf(ulong id)
    {
        for (var i = 0; i < _aoes.Count; ++i)
            if (_aoes[i].ActorID == id)
                return i;
        return -1;
    }

    public override void OnTethered(Actor source, ActorTetherInfo tether)
    {
        var shape = tether.ID switch
        {
            (uint)TetherID.Tether86 => _compress,
            (uint)TetherID.Tether88 => _beam,
            _ => null
        };
        if (shape == null || IndexOf(source.InstanceID) >= 0)
            return;
        // 衛星光束的讀條落點在機體正後方 15 碼、往前 30 碼 ⇒ 以機體為中心的方塊。
        var origin = shape == _beam ? source.Position - 15f * source.Rotation.ToDirection() : source.Position;
        _aoes.Add(new(shape, origin, source.Rotation, WorldState.FutureTime(TetherToHit), risky: false, actorID: source.InstanceID));
    }

    public override void OnCastStarted(Actor caster, ActorCastInfo spell)
    {
        var shape = spell.Action.ID switch
        {
            (uint)AID.Compress => _compress,
            (uint)AID.SatelliteBeam => _beam,
            _ => null
        };
        if (shape == null)
            return;
        var aoe = new AOEInstance(shape, spell.LocXZ, spell.Rotation, Module.CastFinishAt(spell), actorID: caster.InstanceID);
        var i = IndexOf(caster.InstanceID);
        if (i >= 0)
            _aoes[i] = aoe;
        else
            _aoes.Add(aoe);
    }

    public override void OnCastFinished(Actor caster, ActorCastInfo spell)
    {
        if (spell.Action.ID is (uint)AID.Compress or (uint)AID.SatelliteBeam && IndexOf(caster.InstanceID) is var i && i >= 0)
            _aoes.RemoveAt(i);
    }

    // 連線了卻沒讀條（例：滅團）就在預定時間過 2 秒後放掉。
    // AI 只在最後 RiskySeconds 秒才閃：第四輪連線正好落在彈道散開判定前，太早閃會為了 14 秒後的雷射走進別人的散開圈。
    public override void Update()
    {
        var expired = WorldState.FutureTime(-2d);
        var risky = WorldState.FutureTime(RiskySeconds);
        for (var i = _aoes.Count - 1; i >= 0; --i)
            if (_aoes[i].Activation < expired)
                _aoes.RemoveAt(i);
        var span = CollectionsMarshal.AsSpan(_aoes);
        for (var i = 0; i < span.Length; ++i)
            span[i].Risky = span[i].Activation <= risky;
    }
}

// 探照燈出現後會追著被點名的人走，約 7.8 秒後在當時的位置炸開 5 碼圓 ⇒ 一路畫在它目前的位置。
sealed class Searchlight(BossModule module) : Components.GenericAOEs(module, (uint)AID.Searchlight2)
{
    private static readonly AOEShapeCircle _circle = new(5f);
    private const double SpawnToHit = 7.8d;
    private readonly List<(Actor Light, DateTime Activation)> _lights = [];
    private readonly List<AOEInstance> _aoes = [];

    public override ReadOnlySpan<AOEInstance> ActiveAOEs(int slot, Actor actor) => CollectionsMarshal.AsSpan(_aoes);

    public override void OnActorCreated(Actor actor)
    {
        if (actor.OID == (uint)OID.Mustadio1)
            _lights.Add((actor, WorldState.FutureTime(SpawnToHit)));
    }

    public override void OnActorDestroyed(Actor actor)
    {
        for (var i = _lights.Count - 1; i >= 0; --i)
            if (_lights[i].Light == actor)
                _lights.RemoveAt(i);
    }

    public override void OnEventCast(Actor caster, ActorCastEvent spell)
    {
        if (spell.Action.ID == WatchedAction)
            _lights.Clear();
    }

    public override void Update()
    {
        _aoes.Clear();
        for (var i = 0; i < _lights.Count; ++i)
            _aoes.Add(new(_circle, _lights[i].Light.Position, default, _lights[i].Activation));
    }
}

class BallisticSpread(BossModule module) : Components.SpreadFromCastTargets(module, (uint)AID.BallisticImpact1, 6);

// 分析給的「X側未解析」那一側要對著讀條中的姆斯塔迪奧，其他方向被最終聖約打中會死。
// 沒收到某人的未解析狀態（例：中途才載入模組）就不畫那個人的提示，不猜方向。
sealed class LastTestament(BossModule module) : Components.CastWeakpoint(module, (uint)AID.LastTestament, new AOEShapeRect(100f, 30f), default, (uint)SID.BackUnseen, (uint)SID.LeftUnseen, (uint)SID.RightUnseen);

[ModuleInfo(BossModuleInfo.Maturity.WIP, Contributors = "The Combat Reborn Team", GroupType = BossModuleInfo.GroupType.CFC, GroupID = 636, NameID = 7915)] // 7919 
public class A31Mustadio(WorldState ws, Actor primary) : BossModule(ws, primary, new(600, 290), new ArenaBoundsSquare(30, 45.Degrees()));
