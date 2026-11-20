namespace BossMod.Heavensward.Raid.A7NQuickthinxAllthoughts;

// 首領 OID 沒有錄影：萬事通 奎克辛克斯（BNpcName 3376）在 BNpcBase 有兩列，分不出一般／零式，兩個都註冊。
// 一般難度的技能 ID 沒有直接證據：同名同形狀的兩列都放進列舉（其中一列零式用），沒用到的元件就不會觸發。
// 契約：列舉＝模組知道的技能；列舉以外的敵方讀條由 Components.UnclaimedCastAOEs 照沒有模組時的猜法畫。
public enum OID : uint
{
    BossCandidateA = 0x15BC,
    BossCandidateB = 0x1505,
}

public enum AID : uint
{
    Sizzlebeam1 = 5614, // Boss->self, 2.0s cast, range 60 width 6 rect, aimed at the marked player (used in savage per cactbot)
    Sizzlebeam2 = 5879, // Boss->self, 2.0s cast, range 60 width 6 rect
    Sizzlespark1 = 5615, // Boss->self, 4.0s cast, range 80 circle, raidwide
    Sizzlespark2 = 5880, // Boss->self, 4.0s cast, range 80 circle, raidwide (savage per cactbot)
    Kugelblitz1 = 5815, // SturmDoll->self, 4.0s cast, range 100 circle, raidwide, stunnable
    Kugelblitz2 = 5886, // SturmDoll->self, 4.0s cast, range 100 circle, raidwide, stunnable (savage per cactbot)
}

public enum IconID : uint
{
    Sizzlebeam = 24, // player
}
