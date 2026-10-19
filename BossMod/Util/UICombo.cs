using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using System.Collections.Concurrent;
using System.Reflection;

namespace BossMod;

[SkipLocalsInit]
public static class UICombo
{
    // 列舉值 → 未翻譯的顯示名（反射只做一次）；翻譯每次照查 Loc.T，所以快取不必跟著語言作廢。
    private static readonly ConcurrentDictionary<Enum, string> _enumLabels = new();

    // 每個列舉型別的值陣列（Enum.GetValues 的順序）只建一次；共用、不可改。
    // 以執行期型別為鍵：T 可能是 Enum 本身（ConfigUI 傳的是裝箱的欄位值）。
    private static class EnumValues<T> where T : Enum
    {
        private static readonly ConcurrentDictionary<Type, T[]> _values = new();
        public static T[] Get(Type type) => _values.GetOrAdd(type, static t => System.Enum.GetValues(t).Cast<T>().ToArray());
    }

    public static string EnumString(Enum v)
    {
        var label = _enumLabels.GetOrAdd(v, static v =>
        {
            var name = v.ToString();
            return v.GetType().GetField(name)?.GetCustomAttribute<PropertyDisplayAttribute>()?.Label ?? name;
        });
        return Loc.T(label, label);
    }

    public static bool Enum<T>(string label, ref T v, Func<T, string>? print = null) where T : Enum
    {
        var et = v.GetType();
        var values = EnumValues<T>.Get(et);
        var idxCur = Array.IndexOf(values, v);

        if (idxCur < 0)
            idxCur = 0;

        var res = false;
        var changed = print != null
            ? Combo(label, values.Length, ref idxCur, static (a, i) => a.print(a.values[i]), (values, print))
            : Combo(label, values.Length, ref idxCur, static (b, i) => EnumString(b[i]), EnumValues<Enum>.Get(et));
        if (changed)
        {
            v = values[idxCur];
            res = true;
        }
        return res;
    }

    public static bool EnumIndex(string label, Type type, ref int v, Func<int, string>? print = null)
    {
        var values = EnumValues<Enum>.Get(type);
        return print != null
            ? Combo(label, values.Length, ref v, static (p, i) => p(i), print)
            : Combo(label, values.Length, ref v, static (b, i) => EnumString(b[i]), values);
    }

    // print(arg, i)＝第 i 個選項的文字；呼叫次數與順序和原本的 EnumIndex 相同（目前值兩次、展開時每個選項一次）。
    private static bool Combo<TArg>(string label, int count, ref int v, Func<TArg, int, string> print, TArg arg)
    {
        var res = false;
        var width = 300 * ImGuiHelpers.GlobalScale;
        ImGui.SetNextItemWidth(width);

        var labelCur = print(arg, v);
        var showLabelPopup = ImGui.CalcTextSize(labelCur).X > width;

        // draw combo without label so we can check if only the button itself is hovered
        if (ImGui.BeginCombo($"###{label}", print(arg, v)))
        {
            showLabelPopup = false;
            for (var i = 0; i < count; i++)
            {
                if (ImGui.Selectable(print(arg, i), i == v))
                {
                    v = i;
                    res = true;
                }
            }
            ImGui.EndCombo();
        }
        if (showLabelPopup && ImGui.IsItemHovered())
            ImGui.SetTooltip(labelCur);
        if (!label.StartsWith('#'))
        {
            ImGui.SameLine();
            ImGui.TextWrapped(label);
        }
        return res;
    }

    public static bool Radio(Type type, ref int v, bool oneLine, Func<int, string>? print = null)
    {
        var values = EnumValues<Enum>.Get(type);
        var orig = v;
        var res = false;

        for (var i = 0; i < values.Length; i++)
        {
            if (ImGui.RadioButton(print != null ? print(i) : EnumString(values[i]), i == v))
            {
                v = i;
                res = i != orig;
            }
            if (oneLine && i + 1 < values.Length)
                ImGui.SameLine();
        }

        return res;
    }

    public static bool Int(string label, string[] values, ref int v)
    {
        var res = false;
        ImGui.SetNextItemWidth(200);
        if (ImGui.BeginCombo(label, v < values.Length ? values[v] : v.ToString()))
        {
            for (var i = 0; i < values.Length; ++i)
            {
                if (ImGui.Selectable(values[i], v == i))
                {
                    v = i;
                    res = true;
                }
            }
            ImGui.EndCombo();
        }
        return res;
    }

    public static bool Bool(string label, string[] values, ref bool v)
    {
        var val = v ? 1 : 0;
        if (!Int(label, values, ref val))
            return false;
        v = val != 0;
        return true;
    }
}
