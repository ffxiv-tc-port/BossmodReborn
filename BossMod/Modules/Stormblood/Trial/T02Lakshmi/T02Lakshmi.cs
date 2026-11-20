namespace BossMod.Stormblood.Trial.T02Lakshmi;

class DivineDenial(BossModule module) : Components.RaidwideCast(module, (uint)AID.DivineDenial);
class Stotram1(BossModule module) : Components.RaidwideCast(module, (uint)AID.Stotram1);
class Stotram2(BossModule module) : Components.RaidwideCast(module, (uint)AID.Stotram2);
// 心魔的 Action 帶凝視特效，但背對施放者照樣被打中 ⇒ 當圓形範圍閃，不當凝視。
class InnerDemons(BossModule module) : Components.SimpleAOEs(module, (uint)AID.InnerDemons, 7f);
class ThePallOfLightStack(BossModule module) : Components.StackWithCastTargets(module, (uint)AID.ThePallOfLightStack, 7, 8, 8);
class ThePullOfLightTB1(BossModule module) : Components.SingleTargetCast(module, (uint)AID.ThePullOfLightTB1);
class ThePullOfLightTB2(BossModule module) : Components.SingleTargetCast(module, (uint)AID.ThePullOfLightTB2);
class BlissfulArrow1(BossModule module) : Components.CastCounter(module, (uint)AID.BlissfulArrow1);
class BlissfulArrow2(BossModule module) : Components.CastCounter(module, (uint)AID.BlissfulArrow2);
class BlissfulSpear1(BossModule module) : Components.CastCounter(module, (uint)AID.BlissfulSpear1);
class BlissfulSpear2(BossModule module) : Components.CastCounter(module, (uint)AID.BlissfulSpear2);
class BlissfulSpear3(BossModule module) : Components.CastCounter(module, (uint)AID.BlissfulSpear3);
class BlissfulSpear4(BossModule module) : Components.CastCounter(module, (uint)AID.BlissfulSpear4);

class BlissfulSpear(BossModule module) : Components.BaitAwayIcon(module, 7f, (uint)IconID.Spread1, (uint)AID.BlissfulSpear1, 3f)
{
    public override void OnEventCast(Actor caster, ActorCastEvent spell)
    {
        base.OnEventCast(caster, spell);
        if (spell.Action.ID == WatchedAction)
            CurrentBaits.Clear();
    }
}

class BlissfulArrow(BossModule module) : Components.BaitAwayIcon(module, 7f, (uint)IconID.Spread2, (uint)AID.BlissfulArrow2, 3f)
{
    public override void OnEventCast(Actor caster, ActorCastEvent spell)
    {
        base.OnEventCast(caster, spell);
        if (spell.Action.ID == WatchedAction)
            CurrentBaits.Clear();
    }
}

[ModuleInfo(BossModuleInfo.Maturity.WIP, Contributors = "The Combat Reborn Team", GroupType = BossModuleInfo.GroupType.CFC, GroupID = 263, NameID = 6385)]
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
