namespace BossMod.Heavensward.Alliance.A24Ozma;

class A24OzmaStates : StateMachineBuilder
{
    public A24OzmaStates(BossModule module) : base(module)
    {
        // 首領消失後只要人還在主場地或分割／奧斯瑪之影那一區就不結束（同上游）；人離開這些區域或離開副本才結束。
        bool IsWipedOrLeftRaid() => module.WorldState.CurrentCFCID != 168u
            || module.Raid.Player() is { } p && !(p.Position.InSquare(new(300f, 265f), 80f) || p.Position.InSquare(new(280f, -404.5f), 40f));
        TrivialPhase()
            .ActivateOnEnter<ArenaChanges>()
            .ActivateOnEnter<MeteorImpact>()
            .ActivateOnEnter<HolyKB>()
            .ActivateOnEnter<Holy>()
            .ActivateOnEnter<ExecrationAOE>()
            .ActivateOnEnter<AccelerationBomb>()
            .Raw.Update = () => module.PrimaryActor.IsDestroyed && IsWipedOrLeftRaid();
    }
}
