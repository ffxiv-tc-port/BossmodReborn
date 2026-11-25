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
- 因此 `VersionPrefix` 至今仍是 `7.15.0`，而 feed 上的版本是 `v7.30.0.x` ——**這不是漏改**，
  不要為了「分支叫 tc-7.30」就去動 `VersionPrefix`。

## 目前的分支 / API 狀態

- 這個 repo 現在在分支 **`tc-7.30`**，`BossMod/BossModReborn.json` 的 `DalamudApiLevel` 是 **13**，TFM 是 `net9.0-windows`。
- `tc-7.15`（API12 時代）已經不在遠端：`origin` 只有 `tc-7.30` 一個分支，也是預設分支。

## CI

- **`release.yml`**：只吃 `workflow_dispatch`（不再靠 tag push 自動觸發）。流程是
  下載釘住的 Dalamud（`ffxiv-tc-port/Dalamud` 的 release `dalamud-pin-v7.30.0.1/dalamud-api13-net9.zip`，
  以 workflow 的 `$pinUrl` 為準，下載後驗 SHA256）解到 `$RUNNER_TEMP\dalamud-pinned\` →
  `dotnet build -c Release -p:Version=<tag> -p:DalamudLibPath=<那個目錄>` →
  壓成 `BossModReborn.zip` → 發佈 zip + `BossModReborn.json`。
- **`build-check.yml` 也只吃 `workflow_dispatch`**，用同一顆 pin 建置；push 不會自動觸發它，
  所以 push 進來的編譯錯誤**不會被自動抓到**。要驗證編得過，請本機 `dotnet build`，或手動觸發 build-check／release。
- ⚠️ **CI 用的是釘住的那顆 Dalamud（檔案版本 13.0.0.16），本機 `Dalamud\bin\Release` 可能比它新。**
  所以「本機編得過」不等於「CI 編得過」——用到 pin 之後才加進 Dalamud 的 API 時，CI 會編不過。
  （更早的 pin 13.0.0.6 沒有 `ClientLanguage.TraditionalChinese` 這個列舉名；現在的 pin 已經有。）
- `release_plugin.py`（在 `DalamudPluginsTC`）本來就是平行執行的，而且**只推 tag**，
  不會推分支、也不會幫你 commit `repo.json`；分支與 feed 變更要自己推。

## 本機建置：`DALAMUD_HOME` 陷阱

系統的 `DALAMUD_HOME` 環境變數指向 `%APPDATA%\FFXIVSimpleLauncher\Dalamud\Injector`，
**那裡是啟動器自帶的舊 Dalamud 12.0.2.0**（實測 FileVersion，且沒有 `Dalamud.Bindings.ImGui.dll`）。
直接照著舊筆記把 `DalamudLibPath` 指過去建置，會得到：

```
error CS0234: 命名空間 'Dalamud' 中沒有類型或命名空間名稱 'Bindings'
```

建置時要指到一份真正的 API13 Dalamud（本機有兩份）：

- `D:\ffxiv-tc-port\Dalamud\bin\Release` — Dalamud **13.0.0.16**，本機重建的那份，建置就指這裡；
  遊戲實際載入的是它部署出去的 `D:\ffxiv-tc-port\DalamudLive`。
- `%APPDATA%\xivlauncher\addon\Hooks\dev` — Dalamud **13.0.0.6**，舊版，和 CI 現在釘的不同代，不要拿來重現 CI。

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
