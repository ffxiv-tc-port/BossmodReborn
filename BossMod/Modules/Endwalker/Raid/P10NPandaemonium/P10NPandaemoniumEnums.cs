namespace BossMod.Endwalker.Raid.P10NPandaemonium;

// 只列有證據的技能：列舉＝模組認領，列進來卻沒有元件的技能就連後備猜測都沒有。
public enum OID : uint
{
    Boss = 0x3F19,
    Helper = 0x233C,
}

public enum AID : uint
{
    Ultima = 33403, // Boss->location, 5.0s cast, range 50 circle, raidwide

    SilkspitAOE = 33370, // ?->player, no cast, range 7 circle, spread

    Bury = 33375, // ?->self, 6.7s cast, range 2 circle, tower
    ImprisonmentAOE = 33378, // ?->self, 4.0s cast, range 4 circle
    CannonspawnAOE = 33380, // ?->self, 4.0s cast, range 3-7 donut

    PandaemoniacRayAOE = 33382, // ?->self, 6.2s cast, range 30 width 50 rect, half-room cleave
    PartedPlumesAOE = 33393, // ?->self, 4.0s cast, range 50 20-degree cone

    WickedStepAOE1 = 33395, // ?->self, 7.1s cast, range 4 circle, tank tower
    WickedStepAOE2 = 33396, // ?->self, 9.5s cast, range 4 circle, tank tower

    SoulGrasp = 33400, // Boss->player, 5.0s cast, single-target, visual (shared tankbuster on the cast target)
}

public enum IconID : uint
{
    Silkspit = 462, // player
}
