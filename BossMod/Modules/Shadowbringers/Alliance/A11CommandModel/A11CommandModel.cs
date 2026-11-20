namespace BossMod.Shadowbringers.Alliance.A11CommandModel;

sealed class ClangingBlow(BossModule module) : Components.SingleTargetCast(module, (uint)AID.ClangingBlow);
sealed class ForcefulImpact(BossModule module) : Components.RaidwideCast(module, (uint)AID.ForcefulImpact);
sealed class EnergyBombardment(BossModule module) : Components.SimpleAOEs(module, (uint)AID.EnergyBombardment, 4f);
sealed class SidestrikingSpin(BossModule module) : Components.SimpleAOEGroups(module, [(uint)AID.SidestrikingSpinL, (uint)AID.SidestrikingSpinR], new AOEShapeRect(30f, 6f));
sealed class CentrifugalSpin(BossModule module) : Components.SimpleAOEs(module, (uint)AID.CentrifugalSpin, new AOEShapeRect(30f, 4f));
sealed class HighCaliberLaser(BossModule module) : Components.SimpleAOEs(module, (uint)AID.HighCaliberLaser, new AOEShapeRect(70f, 12f));

// 王轉向隨機玩家後從正面持續放扇形，到王的收尾技能為止；扇形角度沒有資料，保守畫正面半圓（cactbot：繞到背後）。
sealed class EnergyAssault(BossModule module) : Components.GenericAOEs(module)
{
    private static readonly AOEShapeCone cone = new(30f, 90f.Degrees());
    private AOEInstance? _aoe;
    private DateTime _activation;
    private DateTime _expire;

    public override ReadOnlySpan<AOEInstance> ActiveAOEs(int slot, Actor actor) => Utils.ZeroOrOne(ref _aoe);

    public override void Update()
    {
        if (_activation == default)
            return;
        if (WorldState.CurrentTime > _expire)
        {
            _activation = default;
            _aoe = null;
            return;
        }
        var boss = Module.PrimaryActor;
        _aoe = new(cone, boss.Position.Quantized(), boss.Rotation, _activation);
    }

    public override void OnCastStarted(Actor caster, ActorCastInfo spell)
    {
        if (spell.Action.ID == (uint)AID.EnergyAssault)
        {
            _activation = Module.CastFinishAt(spell);
            // 收尾技能沒看到時的上限：時間軸上讀條結束到收尾約 7 秒。
            _expire = _activation.AddSeconds(10d);
        }
    }

    public override void OnEventCast(Actor caster, ActorCastEvent spell)
    {
        if (spell.Action.ID == (uint)AID.EnergyAssaultEnd)
        {
            _activation = default;
            _aoe = null;
        }
    }
}

// 圓與三圈甜甜圈依序結算（內外徑取自台服 Omen），只把最先結算的一個當危險、下一個只預告：AI 每次只需小幅移動。
sealed class EnergyRing(BossModule module) : Components.GenericAOEs(module)
{
    private readonly List<AOEInstance> _aoes = new(4);

    private static AOEShape? ShapeFor(uint aid) => aid switch
    {
        (uint)AID.EnergyRingCircle => new AOEShapeCircle(12f),
        (uint)AID.EnergyRingDonut1 => new AOEShapeDonut(12f, 24f),
        (uint)AID.EnergyRingDonut2 => new AOEShapeDonut(24f, 36f),
        (uint)AID.EnergyRingDonut3 => new AOEShapeDonut(36f, 48f),
        _ => null
    };

    public override ReadOnlySpan<AOEInstance> ActiveAOEs(int slot, Actor actor)
    {
        var count = _aoes.Count;
        if (count == 0)
            return [];
        var max = count > 2 ? 2 : count;
        var aoes = CollectionsMarshal.AsSpan(_aoes);
        for (var i = 0; i < max; ++i)
        {
            ref var aoe = ref aoes[i];
            aoe.Risky = i == 0;
            aoe.Color = i == 0 && max > 1 ? Colors.Danger : default;
        }
        return aoes[..max];
    }

    public override void OnCastStarted(Actor caster, ActorCastInfo spell)
    {
        if (ShapeFor(spell.Action.ID) is AOEShape shape)
        {
            _aoes.Add(new(shape, spell.LocXZ, spell.Rotation, Module.CastFinishAt(spell), actorID: caster.InstanceID));
            _aoes.Sort((a, b) => a.Activation.CompareTo(b.Activation));
        }
    }

    public override void OnCastFinished(Actor caster, ActorCastInfo spell)
    {
        if (ShapeFor(spell.Action.ID) == null)
            return;
        var count = _aoes.Count;
        for (var i = 0; i < count; ++i)
        {
            if (_aoes[i].ActorID == caster.InstanceID)
            {
                _aoes.RemoveAt(i);
                return;
            }
        }
    }
}

// 場地＝碰撞資料的方形倉庫（中心 (-500,0)、半邊 24），內縮 0.5；北側通道戰鬥中封閉，不放進場地。
[ModuleInfo(BossModuleInfo.Maturity.WIP, Contributors = "The Combat Reborn Team, ffxiv-tc-port", GroupType = BossModuleInfo.GroupType.CFC, GroupID = 700, NameID = 9141)]
public sealed class A11CommandModel(WorldState ws, Actor primary) : BossModule(ws, primary, new(-500f, default), new ArenaBoundsSquare(23.5f));
