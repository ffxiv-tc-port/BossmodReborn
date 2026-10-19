---
name: build-versioning
description: Explains how the BossModReborn build number auto-increments after each build, where the compiled DLL ends up, why the released version has nothing to do with the csproj version, and why the running game may still show old behavior after a code change. Use when the user asks about version bumps, BossModReborn.csproj / BuildNumber.txt drift, dll output path, release.yml / release_plugin.py, or reports that a change "doesn't show up" in-game.
---

# Build versioning

## Current mechanism: `BuildNumber.txt` (not `increment_version.ps1`)

`BossMod/BossModReborn.csproj` derives its version from a git-tracked counter file, `BossMod/BuildNumber.txt`:

```xml
<BuildNumberFile>$(MSBuildProjectDirectory)\BuildNumber.txt</BuildNumberFile>
<_PreviousBuildNumber Condition="Exists('$(BuildNumberFile)')">$([System.IO.File]::ReadAllText('$(BuildNumberFile)').Trim())</_PreviousBuildNumber>
<_PreviousBuildNumber Condition="'$(_PreviousBuildNumber)' == ''">0</_PreviousBuildNumber>
<BuildNumber>$([MSBuild]::Add($(_PreviousBuildNumber), 1))</BuildNumber>
<VersionPrefix>7.15.0</VersionPrefix>
<Version>$(VersionPrefix).$(BuildNumber)</Version>
<AssemblyVersion>$(Version)</AssemblyVersion>
<FileVersion>$(Version)</FileVersion>
```

```xml
<Target Name="PersistBuildNumber" AfterTargets="Build">
  <WriteLinesToFile File="$(BuildNumberFile)" Lines="$(BuildNumber)" Overwrite="true" />
</Target>
```

The counter is read as a plain evaluated property (not inside a `<Target>`), so `$(Version)` is already correct before packaging runs; `PersistBuildNumber` only writes the incremented value back for the *next* build.

> **歷史（已不適用，API12 / `tc-7.15` 時代的舊筆記）**：以前是 `BossMod/increment_version.ps1` 搭配一個
> `IncrementBuildNumber` MSBuild target，直接改寫 csproj 裡的 `<Version>Major.Minor.Build.Revision</Version>`。
> **那個腳本已經不存在**，csproj 也不再被建置改寫。看到任何文件叫你去找 `increment_version.ps1`
> 或說「建置後 csproj 會出現在 `git status`」，那都是舊資訊——現在被改寫的是 `BuildNumber.txt`。

## Implications

- **Every local build bumps `BuildNumber.txt`**（檔案受 git 追蹤、沒有被 `.gitignore` 排除），even builds that
  don't get committed/released. 這是預期行為，不要當成 bug 去「修回去」，除非使用者要求。
- 弄髒工作區的是 `BossMod/BuildNumber.txt`，**不是 csproj**。`git status` 出現它時直接
  `git checkout -- BossMod/BuildNumber.txt` 還原即可；**小心 `git add -A`**，很容易把它連帶提交進無關的 commit。
- ⚠️ **工作區髒掉會讓 `release_plugin.py` 直接跳過這個外掛**（它會判定「有未提交變更」而不發版）。
  發版前先確認 `git status --porcelain` 是乾淨的。
- **csproj 算出來的版本號跟實際發版無關**：`release.yml` 建置時用
  `-p:Version=${tag} -p:AssemblyVersion=… -p:FileVersion=… -p:InformationalVersion=…` 從 git tag 覆蓋掉，
  csproj 的 `VersionPrefix`/`BuildNumber` 只影響本機建置產物的版本字串。
- 因此 `VersionPrefix` 至今仍是 `7.15.0`，而 feed 上的版本是 `v7.20.0.x` ——**這不是漏改**，
  不要為了「分支叫 tc-7.20」就去動 `VersionPrefix`（先看下面 build-check 那條，改它有副作用）。

## 目前的分支 / API 狀態

- 這個 repo 現在在分支 **`tc-7.20`**，`BossMod/BossModReborn.json` 的 `DalamudApiLevel` 是 **13**，TFM 是 `net9.0-windows`。
- **`tc-7.15` 是凍結的 API12 archive**，只留著當歷史參考，不要往上面提交。
  （注意 GitHub 上 `origin/HEAD` 目前還指向 `tc-7.15`，`git clone` 預設會落在舊分支上——clone 完先 `git checkout tc-7.20`。）

## CI

