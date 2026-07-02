---
name: module-development
description: 在 BossmodReborn 中新增/修改 Boss 模組（BossModule）時的常見結構規則與坑。當使用者要新增戰鬥模組、遇到「incorrect associated states type」之類的模組註冊錯誤、要把某個副本/王的機制「照抄」成另一個版本（例如 Unreal 版沿用 Extreme 版）、或 AI 尋路/危險區時機判斷跟實際不符（例如猜測的 forbidden zone activation 時間不準確）時使用。
---

# Boss 模組開發

## 常見問題 1：模組註冊失敗「incorrect associated states type」

日誌出現：
```
[ModuleRegistry] Module BossMod.X.Y.ZModule has incorrect associated states type: it should be derived from StateMachineBuilder and have a constructor accepting module
```

原因：[BossMod/BossModule/BossModuleRegistry.cs](../../../BossMod/BossModule/BossModuleRegistry.cs) 用反射尋找一個**命名規則固定**的類別：`{模組完整命名空間.類別名}States`，例如模組是 `BossMod.Dawntrail.Unreal.UnByakko.UnByakko`，就一定要有 `BossMod.Dawntrail.Unreal.UnByakko.UnByakkoStates`，且：
- 繼承 `StateMachineBuilder`
- 建構子要能接受該模組實例（型別可以是具體模組類別，也可以直接用基底 `BossModule`）

修法：
1. 新建一個模組時，永遠同步建立對應的 `XxxStates.cs`。
2. 還沒空寫真正時間軸時，用最小占位版本讓它先能註冊，不要留空或整個檔案註解掉（註解掉等於沒有這個型別，一樣會報錯）：
   ```csharp
   namespace BossMod.X.Y.Z;

   class ZModuleStates : StateMachineBuilder
   {
       public ZModuleStates(BossModule module) : base(module)
       {
           TrivialPhase(); // 只有一個 enrage 計時器，之後再補真正機制
       }
   }
   ```
3. `ModuleInfo` 屬性上也可以用 `StatesType = typeof(...)` 明確指定型別，跳過命名規則，但專案內幾乎都用命名慣例，沒特殊理由不要這樣做。

## 常見問題 2：「照抄」另一個版本的機制（例如 Unreal 沿用同一隻王的 Extreme 版本）

FFXIV 常把同一隻王的機制原封不動搬到不同難度/版本（例如 Unreal 討伐戰重用 Extreme 版本的招式與時間軸，只是等級、CFC、GroupID 不同）。這種情況**不要重刻機制判定邏輯**，可以直接跨命名空間重用既有模組的 component 類別：

- 這個 codebase 裡機制 component（例如 `class HeavenlyStrike(BossModule module) : Components.BaitAwayCast(...)`）幾乎都**沒有寫存取修飾詞**，C# 預設是 `internal`——代表在同一個組件（BossMod.dll，本專案只有一個 assembly）裡，任何命名空間都能直接引用，不需要 `public`、也不需要重複定義。
- 做法：在新模組檔案裡 `using 別名 = 舊模組命名空間;`（例如 `using Ex6 = BossMod.Stormblood.Extreme.Ex6Byakko;`），然後：
  - `(uint)Ex6.AID.SomeSkill`、`(uint)Ex6.OID.SomeActor` 直接重用舊版本的技能/單位 ID 列舉。
  - `ComponentCondition<Ex6.SomeComponent>(...)`、`.ActivateOnEnter<Ex6.SomeComponent>()` 等直接重用舊版本的機制 component。
  - 場地邊界等靜態欄位（如 `Ex6.Ex6Byakko.NormalBounds` / `IntermissionBounds`）也可以直接借用。
- 新模組類別本身如果有依賴額外的輔助方法（例如舊模組追蹤某個中途出現的 add，像 `Hakutei()` 搭配 `UpdateModule()` 裡 `_hakutei ??= ...` 這種「處理 wipe 時 actor 可能同幀被刪除重建」的 hack），要在新模組類別裡**照樣複製一份**，否則 States 時間軸裡引用 `_module.Hakutei` 這類存取子會編譯失敗或行為不對。
- 引用哪些 ID/component 是可以借用的，先去看舊模組資料夾內所有檔案最上面幾行的 `class X(BossModule module) : Components.Y(...)` 宣告，那些都是可以直接借用的 component。

