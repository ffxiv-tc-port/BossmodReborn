namespace BossMod.Endwalker.Raid.P10NPandaemonium;

sealed class Ultima(BossModule module) : Components.RaidwideCast(module, (uint)AID.Ultima);
sealed class Silkspit(BossModule module) : Components.SpreadFromIcon(module, (uint)IconID.Silkspit, (uint)AID.SilkspitAOE, 7f, 8d);
sealed class PandaemoniacPillars(BossModule module) : Components.CastTowers(module, (uint)AID.Bury, 2f);
sealed class Imprisonment(BossModule module) : Components.SimpleAOEs(module, (uint)AID.ImprisonmentAOE, 4f);
sealed class Cannonspawn(BossModule module) : Components.SimpleAOEs(module, (uint)AID.CannonspawnAOE, new AOEShapeDonut(3f, 7f));
sealed class PandaemoniacRay(BossModule module) : Components.SimpleAOEs(module, (uint)AID.PandaemoniacRayAOE, new AOEShapeRect(30f, 25f));
sealed class PartedPlumes(BossModule module) : Components.SimpleAOEs(module, (uint)AID.PartedPlumesAOE, new AOEShapeCone(50f, 10f.Degrees()));
sealed class SoulGrasp(BossModule module) : Components.CastSharedTankbuster(module, (uint)AID.SoulGrasp, 4f);

// 尖腳的塔只給坦克踩，其他人避開；零式那一下附帶的擊退在一般難度沒有證據，刻意不畫。
sealed class WickedStep(BossModule module) : Components.GenericTowers(module)
{
    private readonly BitMask _nonTanks = GetNonTanks(module);

    private static BitMask GetNonTanks(BossModule module)
    {
        var party = module.Raid.WithSlot(true, true, true);
        BitMask mask = default;
        for (var i = 0; i < party.Length; ++i)
            if (party[i].Item2.Role != Role.Tank)
                mask[party[i].Item1] = true;
        return mask;
    }

    public override void OnCastStarted(Actor caster, ActorCastInfo spell)
    {
        if (spell.Action.ID is (uint)AID.WickedStepAOE1 or (uint)AID.WickedStepAOE2)
            Towers.Add(new(spell.LocXZ, 4f, forbiddenSoakers: _nonTanks, activation: Module.CastFinishAt(spell), actorID: caster.InstanceID));
    }

    public override void OnCastFinished(Actor caster, ActorCastInfo spell)
    {
        if (spell.Action.ID is (uint)AID.WickedStepAOE1 or (uint)AID.WickedStepAOE2)
        {
            var id = caster.InstanceID;
            for (var i = Towers.Count - 1; i >= 0; --i)
                if (Towers[i].ActorID == id)
                    Towers.RemoveAt(i);
            ++NumCasts;
        }
    }
}

sealed class P10NPandaemoniumStates : StateMachineBuilder
{
    public P10NPandaemoniumStates(BossModule module) : base(module)
    {
        TrivialPhase()
            .ActivateOnEnter<Ultima>()
            .ActivateOnEnter<Silkspit>()
            .ActivateOnEnter<PandaemoniacPillars>()
            .ActivateOnEnter<Imprisonment>()
            .ActivateOnEnter<Cannonspawn>()
            .ActivateOnEnter<PandaemoniacRay>()
            .ActivateOnEnter<PartedPlumes>()
            .ActivateOnEnter<SoulGrasp>()
            .ActivateOnEnter<WickedStep>()
            .ActivateOnEnter<Components.UnclaimedCastAOEs>();
    }
}

// 場地＝中央與兩側平台（＝靜態碰撞地板，平台之間沒有地板）；橋只在中途出現，一般難度的出現時機沒有證據，刻意不放進場地。
// 空降（33385）因此也不認領：要 AI 離開中央平台卻沒有橋可走，比交給後備猜法更糟。
[ModuleInfo(BossModuleInfo.Maturity.WIP, Contributors = "ffxiv-tc-port", GroupType = BossModuleInfo.GroupType.CFC, GroupID = 938, NameID = 12354)]
public sealed class P10NPandaemonium(WorldState ws, Actor primary) : BossModule(ws, primary, DefaultArena.Center, DefaultArena)
{
    private static readonly Rectangle[] union = [new(new(100f, 100f), 13f, 15f), new(new(125f, 85f), 4f, 15f), new(new(75f, 85f), 4f, 15f)];
    public static readonly ArenaBoundsComplex DefaultArena = new(union);
}
