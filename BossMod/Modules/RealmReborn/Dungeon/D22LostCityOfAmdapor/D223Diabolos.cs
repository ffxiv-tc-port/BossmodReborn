namespace BossMod.RealmReborn.Dungeon.D22LostCityOfAmdapor.D223Diabolos;

public enum OID : uint
{
    Boss = 0xA7C // R?
}

public enum AID : uint
{
    RuinousOmen = 1893, // Boss->self, 15.0s cast, raidwide; avoided by the Voidbound status from going through a matching door pair
    UltimateTerror = 1894 // Boss->self, 4.0s cast, ring around the boss: safe directly under the boss or far away
}

sealed class RuinousOmen(BossModule module) : Components.CastHint(module, (uint)AID.RuinousOmen, "Go through a matching door pair (Voidbound)!", true);

// Action 表只寫圓形半徑 20、沒有預兆；攻略說王腳下與遠處安全 ⇒ 甜甜圈。
// 內徑沒有資料，取王的 hitbox（安全洞只會比實際小）；外徑加上 hitbox（危險區只會比實際大）。
sealed class UltimateTerror(BossModule module) : Components.SimpleAOEs(module, (uint)AID.UltimateTerror, new AOEShapeDonut(module.PrimaryActor.HitboxRadius, 20f + module.PrimaryActor.HitboxRadius));

sealed class D223DiabolosStates : StateMachineBuilder
{
    public D223DiabolosStates(BossModule module) : base(module)
    {
        TrivialPhase()
            .ActivateOnEnter<RuinousOmen>()
            .ActivateOnEnter<UltimateTerror>()
            .ActivateOnEnter<Components.UnclaimedCastAOEs>();
    }
}

// 沒有錄影：場地＝碰撞地板裡放得下的最大圓（比房間小，保守側）；其餘讀條範圍技交給 UnclaimedCastAOEs。
[ModuleInfo(BossModuleInfo.Maturity.WIP, Contributors = "ffxiv-tc-port", GroupType = BossModuleInfo.GroupType.CFC, GroupID = 22, NameID = 2564)]
public sealed class D223Diabolos(WorldState ws, Actor primary) : BossModule(ws, primary, default, new ArenaBoundsCircle(23f));