以整套機制照抄為前提時，記得在檔案開頭留 TODO 註解，提醒之後要對照實際 Unreal/新版本的招式紀錄核對時間、傷害、機制細節是否有被官方調整過（版本間常常有小差異）——不要當作抄完就是完工。

## 常見問題 3：新增 AIConfig 選項要記得兩個地方都要顯示

`AIConfig`（`BossMod/AI/AIConfig.cs`）上的 `[PropertyDisplay(...)]` 屬性會自動出現在主設定視窗的通用 Settings UI；但 AI 追蹤小視窗（[AIManagementWindow.cs](../../../BossMod/AI/AIManagementWindow.cs)）是另外手動排版的 ImGui 呼叫，**不會**自動撿到新的 `AIConfig` 欄位。如果希望新選項在兩個地方都看得到／勾得到，兩處都要手動加，並依照 [[localization]] 裡說的兩套不同 key 規則各自補翻譯。

## 常見問題 4：危險區 activation 時間用猜的，導致 AI 尋路判斷順序錯誤

AI 尋路（`NavigationDecision`/ThetaStar）是對整張地圖做全方向最短路徑搜尋，理論上包含「往回跑」這種非直覺路線，**但前提是每個 forbidden zone 的 `activation`（`AOEInstance.Activation` / `hints.AddForbiddenZone` 的 `activation` 參數）時間要準確**。如果時間是錯的或只是概略猜的，AI 對「哪個區域先炸、哪個晚炸、能不能繞回去」的判斷就會跟著錯。

常見地雷：某些機制在敵人「剛出現、還沒開始真正施法」的階段就要先把危險區加進 hints（因為要提早警告玩家/提早規劃路線），這時候還沒有 `CastInfo` 可用，只能先用經驗值猜一個大概時間（例如 `WorldState.FutureTime(6d)`）。這種猜測值：
- 不見得跟實際結算時間吻合（動畫前搖時間、伺服器延遲都會有誤差）。
- 如果同一波裡有多個施法者，各自從「自己的出生時間」獨立起算猜測值，只要出生時間有些微先後差，猜出來的觸發順序就可能跟實際不同。

修法：**先用猜測值頂著（避免完全沒有警告），但一旦拿得到真正的伺服器施法資料就要覆蓋成準確值**。做法是在 component 裡覆寫 `Update()`，透過 `AOEInstance.ActorID`（建立時記得傳入 `actorID: actor.InstanceID`）找回施法者，檢查它的 `CastInfo`：

```csharp
public override void Update()
{
    var span = CollectionsMarshal.AsSpan(_aoes);
    for (var i = 0; i < span.Length; ++i)
    {
        ref var aoe = ref span[i];
        var caster = WorldState.Actors.Find(aoe.ActorID);
        if (caster?.CastInfo != null && caster.CastInfo.IsSpell(AID.TheRealCastAction))
            aoe.Activation = WorldState.FutureTime(caster.CastInfo.NPCRemainingTime);
    }
}
```

（實際案例見 [BossMod/Modules/RealmReborn/Dungeon/D14Praetorium/D143Gaius.cs](../../../BossMod/Modules/RealmReborn/Dungeon/D14Praetorium/D143Gaius.cs) 的 `TerminusEst` component。）

`ActorCastInfo.NPCRemainingTime`（[BossMod/Data/Actor.cs](../../../BossMod/Data/Actor.cs)）是官方施法時間扣掉 NPC 回報延遲後的精確剩餘秒數，配合 `WorldState.FutureTime(...)` 就能算出準確的結算時間。判斷「這個機制的 AI 逃跑路線/回位邏輯感覺怪怪的」時，第一步就是去對應模組找 `AddForbiddenZone`/`AOEInstance` 建立的地方，檢查 `activation` 是不是用猜的、有沒有機會換成真實 `CastInfo`。
