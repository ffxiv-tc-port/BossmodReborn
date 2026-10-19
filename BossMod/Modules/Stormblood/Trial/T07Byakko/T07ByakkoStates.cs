namespace BossMod.Stormblood.Trial.T07Byakko;

class T07ByakkoStates : StateMachineBuilder
{
    public T07ByakkoStates(BossModule module) : base(module)
    {
        TrivialPhase()
            // 🔴 場地邊界切換，放第一個：模組宣告的邊界就是 AI 認定的可走範圍，沒有它的話 AI 會在
            //    空中階段把人帶進「模組以為有、實際沒有」的那 5 碼地板摔死。
            .ActivateOnEnter<ArenaChange>()
            .ActivateOnEnter<StormPulse>()
            .ActivateOnEnter<HeavenlyStrike>()
            .ActivateOnEnter<HeavenlyStrikeSpread>()
            .ActivateOnEnter<SweepTheLeg1>()
            .ActivateOnEnter<SweepTheLeg3>()
            .ActivateOnEnter<TheRoarOfThunder>()
            // 空中階段白帝段的近身危險 —— 見 T07Byakko.cs 的說明。
            .ActivateOnEnter<SteelClaw>()
            .ActivateOnEnter<ImperialGuard>()
            // The icon lands on a helper actor, so this is a tower (GenericTowers, like Ex6Byakko), cleared on
            // the 10806 cast event.
            .ActivateOnEnter<HighestStakes>()
            .ActivateOnEnter<FireAndLightning>()
            .ActivateOnEnter<DistantClap>()
            // Line parameters are measured from TC replays - see the comment on HundredfoldHavoc in T07Byakko.cs.
            .ActivateOnEnter<HundredfoldHavoc>()
            // 荒彈: the cast-bar variant (10793) and the locked-on rain (10817 telegraph -> 10818 detonation).
            .ActivateOnEnter<Aratama>()
            .ActivateOnEnter<AratamaRain>()
            .ActivateOnEnter<AratamaForce>();
        // AratamaForce (OID 0x20F9) never spawns in the TC replays; its Voidzone is kept anyway (absence is
        // not proof it cannot happen). Deliberately no component, for lack of replay data to draw them from:
        // StateOfShock (10070/10208), WhiteHerald (10828), FellSwoop (10829), Bombogenesis2 (10811) and
        // UnrelentingAnguish (10221).
    }
}
