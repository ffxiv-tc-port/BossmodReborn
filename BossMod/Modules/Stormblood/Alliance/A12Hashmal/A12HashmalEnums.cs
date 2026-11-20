namespace BossMod.Stormblood.Alliance.A12Hashmal;

// 只列有證據的技能：列舉＝模組認領，列進來卻沒有元件的技能就連後備猜測都沒有。
public enum OID : uint
{
    Boss = 0x1F83, // R6.000
    Helper = 0x18D6, // R0.500
}

public enum AID : uint
{
    QuakeIV = 9688, // Boss->self, 4.0s cast, range 60 circle, raidwide

    JaggedEdge = 9677, // Helper->location, 3.3s cast, range 8 circle
    ControlTower = 9666, // Helper->self, 4.0s cast, range 6 circle
    CommandTower = 9838, // Helper->self, 5.5s cast, range 10 circle
    EarthShaker = 9672, // Helper->self, 5.0s cast, range 60+R 60-degree cone
    Towerfall = 9674, // Helper->self, 9.0s cast, range 40+R width 16 rect (Submission Tower)

    ExtremeEdgeL = 9680, // Boss->self, 6.0s cast, range 60+R width 36 rect
    ExtremeEdgeR = 9678, // Boss->self, 6.0s cast, range 60+R width 36 rect

    FallingBoulder = 9682, // Helper->player, 5.0s cast, range 6 circle, stack
    FallingRock = 9683, // Helper->player, 5.0s cast, range 8 circle, spread
}
