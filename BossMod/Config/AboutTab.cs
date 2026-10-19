using Dalamud.Interface.Utility.Raii;
using Dalamud.Bindings.ImGui;
using System.Diagnostics;
using System.IO;

namespace BossMod;

public sealed class AboutTab(DirectoryInfo? replayDir)
{
    private static readonly Color TitleColor = Color.FromComponents(255u, 165u, default);
    private static readonly Color SectionBgColor = Color.FromComponents(38u, 38u, 38u);
    private static readonly Color BorderColor = Color.FromComponents(178u, 178u, 178u, 204u);
    private static readonly Color DiscordColor = Color.FromComponents(88u, 101u, 242u);

    private string _lastErrorMessage = "";

    public void Draw()
    {
        using var wrap = ImRaii.TextWrapPos(0);

        ImGui.TextUnformatted(Loc.T("ABOUT_Intro", "BossModReborn (BMR) provides boss fight radar, auto-rotation, cooldown planning, and AI. All of its modules can be toggled individually. Support for it can be found in the Discord server linked at the bottom of this tab."));
        ImGui.TextUnformatted(Loc.T("ABOUT_ForkNote", "This is a FORK of the original BossMod (VBM). Only ask for support on the Combat Reborn Discord."));
        ImGui.TextUnformatted(Loc.T("ABOUT_NoDouble", "Please also make sure to not load VBM and this fork at the same time. The consequences of doing that are unexplored and unsupported."));
        ImGui.Spacing();
        DrawSection(Loc.T("ABOUT_Radar", "Radar"),
        [
            Loc.T("ABOUT_Radar_1", "Provides an on-screen window that contains an area mini-map showing player positions, boss position(s), various imminent AOEs, and other mechanics."),
            Loc.T("ABOUT_Radar_2", "Useful because you don't have to remember what ability names mean."),
            Loc.T("ABOUT_Radar_3", "See exactly whether you're getting clipped by incoming AOEs or not."),
            Loc.T("ABOUT_Radar_4", "Enabled for supported bosses, visible in the \"Supported bosses\" tab."),
        ]);
        ImGui.Spacing();
        DrawSection(Loc.T("ABOUT_Autorot", "Autorotation"),
        [
            Loc.T("ABOUT_Autorot_1", "Executes fully optimal rotations to the best of its ability."),
            Loc.T("ABOUT_Autorot_2", "Go to the \"Autorotation presets\" tab to create a preset."),
            Loc.T("ABOUT_Autorot_3", "Maturity of each rotation module is present in a tooltip."),
            Loc.T("ABOUT_Autorot_4", "Guide for using this feature can be found on the wiki."),
        ]);
        ImGui.Spacing();
        DrawSection(Loc.T("ABOUT_CDPlanner", "Cooldown planner"),
        [
            Loc.T("ABOUT_CDPlanner_1", "Creates a CD plan for supported bosses."),
            // 🔴 原句「Replaces autorotations in specific fights.」把優先順序講反了：
            //    RotationModuleManager.Update 是 Preset != null 就用預設的模組，只有 Preset == null
            //    才輪到計劃 ⇒ 是「預設蓋掉計劃」，不是「計劃取代預設」。自動循環視窗自己那顆
            //    警告圖示（ROT_PlanBlockedByPreset）講的就是相反的事，兩句不能並存。
            Loc.T("ABOUT_CDPlanner_2", "Used in specific fights instead of autorotation, but only while no preset is activated."),
            Loc.T("ABOUT_CDPlanner_3", "Allows you to time specific abilities to cast at specific times."),
            Loc.T("ABOUT_CDPlanner_4", "Guide for using this feature can be found on the wiki."),
        ]);
        ImGui.Spacing();
        DrawSection(Loc.T("ABOUT_AI", "AI"),
        [
            Loc.T("ABOUT_AI_1", "Automates movement during boss fights."),
            Loc.T("ABOUT_AI_2", "Automatically moves your character based on safe zones determined by a boss's module, visible on the radar."),
            Loc.T("ABOUT_AI_3", "Should not be used in when playing with unknown players."),
            Loc.T("ABOUT_AI_4", "Can be hooked by other plugins to automate entire duties."),
        ]);
        ImGui.Spacing();
        DrawSection(Loc.T("ABOUT_Replays", "Replays"),
        [
            Loc.T("ABOUT_Replays_1", "Useful for creating boss modules, analyzing problems with them, and making CD plans."),
            Loc.T("ABOUT_Replays_2", "When asking for help, make sure to provide a replay! Please note that replays will contain your player name!"),
            Loc.T("ABOUT_Replays_3", "Enabled in Settings > Show replay management UI (or enable auto recording)."),
            $"{Loc.T("ABOUT_Replays_4", "Files are located in")} '{replayDir}'.",
        ]);
        ImGui.Spacing();
        ImGui.Spacing();

        using (ImRaii.PushColor(ImGuiCol.Button, DiscordColor.ABGR))
            if (ImGui.Button(Loc.T("ABOUT_BtnDiscord", "Combat Reborn Discord"), new(220, 0)))
                _lastErrorMessage = OpenLink("https://discord.gg/p54TZMPnC9");
        ImGui.SameLine();
        if (ImGui.Button(Loc.T("ABOUT_BtnGitHub", "BossModReborn GitHub"), new(220, 0)))
            _lastErrorMessage = OpenLink("https://github.com/FFXIV-CombatReborn/BossmodReborn");
        ImGui.SameLine();
        if (ImGui.Button(Loc.T("ABOUT_BtnWiki", "BossMod Wiki"), new(130, 0)))
            _lastErrorMessage = OpenLink("https://github.com/awgil/ffxiv_bossmod/wiki");
        ImGui.SameLine();
        if (ImGui.Button(Loc.T("ABOUT_BtnOpenFolder", "Open replay folder"), new(180, 0)) && replayDir != null)
            _lastErrorMessage = OpenDirectory(replayDir);

        if (_lastErrorMessage.Length > 0)
        {
            using var color = ImRaii.PushColor(ImGuiCol.Text, Colors.TextColor3);
            ImGui.TextUnformatted(_lastErrorMessage);
        }
    }

