namespace BossMod.RealmReborn.Dungeon.D27SastashaHard.D273Kraken;

public enum OID : uint
{
    Boss = 0xCC0, // 克拉肯
    Tentacle = 0xCC1, // 克拉肯的觸手
    KrakensArm = 0xCC2 // 克拉肯的腕足
}

// 沒有列技能：噴墨 3069（扇形 120°）、墨點 3070（圓形）都有 Omen，後備猜法已能照 EXD 畫出，交給 UnclaimedCastAOEs。
sealed class D273KrakenStates : StateMachineBuilder
{
    public D273KrakenStates(BossModule module) : base(module)
    {
        TrivialPhase()
            .ActivateOnEnter<Components.UnclaimedCastAOEs>();
    }
}

// 場地與尋路沿用沒有模組時的做法（跟著玩家走的方框）：這個副本沒有障礙物圖，也沒有錄影可量場地。
[ModuleInfo(BossModuleInfo.Maturity.WIP, Contributors = "ffxiv-tc-port", GroupType = BossModuleInfo.GroupType.CFC, GroupID = 28, NameID = 2904)]
public sealed class D273Kraken(WorldState ws, Actor primary) : SimpleBossModule(ws, primary)
{
    // 腕足在場時先打腕足，觸手一律不打。
    protected override void CalculateModuleAIHints(int slot, Actor actor, PartyRolesConfig.Assignment assignment, AIHints hints)
    {
        base.CalculateModuleAIHints(slot, actor, assignment, hints);
        var count = hints.PotentialTargets.Count;
        for (var i = 0; i < count; ++i)
        {
            var e = hints.PotentialTargets[i];
            switch (e.Actor.OID)
            {
                case (uint)OID.KrakensArm:
                    e.Priority = 1;
                    break;
                case (uint)OID.Tentacle:
                    e.Priority = AIHints.Enemy.PriorityPointless;
                    break;
            }
        }
    }

    protected override void DrawEnemies(int pcSlot, Actor pc)
    {
        Arena.Actor(PrimaryActor);
        Arena.Actors(Enemies((uint)OID.KrakensArm));
        Arena.Actors(Enemies((uint)OID.Tentacle), Colors.Object);
    }
}
