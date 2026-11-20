namespace BossMod.RealmReborn.Dungeon.D20BrayfloxHard.D203GobmachineGVI;

public enum OID : uint
{
    Boss = 0xAEC // R?
}

public enum AID : uint
{
    FlameThrower = 1931 // Boss->self, no cast, range 8+R 120-degree cone
}

// 錐形長度＝EffectRange 8＋王的 hitbox（CastType 3 的慣例），hitbox 沒有錄影可證，取執行期的值。
sealed class FlameThrower(BossModule module) : Components.Cleave(module, (uint)AID.FlameThrower, new AOEShapeCone(8f + module.PrimaryActor.HitboxRadius, 60f.Degrees()));

sealed class D203GobmachineGVIStates : StateMachineBuilder
{
    public D203GobmachineGVIStates(BossModule module) : base(module)
    {
        TrivialPhase()
            .ActivateOnEnter<FlameThrower>()
            .ActivateOnEnter<Components.UnclaimedCastAOEs>();
    }
}

// 沒有錄影：場地＝碰撞地板裡放得下的最大圓（比房間小，保守側）；讀條範圍技全交給 UnclaimedCastAOEs。
[ModuleInfo(BossModuleInfo.Maturity.WIP, Contributors = "ffxiv-tc-port", GroupType = BossModuleInfo.GroupType.CFC, GroupID = 20, NameID = 2547)]
public sealed class D203GobmachineGVI(WorldState ws, Actor primary) : BossModule(ws, primary, new(-28.5f, -238.5f), new ArenaBoundsCircle(18f));