    private static void DrawSection(string title, string[] bulletPoints)
    {
        using var colorBackground = ImRaii.PushColor(ImGuiCol.ChildBg, SectionBgColor.ABGR);
        using var colorBorder = ImRaii.PushColor(ImGuiCol.Border, BorderColor.ABGR);
        var height = ImGui.GetTextLineHeightWithSpacing() * (bulletPoints.Length + 2);
        using var section = ImRaii.Child(title, new(0, height), false, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.AlwaysUseWindowPadding);

        if (!section)
            return;

        using (ImRaii.PushColor(ImGuiCol.Text, TitleColor.ABGR))
            ImGui.TextUnformatted(title);

        ImGui.Separator();
        ImGui.PushTextWrapPos();
        foreach (var point in bulletPoints)
        {
            ImGui.Bullet();
            ImGui.SameLine();
            ImGui.TextUnformatted(point);
        }
        ImGui.PopTextWrapPos();
    }

    private static string OpenLink(string link)
    {
        try
        {
            Process.Start(new ProcessStartInfo(link) { UseShellExecute = true });
            return "";
        }
        catch (Exception e)
        {
            Service.Log($"Error opening link {link}: {e}");
            return string.Format(Loc.T("ABOUT_ErrOpenLink", "Failed to open link '{0}', open it manually in the browser."), link);
        }
    }

    private static string OpenDirectory(DirectoryInfo dir)
    {
        if (!dir.Exists)
            return string.Format(Loc.T("ABOUT_ErrDirNotFound", "Directory '{0}' not found."), dir);

        try
        {
            Process.Start(new ProcessStartInfo(dir.FullName) { UseShellExecute = true });
            return "";
        }
        catch (Exception e)
        {
            Service.Log($"Error opening directory {dir}: {e}");
            return string.Format(Loc.T("ABOUT_ErrOpenDir", "Failed to open folder '{0}', open it manually."), dir);
        }
    }
}
