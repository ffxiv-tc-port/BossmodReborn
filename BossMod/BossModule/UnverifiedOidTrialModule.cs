namespace BossMod;

// 首領 OID 沒有錄影佐證（例：從極版同模型推的）的討伐戰模組一律繼承這個。
// 契約：只在 ModuleInfo 宣告的那個 CFC 裡開打，推錯也不會在別的副本誤啟用；
// 通用雷達不因為這種模組存在就讓位（GenericTrialRadar.IsTrialWithoutModule），推錯時退回通用雷達。
public abstract class UnverifiedOidTrialModule(WorldState ws, Actor primary, WPos center, ArenaBounds bounds) : BossModule(ws, primary, center, bounds)
{
    protected override bool CheckPull() => base.CheckPull() && Info?.GroupType == BossModuleInfo.GroupType.CFC && WorldState.CurrentCFCID == Info.GroupID;
}
