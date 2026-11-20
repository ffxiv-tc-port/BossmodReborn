namespace BossMod.Shadowbringers.Raid.E11NFatebreaker;

sealed class BurnishedGlory(BossModule module) : Components.RaidwideCast(module, (uint)AID.BurnishedGlory);
sealed class PowderMark(BossModule module) : Components.SingleTargetCast(module, (uint)AID.PowderMark);

// 爆印：狀態結束時以身上帶狀態的人為中心爆炸（半徑 10），其他人要離開。
sealed class BurnMark(BossModule module) : Components.UniformStackSpread(module, default, 10f, alwaysShowSpreads: true, raidwideOnResolve: false)
{
    public override void OnStatusGain(Actor actor, ActorStatus status)
    {
        if (status.ID == (uint)SID.PowderMark)
        {
            AddSpread(actor, status.ExpireAt);
        }
    }

    public override void OnStatusLose(Actor actor, ActorStatus status)
    {
        if (status.ID == (uint)SID.PowderMark)
        {
            Spreads.RemoveAll(s => s.Target == actor);
        }
    }
}

// 罪壤刺：被連線的人在讀條結束約 3.4 秒後吃罪炎（火＝全隊分攤）或罪光（光＝散開），雷版是單體傷害不用處理。
// 連線與讀條誰先到沒有證據，兩種順序都接受。
// 全隊分攤最少與最多都設 8：最少小於最多時，圈外的人會同時收到「進圈」與「別進圈」兩個禁區。
sealed class BoundOfFaith(BossModule module) : Components.UniformStackSpread(module, 6f, 6f, 8, 8, alwaysShowSpreads: true, raidwideOnResolve: false)
{
    private Actor? _tethered;
    private uint _pending;
    private DateTime _activation;

    public override void OnTethered(Actor source, ActorTetherInfo tether)
    {
        if (tether.ID is not ((uint)TetherID.BoundOfFaithA or (uint)TetherID.BoundOfFaithB or (uint)TetherID.BoundOfFaithC))
        {
            return;
        }
        var player = Raid.FindSlot(source.InstanceID) >= 0 ? source : Raid.FindSlot(tether.Target) >= 0 ? WorldState.Actors.Find(tether.Target) : null;
        if (player != null)
        {
            _tethered = player;
            TryResolve();
        }
    }

    public override void OnUntethered(Actor source, ActorTetherInfo tether)
    {
        if (_tethered != null && (_tethered == source || _tethered.InstanceID == tether.Target))
        {
            _tethered = null;
        }
    }

    public override void OnCastStarted(Actor caster, ActorCastInfo spell)
    {
        if (spell.Action.ID is (uint)AID.BoundOfFaithFire or (uint)AID.BoundOfFaithHoly)
        {
            _pending = spell.Action.ID;
            _activation = Module.CastFinishAt(spell, 3.4d);
            TryResolve();
        }
    }

    public override void OnEventCast(Actor caster, ActorCastEvent spell)
    {
        if (spell.Action.ID == (uint)AID.Sinsmoke)
        {
            Stacks.Clear();
        }
        else if (spell.Action.ID == (uint)AID.Sinsight)
        {
            Spreads.Clear();
        }
    }

    private void TryResolve()
    {
        if (_pending == default || _tethered is not { } target)
        {
            return;
        }
        if (_pending == (uint)AID.BoundOfFaithFire)
        {
            AddStack(target, _activation);
        }
        else
        {
            AddSpread(target, _activation);
        }
        _pending = default;
        _tethered = null;
    }
}

// 火燃爆（燃燒擊火版之後的擊退）寬 50 大於場地直徑 40、全場都會被推，距離沒有證據，刻意不處理；
// 列進技能列舉只為了不讓後備把它當成寬 50 的直線傷害畫出來（它沒有預兆）。
// 燃燒擊／雷燃爆／光明之刃／光炎／魔亂流照讀條預兆交給後備；爆破領域、至天絕技的擊退、急衝刺沒有讀條或證據，不處理。
sealed class E11NFatebreakerStates : StateMachineBuilder
{
    public E11NFatebreakerStates(BossModule module) : base(module)
    {
        TrivialPhase()
            .ActivateOnEnter<BurnishedGlory>()
            .ActivateOnEnter<PowderMark>()
            .ActivateOnEnter<BurnMark>()
            .ActivateOnEnter<BoundOfFaith>()
            .ActivateOnEnter<Components.UnclaimedCastAOEs>();
    }
}

// 場地＝台服碰撞在 (100,100) 的圓形地板（半徑 20），內縮 0.5 碼；被擊退出場會掉下去。
public abstract class E11NFatebreaker(WorldState ws, Actor primary) : UnverifiedOidTrialModule(ws, primary, new(100f, 100f), new ArenaBoundsCircle(19.5f));

[ModuleInfo(BossModuleInfo.Maturity.WIP, StatesType = typeof(E11NFatebreakerStates), PrimaryActorOID = (uint)OID.BossCandidateA, Contributors = "ffxiv-tc-port", Expansion = BossModuleInfo.Expansion.Shadowbringers, Category = BossModuleInfo.Category.Raid, GroupType = BossModuleInfo.GroupType.CFC, GroupID = 751u, NameID = 9707u, SortOrder = 1)]
public sealed class E11NFatebreakerA(WorldState ws, Actor primary) : E11NFatebreaker(ws, primary);

[ModuleInfo(BossModuleInfo.Maturity.WIP, StatesType = typeof(E11NFatebreakerStates), PrimaryActorOID = (uint)OID.BossCandidateB, Contributors = "ffxiv-tc-port", Expansion = BossModuleInfo.Expansion.Shadowbringers, Category = BossModuleInfo.Category.Raid, GroupType = BossModuleInfo.GroupType.CFC, GroupID = 751u, NameID = 9707u, SortOrder = 1)]
public sealed class E11NFatebreakerB(WorldState ws, Actor primary) : E11NFatebreaker(ws, primary);
