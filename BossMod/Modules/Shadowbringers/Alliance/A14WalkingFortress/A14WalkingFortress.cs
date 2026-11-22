namespace BossMod.Shadowbringers.Alliance.A14WalkingFortress;

sealed class Neutralization(BossModule module) : Components.SingleTargetCast(module, (uint)AID.Neutralization);
sealed class LaserSaturation(BossModule module) : Components.RaidwideCast(module, (uint)AID.LaserSaturation);
sealed class LaserTurret(BossModule module) : Components.SimpleAOEs(module, (uint)AID.LaserTurret, new AOEShapeRect(90f, 4f));
sealed class GroundToGroundMissile(BossModule module) : Components.SimpleAOEs(module, (uint)AID.GroundToGroundMissile, 6f);
sealed class BallisticImpact(BossModule module) : Components.SpreadFromCastTargets(module, (uint)AID.BallisticImpact, 6f);
sealed class MarxImpact(BossModule module) : Components.SimpleAOEs(module, (uint)AID.MarxImpact, 22f);
sealed class ConvenientSelfDestructionSmall(BossModule module) : Components.SimpleAOEs(module, (uint)AID.ConvenientSelfDestructionSmall, 22f);
sealed class ClangingBlow(BossModule module) : Components.SingleTargetCast(module, (uint)AID.ClangingBlow);
sealed class ShrapnelImpact(BossModule module) : Components.StackWithCastTargets(module, (uint)AID.ShrapnelImpact, 6f);
sealed class CentrifugalSpin(BossModule module) : Components.SimpleAOEs(module, (uint)AID.CentrifugalSpin, new AOEShapeRect(30f, 4f));

// 前後齊射＝前後兩道、側面齊射＝左右兩道扇形，正側面／正前後安全；角度沒有資料，保守畫 150 度（不到 180＝安全方向仍留空）。
sealed class LaserSuppression(BossModule module) : Components.SimpleAOEs(module, (uint)AID.LaserSuppression, new AOEShapeCone(60f, 75f.Degrees()));

// 被駭的戰車自爆可以躲在另一台戰車後面擋掉；擋的只算施放者以外的戰車。
sealed class ConvenientSelfDestructionLarge(BossModule module) : Components.CastLineOfSightAOE(module, (uint)AID.ConvenientSelfDestructionLarge, 85f)
{
    public override ReadOnlySpan<Actor> BlockerActors()
    {
        var tanks = Module.Enemies((uint)OID.GoliathTank);
        var caster = ActiveCaster;
        var blockers = new List<Actor>(tanks.Count);
        for (var i = 0; i < tanks.Count; ++i)
            if (tanks[i] != caster)
                blockers.Add(tanks[i]);
        return CollectionsMarshal.AsSpan(blockers);
    }
}

// 三道俯衝同時開始讀條、依序結算；三道可能蓋滿全場，所以只把最先結算的當危險、下一道只預告。
sealed class Divebomb(BossModule module) : Components.GenericAOEs(module)
{
    private static readonly AOEShapeRect rect = new(60f, 10f);
    private readonly List<AOEInstance> _aoes = new(3);

    private static bool IsLane(uint aid) => aid is (uint)AID.DivebombLane1 or (uint)AID.DivebombLane2 or (uint)AID.DivebombLane3;

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
        if (IsLane(spell.Action.ID))
        {
            _aoes.Add(new(rect, spell.LocXZ, spell.Rotation, Module.CastFinishAt(spell), actorID: caster.InstanceID));
            _aoes.Sort((a, b) => a.Activation.CompareTo(b.Activation));
        }
    }

    public override void OnCastFinished(Actor caster, ActorCastInfo spell)
    {
        if (!IsLane(spell.Action.ID))
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

// 首領的無敵來自大型戰車的連線、戰車全倒才解除，所以首領無敵期間先打戰車；首領本身由 UnclaimedCastAOEs 降成無敵優先度。
// 只把預設優先度的戰車拉高：被判成不該打（沒在跟我方戰鬥等）的照舊。
sealed class GoliathTankPriority(BossModule module) : BossComponent(module)
{
    private readonly List<Actor> _tanks = module.Enemies((uint)OID.GoliathTank);

    public override void AddAIHints(int slot, Actor actor, PartyRolesConfig.Assignment assignment, AIHints hints)
    {
        if (Module.PrimaryActor.FindStatus((uint)SID.Invincibility) == null)
            return;
        var count = _tanks.Count;
        for (var i = 0; i < count; ++i)
        {
            var t = _tanks[i];
            if (hints.FindEnemy(t) is { } e && e.Actor == t && e.Priority == 0)
                e.Priority = 1;
        }
    }
}

// 場地＝碰撞資料的方形甲板（x 870.25～930、z 397.25～457），內縮到半邊 29.5；南側通道戰鬥中封閉，不放進場地。
[ModuleInfo(BossModuleInfo.Maturity.WIP, Contributors = "The Combat Reborn Team, ffxiv-tc-port", GroupType = BossModuleInfo.GroupType.CFC, GroupID = 700, NameID = 9153)]
public sealed class A14WalkingFortress(WorldState ws, Actor primary) : BossModule(ws, primary, new(900f, 427f), new ArenaBoundsSquare(29.5f));
