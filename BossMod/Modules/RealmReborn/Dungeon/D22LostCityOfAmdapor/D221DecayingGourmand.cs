namespace BossMod.RealmReborn.Dungeon.D22LostCityOfAmdapor.D221DecayingGourmand;

public enum OID : uint
{
    Boss = 0xA80, // R2.85
    GluttonousMaw = 0xA81 // R2.85, appears when a player is devoured; killing it frees the player
}

// 被吞的人每秒掉血、只能靠隊友打掉貪婪大口放出來 ⇒ 它在場時 AI 先打它。
sealed class GluttonousMaw(BossModule module) : BossComponent(module)
{
    public override void AddAIHints(int slot, Actor actor, PartyRolesConfig.Assignment assignment, AIHints hints)
        => hints.PrioritizeTargetsByOID((uint)OID.GluttonousMaw, 1);
}

sealed class D221DecayingGourmandStates : StateMachineBuilder
{
    public D221DecayingGourmandStates(BossModule module) : base(module)
    {
        TrivialPhase()
            .ActivateOnEnter<GluttonousMaw>()
            .ActivateOnEnter<Components.UnclaimedCastAOEs>();
    }
}

// 場地＝碰撞地板裡放得下的最大圓（比房間小，保守側）；讀條範圍技全交給 UnclaimedCastAOEs。
// 刻意不做獵物（引誘劑）的傳遞站位：只有狀態說明、錄影只有一次吞食，不足以寫規則。
[ModuleInfo(BossModuleInfo.Maturity.Contributed, Contributors = "ffxiv-tc-port", GroupType = BossModuleInfo.GroupType.CFC, GroupID = 22, NameID = 2556)]
public sealed class D221DecayingGourmand(WorldState ws, Actor primary) : BossModule(ws, primary, new(98f, -28f), new ArenaBoundsCircle(20f));
