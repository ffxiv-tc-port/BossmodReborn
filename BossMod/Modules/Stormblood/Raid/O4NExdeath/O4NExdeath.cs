namespace BossMod.Stormblood.Raid.O4NExdeath;

sealed class Meteor(BossModule module) : Components.RaidwideCast(module, (uint)AID.Meteor);
sealed class ThunderIII(BossModule module) : Components.SingleTargetCast(module, (uint)AID.ThunderIII);
sealed class FireIIISpread(BossModule module) : Components.SpreadFromCastTargets(module, (uint)AID.FireIIISpread, 4f);
sealed class Doom(BossModule module) : Components.CleansableDebuff(module, (uint)SID.Doom);

// 神聖：頭上標記 0x3E 的人吃半徑 6 的分攤。標記到判定的秒數沒有證據，取 5.1；判定沒看到時過了預定時間 10 秒自動清掉。
sealed class Holy(BossModule module) : Components.UniformStackSpread(module, 6f, default)
{
    public override void OnEventIcon(Actor actor, uint iconID, ulong targetID)
    {
        if (iconID == (uint)IconID.Holy)
        {
            AddStack(actor, WorldState.FutureTime(5.1d));
        }
    }

    public override void OnEventCast(Actor caster, ActorCastEvent spell)
    {
        if (spell.Action.ID == (uint)AID.Holy)
        {
            var i = Stacks.FindIndex(s => s.Target.InstanceID == spell.MainTargetID);
            if (Stacks.Count > 0)
            {
                Stacks.RemoveAt(Math.Max(i, 0));
            }
        }
    }

    public override void Update()
    {
        var expired = WorldState.FutureTime(-10d);
        Stacks.RemoveAll(s => s.Activation < expired || s.Target.IsDead);
    }
}

// 大火焰（強化）讀條結束上熱病＝別動、別出手；大暴雪（強化）讀條結束時站著不動會被凍結＝要移動。
// 契約：別動的要求只在讀條結束後 1.5 秒內、或身上還有熱病時成立，狀態 ID 對不上也不會讓 AI 一直站著不動。
sealed class EmpoweredFireBlizzard(BossModule module) : Components.StayMove(module)
{
    public override void OnCastStarted(Actor caster, ActorCastInfo spell)
    {
        var req = spell.Action.ID switch
        {
            (uint)AID.EmpoweredFire => Requirement.Stay,
            (uint)AID.EmpoweredBlizzard => Requirement.Move,
            _ => Requirement.None
        };
        if (req != Requirement.None)
        {
            Array.Fill(PlayerStates, new(req, Module.CastFinishAt(spell)));
        }
    }

    public override void OnEventCast(Actor caster, ActorCastEvent spell)
    {
        if (spell.Action.ID == (uint)AID.EmpoweredBlizzard)
        {
            Array.Fill(PlayerStates, default);
        }
    }

    public override void OnStatusLose(Actor actor, ActorStatus status)
    {
        if (status.ID == (uint)SID.Pyretic)
        {
            ClearState(Raid.FindSlot(actor.InstanceID));
        }
    }

    public override void Update()
    {
        var stale = WorldState.FutureTime(-1.5d);
        for (var slot = 0; slot < PlayerStates.Length; ++slot)
        {
            ref readonly var s = ref PlayerStates[slot];
            if (s.Requirement != Requirement.None && s.Activation < stale && Raid[slot]?.FindStatus((uint)SID.Pyretic) == null)
            {
                PlayerStates[slot] = default;
            }
        }
    }
}

// 真空波（從首領的擊退）範圍 80 涵蓋全場、距離沒有證據，刻意不處理；黑洞、決戰後的頭與藤蔓位置、火光標記的範圍沒有證據，不處理。
// 大暴雪（地點圓 6）、大雷電（強化，圓 11）、橫掃（扇形 120）照讀條預兆交給後備；殭屍吐息沒有預兆，後備畫成 180 度（比實際寬，安全側）。
sealed class O4NExdeathStates : StateMachineBuilder
{
    public O4NExdeathStates(BossModule module) : base(module)
    {
        TrivialPhase()
            .ActivateOnEnter<Meteor>()
            .ActivateOnEnter<ThunderIII>()
            .ActivateOnEnter<FireIIISpread>()
            .ActivateOnEnter<Doom>()
            .ActivateOnEnter<Holy>()
            .ActivateOnEnter<EmpoweredFireBlizzard>()
            .ActivateOnEnter<Components.UnclaimedCastAOEs>();
    }
}

// 場地＝台服碰撞在原點的圓形地板（半徑約 20），內縮 0.5 碼；攻略寫後半場牆壞掉後會被擊退出場摔死。
public abstract class O4NExdeath(WorldState ws, Actor primary) : UnverifiedOidTrialModule(ws, primary, default, new ArenaBoundsCircle(19.5f));

[ModuleInfo(BossModuleInfo.Maturity.WIP, StatesType = typeof(O4NExdeathStates), PrimaryActorOID = (uint)OID.BossCandidateA, Contributors = "ffxiv-tc-port", Expansion = BossModuleInfo.Expansion.Stormblood, Category = BossModuleInfo.Category.Raid, GroupType = BossModuleInfo.GroupType.CFC, GroupID = 255u, NameID = 6052u, SortOrder = 1)]
public sealed class O4NExdeathA(WorldState ws, Actor primary) : O4NExdeath(ws, primary);

[ModuleInfo(BossModuleInfo.Maturity.WIP, StatesType = typeof(O4NExdeathStates), PrimaryActorOID = (uint)OID.BossCandidateB, Contributors = "ffxiv-tc-port", Expansion = BossModuleInfo.Expansion.Stormblood, Category = BossModuleInfo.Category.Raid, GroupType = BossModuleInfo.GroupType.CFC, GroupID = 255u, NameID = 6052u, SortOrder = 1)]
public sealed class O4NExdeathB(WorldState ws, Actor primary) : O4NExdeath(ws, primary);
