namespace BossMod;

/// <summary>
/// 放不進其他分頁、但必須讓使用者看得見（而不是靜靜發生）的少數全域選項。
/// </summary>
/// <remarks>
/// 📌 這個節點也存放 <see cref="ConfigChangelogWindow"/> 用的「上次看到的版本」。
/// 它刻意沒有 <c>PropertyDisplay</c>：不該出現在設定頁，但必須跟著設定檔一起序列化。
/// 為了一個看不見的欄位另開一個設定節點，會在設定樹上多出一個空白頁面。
/// </remarks>
[ConfigDisplay(Name = "Miscellaneous", Order = 9)]
public sealed class MiscConfig : ConfigNode
{
    // 🔴 預設 false 本身就是**行為變更**：這個功能在 7.20.0.62 之前是每次載入外掛都無條件執行的，
    //    沒有開關、也沒有任何告知。使用者裁決「搬 bmr 加開關」，預設關。
    // 📌 既有使用者確實拿得到這個 false：BMR 的反序列化是「遍歷 JSON 裡既有的鍵」
    //    （ConfigNode.Deserialize 走 j.EnumerateObject()），與 ECommons EzConfig 相反 ——
    //    舊設定檔裡沒有這個鍵，欄位初始值就是最終值。不需要遷移旗標。
    [PropertyDisplay("Unlock multiboxing (close the game's single-instance mutex)",
        tooltip: "Lets you run more than one game client on this machine at the same time.\n\n" +
        "What it actually does: the plugin walks this game process's own handle table, finds the named mutex the launcher uses to enforce \"one client per machine\" (its name ends in _ffxiv_game0) and closes that one handle. Nothing is sent to the server and no other process is touched.\n\n" +
        "Risks, please read: running several clients at once is against the terms of service of most FFXIV services, and each extra client costs a full set of CPU, GPU and memory. The handle is only closed for this client, and it comes back the next time you restart the game.\n\n" +
        "Off by default. Note that up to and including version 7.20.0.62 this ran unconditionally on every plugin load, with no setting and no notice - if you were relying on it, this is the switch that brings it back.")]
    public bool UnlockMultibox = false;

    // 沒有 PropertyDisplay ⇒ 不出現在設定 UI，也不會被 /bmr cfg 列出（ConsoleCommand 只列有 PropertyDisplay 的欄位），
    // 但 ConfigNode.Serialize 是走「所有非 JsonIgnore 的實例欄位」，所以照樣存進設定檔。
    public string LastSeenVersion = "";
}
