namespace BossMod.Heavensward.Dungeon.D14HullbreakerIsleHard.D143Slafyrsyn;

public enum OID : uint
{
    Boss = 0x1646, // 愛因扎爾・斯拉菲爾辛大甲將
    Mistbeard = 0x1647 // 推定的霧鬚王（同 BNpcCustomize、下一列模型；未證實）
}

// 契約：這個列舉＝模組知道的技能。列舉以外的敵方讀條由 Components.UnclaimedCastAOEs 照沒有模組時的猜法處理，
// 所以刻意不把其他技能列進來（列了就不再被猜）。
public enum AID : uint
{
    BulletHell = 6137 // Boss->self, 13.0s cast, range 50 circle
}

sealed class BulletHell(BossModule module) : Components.RaidwideCast(module, (uint)AID.BulletHell);

sealed class D143SlafyrsynStates : StateMachineBuilder
{
    public D143SlafyrsynStates(BossModule module) : base(module)
    {
        TrivialPhase()
            .ActivateOnEnter<BulletHell>()
            .ActivateOnEnter<Components.UnclaimedCastAOEs>();
    }
}

// 場地與尋路沿用沒有模組時的做法（跟著玩家走的方框）：這個副本沒有障礙物圖，也沒有錄影可量場地。
[ModuleInfo(BossModuleInfo.Maturity.WIP, Contributors = "ffxiv-tc-port", GroupType = BossModuleInfo.GroupType.CFC, GroupID = 172, NameID = 4913)]
public sealed class D143Slafyrsyn(WorldState ws, Actor primary) : SimpleBossModule(ws, primary);

// 第二形態可能換成另一個 BNpcBase（Garland 查不到霧鬚王）；推錯只是永遠不載入，限定這個 CFC 避免別處誤啟用。
[ModuleInfo(BossModuleInfo.Maturity.WIP, Contributors = "ffxiv-tc-port", GroupType = BossModuleInfo.GroupType.CFC, GroupID = 172, NameID = 4914,
    PrimaryActorOID = (uint)OID.Mistbeard, StatesType = typeof(D143SlafyrsynStates))]
public sealed class D143Mistbeard(WorldState ws, Actor primary) : SimpleBossModule(ws, primary)
{
    protected override bool CheckPull() => base.CheckPull() && WorldState.CurrentCFCID == 172u;
}
