namespace BossMod.RealmReborn.Dungeon.D21HalataliHard.D212Catoblepas;

public enum OID : uint
{
    Boss = 0xAC9 // R?
}

public enum AID : uint
{
    DemonEye = 2099 // Boss->self, 8.0s cast, party-wide; prevented by the Gloam buff from a Shadowy Orb
}

sealed class DemonEye(BossModule module) : Components.CastHint(module, (uint)AID.DemonEye, "Use a Shadowy Orb!", true);

sealed class D212CatoblepasStates : StateMachineBuilder
{
    public D212CatoblepasStates(BossModule module) : base(module)
    {
        TrivialPhase()
            .ActivateOnEnter<DemonEye>()
            .ActivateOnEnter<Components.UnclaimedCastAOEs>();
    }
}

// 沒有錄影：場地＝碰撞地板裡放得下的最大圓（比房間小，保守側）扣掉四座幽暗珠台座；其餘讀條範圍技交給 UnclaimedCastAOEs。
[ModuleInfo(BossModuleInfo.Maturity.WIP, Contributors = "ffxiv-tc-port", GroupType = BossModuleInfo.GroupType.CFC, GroupID = 21, NameID = 2598)]
public sealed class D212Catoblepas(WorldState ws, Actor primary) : BossModule(ws, primary, arena.Center, arena)
{
    private static readonly ArenaBoundsComplex arena = new([new Circle(new(149f, 225f), 20.5f)],
        [new Circle(new(143.75f, 210.62f), 2.3f), new Circle(new(163.38f, 219.75f), 2.3f), new Circle(new(134.62f, 230.25f), 2.3f), new Circle(new(154.25f, 239.38f), 2.3f)]);
}
