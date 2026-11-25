namespace BossMod.Stormblood.Trial.T02Lakshmi;

class DivineDenial(BossModule module) : Components.RaidwideCast(module, (uint)AID.DivineDenial);
class Stotram1(BossModule module) : Components.RaidwideCast(module, (uint)AID.Stotram1);
class Stotram2(BossModule module) : Components.RaidwideCast(module, (uint)AID.Stotram2);
// 心魔的 Action 帶凝視特效，但背對施放者照樣被打中 ⇒ 當圓形範圍閃，不當凝視。
class InnerDemons(BossModule module) : Components.SimpleAOEs(module, (uint)AID.InnerDemons, 7f);
class ThePallOfLightStack(BossModule module) : Components.StackWithCastTargets(module, (uint)AID.ThePallOfLightStack, 7, 8, 8);
class ThePullOfLightTB1(BossModule module) : Components.SingleTargetCast(module, (uint)AID.ThePullOfLightTB1);
class ThePullOfLightTB2(BossModule module) : Components.SingleTargetCast(module, (uint)AID.ThePullOfLightTB2);
class EmanationTrash(BossModule module) : Components.Adds(module, (uint)OID.DreamingKshatriya);
// 血池實際半徑量不準（站 10 碼外沒中、4.7 碼中）；照上游取 10、寧可大。
class CircleZone(BossModule module) : Components.Voidzone(module, 10f, m => m.Enemies((uint)OID.VoidZone));

static class VrilAction
{
    public static readonly ActionID Vril = ActionID.MakeSpell(ClassShared.AID.Vril);

    // 只有一格充能；身上已有元氣狀態時再按＝浪費掉下一次要用的那格。
    public static void PushIfReady(BossModule module, Actor actor, AIHints hints)
    {
        if (module.WorldState.Client.DutyActions[0].CurCharges > 0 && actor.FindStatus((uint)SID.Vril) == null)
            hints.ActionsToExecute.Push(Vril, actor, ActionQueue.Priority.High);
    }
}

// 有元氣時擊退約 4～5 碼；沒有元氣會被擊出場外，所以照有元氣的距離畫、讀條期間自動按元氣。
class DivineDenialKnockback(BossModule module) : Components.SimpleKnockbacks(module, (uint)AID.DivineDenial, 6.5f)
{
    public override void AddAIHints(int slot, Actor actor, PartyRolesConfig.Assignment assignment, AIHints hints)
    {
        if (Casters.Count == 0)
            return;
        VrilAction.PushIfReady(Module, actor, hints);
        // 擊退元件本身不出 AI 提示；從場地中心推 6.5 碼，站在半徑 13 以內才不會被推出 20 碼的場地。
        hints.AddForbiddenZone(ShapeDistance.InvertedCircle(Arena.Center, 13f), Casters[0].Activation);
    }
}

// 至上天母之後先按元氣再吃魅惑擁抱（錄影裡全隊都這樣做）。讚歌（9374）刻意不按：只有一格，接著的完全拒絕要用。
class JagadishwariVril(BossModule module) : BossComponent(module)
{
    private bool _active;

    public override void OnEventCast(Actor caster, ActorCastEvent spell)
    {
        if (spell.Action.ID == (uint)AID.Jagadishwari)
            _active = true;
        else if (spell.Action.ID == (uint)AID.AlluringEmbrace2)
            _active = false;
    }

    public override void AddAIHints(int slot, Actor actor, PartyRolesConfig.Assignment assignment, AIHints hints)
    {
        if (_active)
            VrilAction.PushIfReady(Module, actor, hints);
    }
}

// 元氣充能用完、身上也沒有元氣時，走去碰一顆元氣球補充。
class VrilOrbs(BossModule module) : BossComponent(module)
{
    private readonly List<Actor> _orbs = module.Enemies((uint)OID.Vril);