- **`release.yml`**：只吃 `workflow_dispatch`（不再靠 tag push 自動觸發）。流程是
  下載釘住的 Dalamud（`dalamud-pin-v13.0.0.6/dalamud-api13-net9.zip`）解到
  `%AppData%\FFXIVSimpleLauncher\Dalamud\Injector\` → `dotnet build -c Release -p:Version=<tag>` →
  壓成 `BossModReborn.zip` → 發佈 zip + `BossModReborn.json`。
- **`build-check.yml` 目前實質上是空轉**：它有一個 gate job 去 grep csproj 的 `<VersionPrefix>`，
  只有在**不等於 baseline `7.15.0`** 時才會跑 build job。現在 `VersionPrefix` 就是 `7.15.0`，
  所以 push 進來的編譯錯誤 **build-check 不會抓到**。要驗證編得過，請本機 `dotnet build` 或直接跑 release.yml。
  （這個 gate 的原始用意未經確認，動它之前先問清楚，不要順手把 `VersionPrefix` 改成 `7.20.0` 來「打開」它。）
- ⚠️ **CI 釘的是 Dalamud 13.0.0.6，遊戲執行期跑的是 13.0.0.16**，兩者不同版。
  所以「本機編得過」不等於「CI 編得過」——特別是只有 13.0.0.16 才有的 API/列舉名（例如
  `ClientLanguage.TraditionalChinese`），寫進程式碼 CI 會直接炸。
- `release_plugin.py`（在 `DalamudPluginsTC`）本來就是平行執行的，而且**只推 tag**，
  不會推分支、也不會幫你 commit `repo.json`；分支與 feed 變更要自己推。

## 本機建置：`DALAMUD_HOME` 陷阱

系統的 `DALAMUD_HOME` 環境變數指向 `%APPDATA%\FFXIVSimpleLauncher\Dalamud\Injector`，
**那裡是啟動器自帶的舊 Dalamud 12.0.2.0**（實測 FileVersion，且沒有 `Dalamud.Bindings.ImGui.dll`）。
直接照著舊筆記把 `DalamudLibPath` 指過去建置，會得到：

```
error CS0234: 命名空間 'Dalamud' 中沒有類型或命名空間名稱 'Bindings'
```

建置時要指到一份真正的 API13 Dalamud（本機實測可用的兩處）：

- `%APPDATA%\xivlauncher\addon\Hooks\dev` — Dalamud **13.0.0.6**，跟 CI 釘的版本一致，最適合拿來重現 CI。
- `D:\ffxiv-tc-port\Dalamud\bin\Release` — Dalamud **13.0.0.16**，是 TC 遊戲執行期實際載入的那份。

**不要**去覆寫 `%APPDATA%\FFXIVSimpleLauncher\Dalamud\Injector`（CI 在自己的 runner 上覆寫沒差，
本機那份跟實際遊戲載入有關）。

## DLL 輸出位置 & 為什麼遊戲裡看不到剛改的東西

`AppendTargetFrameworkToOutputPath` 設成 `false`，所以沒有 `net9.0-windows` 這層子資料夾，輸出直接在：
- Debug：`BossMod/bin/Debug/BossModReborn.dll`（+ 同目錄的 `BossModReborn.json`）
- Release：`BossMod/bin/Release/BossModReborn.dll`

**`dotnet build` 編譯成功 ≠ 遊戲裡的外掛已經更新。**

⚠️ 而且現在**更嚴格**：實測 `%APPDATA%\FFXIVSimpleLauncher\Dalamud\Config\dalamudConfig.json` 的
`DevPluginLoadLocations` **是空的**——這個外掛是從 feed 安裝的，不是 devPlugin。也就是說
本機 build 出來的 dll **根本不會被遊戲讀到**，reload 也沒用。要在遊戲裡驗證改動，只有兩條路：
（a）走正常發版流程讓 feed 更新，或（b）自己把 `BossMod/bin/Release` 加進 Dalamud 的 devPlugin 路徑清單
（加了之後才適用下面這段）。

若確實是以 devPlugin 方式載入：BossModReborn 是 Dalamud plugin，遊戲進程會把 dll 載進記憶體常駐執行；
改完程式碼、重新 build 完，一定要回遊戲對這個 devPlugin 做 **Reload**（或 Disable 再 Enable），遊戲才會抓到新 dll。
單純看到 `dotnet build` 沒有 error，不代表使用者在遊戲裡看到的就是新版行為——如果使用者回報
「怎麼沒有這個選項/改動」，第一步先確認 dll 到底有沒有進到遊戲（devPlugin 路徑存不存在、有沒有 reload），
而不是急著去找程式碼哪裡漏改。
