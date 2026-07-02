---
name: localization
description: 在 BossmodReborn 中新增或更新介面在地化（翻譯）字串。當使用者要新增介面文字、翻譯某個分頁/視窗，或提到 localization、translation、Loc.T、loc/*.json 時使用。
---

# 在地化 (Localization)

BossmodReborn 有一套自製、輕量的 ImGui 介面文字在地化系統。這不是完整的 i18n 框架——沒有複數形式處理，也沒有參數內插（string interpolation）。

## 運作原理

- [BossMod/Util/Loc.cs](../../../BossMod/Util/Loc.cs) 內有一個靜態類別 `Loc`，只有兩個成員：
  - `Loc.Load(langCode)`：把內嵌資源 `BossModReborn.loc.{langCode}.json` 讀進一個記憶體內的 `Dictionary<string, string>`。這個方法只在外掛初始化時呼叫一次（見 [BossMod/Framework/Plugin.cs](../../../BossMod/Framework/Plugin.cs)）。
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

不管哪一種，Key 一旦定案就是跨所有語言檔的「對照鍵」，所以：
   - 新增 key 後，理想上要同步加進 `BossMod/loc/` 下的每一個語言檔；但因為 `Loc.T` 在找不到 key 時會自動 fallback 回英文，所以就算某個語言檔還沒補上這個 key 也不會壞掉，可以晚點慢慢補翻譯。
   - **絕對不要**在沒有同步更新所有 `loc/*.json` 的情況下，隨意刪除或改名既有的 key——因為翻譯是純粹靠 key 字串去對應的，改名等於讓舊翻譯全部失聯，該語言就會整個 fallback 回英文。
4. `Loc.T` 的第二個參數（fallback 英文字串）就是英文版的「唯一真相來源（source of truth）」。修改英文文案時記得同步改這裡，不要讓 UI 顯示的英文和翻譯者參考的英文原文出現落差。

## 目前的限制 / 不支援的功能

- **不支援字串內插**：如果字串裡需要嵌入變數（例如玩家名稱、數字），要先在 C# 端組好完整字串，或是拆成多個固定的 key 來組合，`Loc.T` 本身不支援 `{0}` 這種 placeholder。
- **不支援複數形式規則**：如果同一句話會因為數量不同而有不同寫法（英文單複數、中文量詞等），需要自己在呼叫端手動處理判斷邏輯。
- 目前唯一有的翻譯語言檔是 `tw.json`（繁體中文），架構上可以直接複製一份改語言代碼（例如 `zh-cn.json`、`ja.json`）再翻譯，但需要確認 `Loc.Load` 呼叫端傳入的 `langCode` 有涵蓋新語言的判斷邏輯。
