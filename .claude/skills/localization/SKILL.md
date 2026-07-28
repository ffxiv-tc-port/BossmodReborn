---
name: localization
description: 在 BossmodReborn 中新增或更新介面在地化（翻譯）字串。當使用者要新增介面文字、翻譯某個分頁/視窗，或提到 localization、translation、Loc.T、loc/*.json 時使用。
---

# 在地化 (Localization)

BossmodReborn 有一套自製、輕量的 ImGui 介面文字在地化系統。這不是完整的 i18n 框架——沒有複數形式處理，也沒有參數內插（string interpolation）。

## 運作原理

- [BossMod/Util/Loc.cs](../../../BossMod/Util/Loc.cs) 內有一個靜態類別 `Loc`，只有兩個成員：
  - `Loc.Load(langCode)`：把內嵌資源 `BossModReborn.loc.{langCode}.json` 讀進一個記憶體內的 `Dictionary<string, string>`。這個方法只在外掛初始化時呼叫一次，而且**語言代碼是寫死的**——[BossMod/Framework/Plugin.cs:63](../../../BossMod/Framework/Plugin.cs) 就是一行 `Loc.Load("tw");`，全程不看遊戲語言、不看 `ClientLanguage`、也不看 `pluginInterface.UiLanguage`（這點很重要，見下面〈ClientLanguage 與這個外掛無關〉）。找不到內嵌資源時 `Load` 會靜默 return，字典維持空的，所有 `Loc.T` 就全部 fallback 回英文。
  - `Loc.T(key, fallback)`：用 `key` 去查剛剛載入的字典；如果找不到（該語言沒有翻譯，或根本沒載入任何語言檔），就回傳 `fallback`（也就是英文原文）。
- 翻譯檔放在 [BossMod/loc/](../../../BossMod/loc/) 資料夾下，一種語言一個 `.json` 檔（例如 `tw.json` 是繁體中文），格式是「key -> { message }」：
  ```json
  {
    "Tab_Settings": { "message": "設定" },
    "AI_ForbidActions": { "message": "禁止行動" }
  }
  ```
- 這些 json 檔是透過 `BossMod/BossModReborn.csproj` 裡的 `<EmbeddedResource Include="loc\*.json" />` 被打包進 dll 的，所以新增/修改語言檔後不需要另外設定專案檔，重新編譯就會生效。

## 新增一個可翻譯的介面字串

有兩種完全不同的加翻譯方式，取決於這個字串是手動畫的還是靠反射自動畫的：

### A. 手動 ImGui 呼叫（例如 [BossMod/AI/AIManagementWindow.cs](../../../BossMod/AI/AIManagementWindow.cs)）

1. 在程式碼裡，把原本寫死的字串字面值改成 `Loc.T("某個Key", "English fallback")`，`fallback` 就是目前的英文原文，找不到翻譯時會顯示這個。
2. Key 的命名要照所在區塊挑一個既有的前綴，方便之後維護與批次翻譯，例如目前已經有：
   - `Tab_` — 分頁名稱
   - `CFG_` — Config UI 裡的通用字串
   - `ABOUT_` — 關於頁面
   - `AI_` — AI 管理視窗
   - 依此類推，新增其他區塊時可以自訂新的前綴，但要保持一致。

### B. 反射式通用設定 UI（`ConfigNode` 上的 `[PropertyDisplay(...)]` 屬性，畫在主設定視窗「設定」分頁裡）

- **這裡的 key 不是自訂的短 key，而是直接拿 `PropertyDisplay` 的 label / tooltip 英文原文字串本身當 key**：見 [BossMod/Config/ConfigUI.cs:178](../../../BossMod/Config/ConfigUI.cs)：`Loc.T(props.Label, props.Label)` / `Loc.T(props.Tooltip, props.Tooltip)`。
- 所以只要在任何 `ConfigNode`（如 `AIConfig.cs`、各職業的 `*Config.cs`）加一個 `[PropertyDisplay("Some label", tooltip: "Some tooltip")]`，翻譯檔裡就要新增一筆 `"Some label": { "message": "..." }`（tooltip 非空的話也要照樣加一筆），**key 必須跟屬性裡的英文字串一字不差**，之後改了英文原文字也要同步改 key，否則翻譯會失聯回退成英文。
- 判斷一個字串屬於 A 還是 B：搜尋看它是不是出現在 `[PropertyDisplay(...)]` 裡；是的話就是 B 類，key = 該字串本身。
- **設定樹的節點/分組名稱也走 B 類規則**：[BossMod/Config/ConfigUI.cs:204](../../../BossMod/Config/ConfigUI.cs) 的 `_tree.Nodes(nodes, n => new(Loc.T(n.Name, n.Name)))`，key 一樣是節點的英文名稱本身。所以「設定」分頁裡左邊那些分組標題要翻譯，也是在 `tw.json` 加一筆以英文原名當 key 的條目，不是去程式碼裡加短 key。

不管哪一種，Key 一旦定案就是跨所有語言檔的「對照鍵」，所以：
   - 新增 key 後，理想上要同步加進 `BossMod/loc/` 下的每一個語言檔；但因為 `Loc.T` 在找不到 key 時會自動 fallback 回英文，所以就算某個語言檔還沒補上這個 key 也不會壞掉，可以晚點慢慢補翻譯。
   - **絕對不要**在沒有同步更新所有 `loc/*.json` 的情況下，隨意刪除或改名既有的 key——因為翻譯是純粹靠 key 字串去對應的，改名等於讓舊翻譯全部失聯，該語言就會整個 fallback 回英文。
4. `Loc.T` 的第二個參數（fallback 英文字串）就是英文版的「唯一真相來源（source of truth）」。修改英文文案時記得同步改這裡，不要讓 UI 顯示的英文和翻譯者參考的英文原文出現落差。

## ClientLanguage 與這個外掛無關（但踩到別的 repo 時要知道）

2026-07 全艦隊升到 Dalamud 13.0.0.16 之後，TC 客戶端回報的 `ClientLanguage` 從
`ChineseSimplified`(4) 變成了 **`TraditionalChinese`(7)**（`Svc.ClientState.ClientLanguage`
與 `Svc.Data.Language` 兩者都是 7）。新的列舉是：

```
Japanese=0, English=1, German=2, French=3,
ChineseSimplified=4, ChineseTraditional=5, Korean=6, TraditionalChinese=7
```

艦隊裡所有靠「`ClientLanguage == 4`」去挑繁中資源的外掛，都因此**靜默**掉回英文/日文
（實證：HuntHelper 從 `traditional chinese.json` 掉成 `japanese.json`）。

**BossmodReborn 不在受影響名單裡**，因為它 `Loc.Load("tw")` 是寫死的，從來沒判斷過語言。所以：

- 不要因為看到別的 repo 在改 ClientLanguage 就跑來「順手修一下」這裡——這裡沒東西可修。
- 如果**未來真的**要加語言自動偵測（例如支援多語系），有一條硬限制：
  **必須用數值比較，不能寫列舉名**。CI 釘的 Dalamud 是 **13.0.0.6**，那一版**沒有
  `TraditionalChinese` 這個列舉名**（已用二進位字串驗證：其他 7 個名字都在，只有它 ABSENT），
  寫 `ClientLanguage.TraditionalChinese` 本機編得過、CI 一定炸。正確寫法：
  - `switch`：`(ClientLanguage)4 or (ClientLanguage)5 or (ClientLanguage)7 => "tw"`
    （要把既有的 `ChineseSimplified`/`ChineseTraditional` 兩條先刪掉再合併換上，否則 CS8120 重複標籤）
  - `if`/三元：`(int)lang is 4 or 5 or 7`

## 目前的限制 / 不支援的功能

- **不支援字串內插**：如果字串裡需要嵌入變數（例如玩家名稱、數字），要先在 C# 端組好完整字串，或是拆成多個固定的 key 來組合，`Loc.T` 本身不支援 `{0}` 這種 placeholder。
- **不支援複數形式規則**：如果同一句話會因為數量不同而有不同寫法（英文單複數、中文量詞等），需要自己在呼叫端手動處理判斷邏輯。
- 目前 `BossMod/loc/` 底下唯一的語言檔就是 `tw.json`（繁體中文），架構上可以直接複製一份改語言代碼（例如 `zh-cn.json`、`ja.json`）再翻譯；`csproj` 的 `<EmbeddedResource Include="loc\*.json" />` 是萬用字元，新檔不用另外登記。但 `Loc.Load("tw")` 是**寫死**的單一呼叫，多語系要能運作就得先把那行改成有判斷邏輯的版本——真的要動的話，先讀上面〈ClientLanguage 與這個外掛無關〉那段的數值比較限制。
