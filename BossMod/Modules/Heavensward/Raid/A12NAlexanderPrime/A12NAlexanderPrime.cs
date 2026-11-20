namespace BossMod.Heavensward.Raid.A12NAlexanderPrime;

sealed class MegaHoly(BossModule module) : Components.RaidwideCast(module, (uint)AID.MegaHoly);
sealed class PunishingHeat(BossModule module) : Components.BaitAwayCast(module, (uint)AID.PunishingHeat, 5f, tankbuster: true, damageType: AIHints.PredictedDamageType.Tankbuster);
// 標記到判定的秒數沒有錄影：時間軸上前置讀條 3 秒、讀完 2 秒後判定，取 5 秒。
sealed class BlazingScourge(BossModule module) : Components.SpreadFromIcon(module, (uint)IconID.BlazingScourge, (uint)AID.BlazingScourge, 4f, 5d);
sealed class IncineratingHeat(BossModule module) : Components.StackWithCastTargets(module, (uint)AID.IncineratingHeat, 5f);

// 讀條結束後才由 SacramentAOE 判定（間隔沒有錄影），危險區留到判定事件，最多讀條結束後 3 秒。
// 位置與面向在讀條結束時重取：攻略說時間停止時首領會跳到場邊再放。
sealed class Sacrament(BossModule module) : Components.GenericAOEs(module, (uint)AID.SacramentAOE)
{
    private static readonly AOEShapeCross cross = new(60f, 8f);
    private AOEInstance[] _aoe = [];

    public override ReadOnlySpan<AOEInstance> ActiveAOEs(int slot, Actor actor) => _aoe;

    public override void OnCastStarted(Actor caster, ActorCastInfo spell)
    {
        if (spell.Action.ID == (uint)AID.Sacrament)
            _aoe = [new(cross, caster.Position.Quantized(), caster.Rotation, Module.CastFinishAt(spell))];
    }

    public override void OnCastFinished(Actor caster, ActorCastInfo spell)
    {
        if (spell.Action.ID == (uint)AID.Sacrament && _aoe.Length != 0)
            _aoe = [new(cross, caster.Position.Quantized(), caster.Rotation, _aoe[0].Activation)];
    }

    public override void OnEventCast(Actor caster, ActorCastEvent spell)
    {
        if (spell.Action.ID == WatchedAction)
            _aoe = [];
    }

    public override void Update()
    {
        if (_aoe.Length != 0 && WorldState.CurrentTime > _aoe[0].Activation.AddSeconds(3d))
            _aoe = [];
    }
}

// 時空門時玩家被送到下層（y -470）四間圓形房間之一；場地跟著換到玩家所在的房間，否則 AI 會往上層場地的方向走。
// 場地在 Update 切而不是在畫雷達時切：AI 的尋路視窗取自模組場地。
sealed class TimegateRooms(BossModule module) : BossComponent(module)
{
    private static readonly WPos[] rooms = [new(-39f, 13f), new(39f, 13f), new(-39f, 91f), new(39f, 91f)];
    private static readonly ArenaBoundsCircle roomBounds = new(20f);

    public override void Update()
    {
        if (Raid.Player() is not { } pc)
            return;
        if (pc.PosRot.Y < 0f)
        {
            var pos = pc.Position;
            for (var i = 0; i < 4; ++i)
            {
                if (pos.InCircle(rooms[i], 30f))
                {
                    if (Arena.Bounds != roomBounds || Arena.Center != rooms[i])
                    {
                        Arena.Bounds = roomBounds;
                        Arena.Center = rooms[i];
                    }
                    return;
                }
            }
        }
        else if (Arena.Bounds == roomBounds)
        {
            Arena.Bounds = A12NAlexanderPrime.MainBounds;
            Arena.Center = default;
        }
    }
}

sealed class A12NAlexanderPrimeStates : StateMachineBuilder
{
    public A12NAlexanderPrimeStates(BossModule module) : base(module)
    {
        TrivialPhase()
            .ActivateOnEnter<MegaHoly>()
            .ActivateOnEnter<PunishingHeat>()
            .ActivateOnEnter<BlazingScourge>()
            .ActivateOnEnter<IncineratingHeat>()
            .ActivateOnEnter<Sacrament>()
            .ActivateOnEnter<TimegateRooms>()
            .ActivateOnEnter<Components.UnclaimedCastAOEs>();
    }
}

// 場地＝台服碰撞在 (0,0) 的圓形地板（y 400，半徑約 26.5）放得下的最大圓，與既有障礙物圖 583.189 的可走圓一致。
public abstract class A12NAlexanderPrime(WorldState ws, Actor primary) : UnverifiedOidTrialModule(ws, primary, default, MainBounds)
{
    public static readonly ArenaBoundsCircle MainBounds = new(26f);
}

[ModuleInfo(BossModuleInfo.Maturity.WIP, StatesType = typeof(A12NAlexanderPrimeStates), PrimaryActorOID = (uint)OID.BossCandidateA, Contributors = "ffxiv-tc-port", Expansion = BossModuleInfo.Expansion.Heavensward, Category = BossModuleInfo.Category.Raid, GroupType = BossModuleInfo.GroupType.CFC, GroupID = 189u, NameID = 5193u, SortOrder = 1)]
public sealed class A12NAlexanderPrimeA(WorldState ws, Actor primary) : A12NAlexanderPrime(ws, primary);

[ModuleInfo(BossModuleInfo.Maturity.WIP, StatesType = typeof(A12NAlexanderPrimeStates), PrimaryActorOID = (uint)OID.BossCandidateB, Contributors = "ffxiv-tc-port", Expansion = BossModuleInfo.Expansion.Heavensward, Category = BossModuleInfo.Category.Raid, GroupType = BossModuleInfo.GroupType.CFC, GroupID = 189u, NameID = 5193u, SortOrder = 1)]
public sealed class A12NAlexanderPrimeB(WorldState ws, Actor primary) : A12NAlexanderPrime(ws, primary);

[ModuleInfo(BossModuleInfo.Maturity.WIP, StatesType = typeof(A12NAlexanderPrimeStates), PrimaryActorOID = (uint)OID.BossCandidateC, Contributors = "ffxiv-tc-port", Expansion = BossModuleInfo.Expansion.Heavensward, Category = BossModuleInfo.Category.Raid, GroupType = BossModuleInfo.GroupType.CFC, GroupID = 189u, NameID = 5193u, SortOrder = 1)]
public sealed class A12NAlexanderPrimeC(WorldState ws, Actor primary) : A12NAlexanderPrime(ws, primary);
