using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using ImGuiNET;

namespace ContentEditor.App;

public static class AppImguiHelpers
{
    private static readonly ConcurrentDictionary<uint, string> fileBrowseResults = new();

    public static bool InputFilepath(string label, [NotNull] ref string? path, string? extension = null)
    {
        var id = ImGui.GetID(label);
        var x = ImGui.GetCursorPosX();
        var textWidth = ImGui.CalcItemWidth() - ImGui.CalcTextSize("Browse...").X - ImGui.GetStyle().FramePadding.X * 2 - x;
        ImGui.PushID(label);
        if (ImGui.Button("Browse...")) {
            PlatformUtils.ShowFileDialog((list) => fileBrowseResults[id] = list[0], path, extension, false);
        }
        ImGui.SameLine();
        path ??= "";
        ImGui.SetNextItemWidth(textWidth);
        var changed = ImGui.InputText(label, ref path, 280, ImGuiInputTextFlags.ElideLeft);
        ImGui.PopID();

        if (fileBrowseResults.TryRemove(id, out var browseInput)) {
            path = browseInput;
            changed = true;
        }

        return changed;
    }

    public static void PrependIcon(object target)
    {
        var icon = AppIcons.GetIcon(target);
        if (icon != '\0') {
            ImGui.Text(icon.ToString());
            ImGui.SameLine();
        }
    }

    public static bool InputFolder(string label, [NotNull] ref string? path)
    {
        var id = ImGui.GetID(label);
        var x = ImGui.GetCursorPosX();
        var textWidth = ImGui.CalcItemWidth() - ImGui.CalcTextSize("Browse...").X - ImGui.GetStyle().FramePadding.X * 2 - x;
        ImGui.PushID(label);
        if (ImGui.Button("Browse...")) {
            PlatformUtils.ShowFolderDialog((list) => fileBrowseResults[id] = list, path);
        }
        ImGui.SameLine();
        path ??= "";
        ImGui.SetNextItemWidth(textWidth);
        var changed = ImGui.InputText(label, ref path, 280, ImGuiInputTextFlags.ElideLeft);
        ImGui.PopID();

        if (fileBrowseResults.TryRemove(id, out var browseInput)) {
            path = browseInput;
            changed = true;
        }

        return changed;
    }
}