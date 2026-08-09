using Dalamud.Interface.Utility.Raii;
using Dalamud.Utility;
using Dalamud.Bindings.ImGui;

namespace BossMod.Autorotation;

public sealed class UIRotationModule
{
    /// <summary>
    /// 一次能列出多少個職業全名；超過就收成數量。
    /// </summary>
    /// <remarks>
    /// ⚠️ 全職業型的模組有 42 個職業，全名展開會是一行三百多字的牆。
    /// 收成數量之後仍然是誠實的（沒有假裝只有幾個），而且「幾乎所有職業」這個資訊本身就夠用了。
    /// </remarks>
    private const int MaxListedClasses = 12;

    /// <summary>職業縮寫 → 台服官方全名。查不到就退回縮寫。</summary>
    /// <remarks>
    /// 📌 走 loc 表而不是 Lumina：<c>Service.LuminaSheet</c> 寫死 <c>Language.English</c>，
    /// 在台服客戶端拿不到繁中職業名。譯名是逐字取自台服 <c>ClassJob.Name</c>，
    /// 42 個職業的縮寫也與該表的 <c>Abbreviation</c> 欄全部對得上（不是猜的）。
    /// </remarks>
    private static string ClassName(Class c) => Loc.T($"CLASS_{c}", c.ToString());

    public static void DescribeModule(Type type, RotationModuleDefinition definition)
    {
        ImGui.TextUnformatted(Loc.T(definition.DisplayName));
        ImGui.TextUnformatted(Loc.T(definition.Description));

        var classes = definition.Classes.SetBits().Where(b => b != (int)Class.None).ToList();
        var classText = classes.Count > MaxListedClasses
            ? string.Format(Loc.T("ROT_ClassCount", "{0} classes"), classes.Count)
            : string.Join(" ", classes.Select(b => ClassName((Class)b)));
        ImGui.TextUnformatted(string.Format(Loc.T("ROT_LevelsClasses", "L{0}-{1} {2}"), definition.MinLevel, definition.MaxLevel, classText));

        ImGui.TextUnformatted(string.Format(Loc.T("ROT_Authors", "Author/contributors: {0}"), definition.Author));
        ImGui.TextUnformatted(string.Format(Loc.T("ROT_Quality", "Quality: {0}/{1} {2}"),
            (int)definition.Quality, (int)RotationModuleQuality.Count - 1,
            Loc.T(definition.Quality.GetAttribute<PropertyDisplayAttribute>()?.Label ?? "")));
        using (ImRaii.Disabled())
        {
            // 🔴 型別全名刻意不翻：它是使用者拿去對照 presets.db.json 的錨點，翻了反而害人。
            ImGui.TextUnformatted(string.Format(Loc.T("ROT_TypeName", "Class: {0}"), type.FullName));
            ImGui.TextUnformatted(string.Format(Loc.T("ROT_OrderGroup", "Order group: {0}"), definition.Order));
        }
    }
}
