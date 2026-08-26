---
name: build-versioning
description: Explains how the BossModReborn build number auto-increments after each build, where the compiled DLL ends up, and why the running game may still show old behavior after a code change. Use when the user asks about version bumps, BossModReborn.csproj Version drift, increment_version.ps1, dll output path, or reports that a change "doesn't show up" in-game.
---

# Build versioning

`BossMod/BossModReborn.csproj` has a `<Version>Major.Minor.Build.Revision</Version>` (e.g. `7.15.0.12`) and an MSBuild target that bumps the revision automatically:

```xml
<Target Name="IncrementBuildNumber" AfterTargets="Build">
  <Exec Command="powershell -NoProfile -ExecutionPolicy Bypass -File &quot;$(MSBuildProjectDirectory)\increment_version.ps1&quot; -ProjectFile &quot;$(MSBuildProjectFullPath)&quot;" />
</Target>
```

`BossMod/increment_version.ps1` runs after every build: it reads the `Version` from the given `.csproj`, increments the last (Revision) component by 1, and writes it back in place.

## Implications

- **Every local build bumps the revision**, even builds that don't get committed/released. This is expected — don't "fix" the version back down as if it were a bug, unless asked to.
- Because the script mutates the `.csproj` on disk on every build, expect `BossModReborn.csproj` to show up as modified in `git status`/`git diff` after building, even when no manual edits were made. Don't assume a diff there is intentional/reviewable content — check whether it's just the version bump.
- Major/Minor are set manually in the csproj (e.g. `7.15`) and match the branch/release naming (this repo's `tc-7.15` branch tracks version `7.15.x.x`).

## DLL 輸出位置 & 為什麼遊戲裡看不到剛改的東西

`AppendTargetFrameworkToOutputPath` 設成 `false`，所以沒有 `net9.0-windows` 這層子資料夾，輸出直接在：
- Debug：`BossMod/bin/Debug/BossModReborn.dll`（+ 同目錄的 `BossModReborn.json`）
- Release：`BossMod/bin/Release/BossModReborn.dll`

**`dotnet build` 編譯成功 ≠ 遊戲裡的外掛已經更新。** BossModReborn 是 Dalamud plugin，遊戲進程會把 dll 載進記憶體常駐執行；改完程式碼、重新 build 完，一定要回遊戲對這個 devPlugin 做 **Reload**（或 Disable 再 Enable），遊戲才會抓到新 dll。單純看到 `dotnet build` 沒有 error，不代表使用者在遊戲裡看到的就是新版行為——如果使用者回報「怎麼沒有這個選項/改動」，第一步先確認他有沒有重新 reload 外掛，而不是急著去找程式碼哪裡漏改。