    public override void AddAIHints(int slot, Actor actor, PartyRolesConfig.Assignment assignment, AIHints hints)
    {
        if (WorldState.Client.DutyActions[0].CurCharges > 0 || actor.FindStatus((uint)SID.Vril) != null)
            return;
        var count = _orbs.Count;
        var zones = new List<Func<WPos, float>>(count);
        for (var i = 0; i < count; ++i)
        {
            var o = _orbs[i];
            if (!o.IsDead)
                zones.Add(ShapeDistance.Circle(o.Position, 1f));
        }
        if (zones.Count != 0)
            hints.AddForbiddenZone(ShapeDistance.InvertedUnion(zones), DateTime.MaxValue);
    }

    public override void DrawArenaForeground(int pcSlot, Actor pc)
    {
        var count = _orbs.Count;
        for (var i = 0; i < count; ++i)
        {
            var o = _orbs[i];
            if (!o.IsDead)
                Arena.AddCircle(o.Position, 1f, Colors.Safe);
        }
    }
}

// 光之波動：點名後約 5.2 秒朝被點名的人放扇形；錄影實測命中角至少 47 度，半角取 60 度。
class PathOfLight(BossModule module) : Components.GenericBaitAway(module)
{
    private static readonly AOEShapeCone cone = new(43.5f, 60f.Degrees());

    public override void OnEventIcon(Actor actor, uint iconID, ulong targetID)
    {
        if (iconID == (uint)IconID.Protean)
            CurrentBaits.Add(new(Module.PrimaryActor, actor, cone, WorldState.FutureTime(5.2d)));
    }

    public override void OnEventCast(Actor caster, ActorCastEvent spell)
    {
        if (spell.Action.ID is (uint)AID.ThePathOfLightProtean or (uint)AID.ThePathOfLightCleave)
        {
            ++NumCasts;
            CurrentBaits.Clear();
        }
    }
}

// 聖槍（十字、正南北向）點名後約 12.7 秒、聖錘（圓 7，落地留血池）約 12.1 秒，都落在被點名的人當下的位置。
class BlissfulBaits(BossModule module) : Components.GenericBaitAway(module)
{
    private static readonly AOEShapeCross cross = new(40f, 4f);
    private static readonly AOEShapeCircle circle = new(7f);

    public override void OnEventIcon(Actor actor, uint iconID, ulong targetID)
    {
        if (iconID == (uint)IconID.Spread1)
            CurrentBaits.Add(new(actor, actor, cross, WorldState.FutureTime(12.7d), customRotation: default(Angle)));
        else if (iconID == (uint)IconID.Spread2)
            CurrentBaits.Add(new(actor, actor, circle, WorldState.FutureTime(12.1d)));
    }

    public override void OnEventCast(Actor caster, ActorCastEvent spell)
    {
        AOEShape? shape = spell.Action.ID switch
        {
            (uint)AID.BlissfulSpear1 or (uint)AID.BlissfulSpear2 => cross,
            (uint)AID.BlissfulSpear3 or (uint)AID.BlissfulSpear4 => circle,
            _ => null
        };
        if (shape != null)
        {
            ++NumCasts;
            CurrentBaits.RemoveAll(b => b.Shape == shape);
        }
    }
}

[ModuleInfo(BossModuleInfo.Maturity.WIP, Contributors = "The Combat Reborn Team, wen", GroupType = BossModuleInfo.GroupType.CFC, GroupID = 263, NameID = 6385)]
public class T02Lakshmi(WorldState ws, Actor primary) : BossModule(ws, primary, default, new ArenaBoundsCircle(20f))
{
    // 吉祥天女無敵、不可選取時夢寐的剎帝利已經開打；等她可選取才啟用的話，那段的心魔只剩後備猜測（帶凝視特效、被猜成凝視、不畫範圍）。
    protected override bool CheckPull()
    {
        if (PrimaryActor.IsTargetable && PrimaryActor.InCombat)
            return true;
        var adds = Enemies((uint)OID.DreamingKshatriya);
        var count = adds.Count;
        for (var i = 0; i < count; ++i)
        {
            if (adds[i].InCombat)
                return true;
        }
        return false;
    }
}
