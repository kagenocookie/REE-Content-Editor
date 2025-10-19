using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Text.Json;
using ContentEditor.App.Windowing;
using ContentEditor.Core;
using ImGuiNET;

namespace ContentEditor.App;

public static class AppImguiHelpers
{
    private static readonly ConcurrentDictionary<uint, string> fileBrowseResults = new();

    public static bool InputFilepath(string label, [NotNull] ref string? path, string? extension = null)
    {
        var id = ImGui.GetID(label);
        var w = ImGui.CalcItemWidth();
        var buttonWidth = ImGui.CalcTextSize("Browse...").X + ImGui.GetStyle().FramePadding.X * 4;
        ImGui.PushID(label);
        if (ImGui.Button("Browse...")) {
            PlatformUtils.ShowFileDialog((list) => fileBrowseResults[id] = list[0], path, extension, false);
        }
        ImGui.SameLine();
        path ??= "";
        ImGui.SetNextItemWidth(w - buttonWidth);
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

    public static void ShowDefaultCopyPopup<T>(ref T value, UIContext context)
    {
        if (ImGui.BeginPopupContextItem(context.label)) {
            ShowDefaultCopyPopupButtons(ref value, context);
            ImGui.EndPopup();
        }
    }

    public static void ShowDefaultCopyPopupButtons<T>(ref T value, UIContext context)
    {
        if (ImGui.Selectable("Copy value")) {
            EditorWindow.CurrentWindow?.CopyToClipboard(JsonSerializer.Serialize(value, JsonConfig.jsonOptionsIncludeFields), $"Copied value of {context.label}!");
            ImGui.CloseCurrentPopup();
        }
        if (ImGui.Selectable("Copy field name")) {
            EditorWindow.CurrentWindow?.CopyToClipboard(context.label, $"Copied {context.label}!");
            ImGui.CloseCurrentPopup();
        }
        if (ImGui.Selectable("Paste value")) {
            UndoRedo.RecordClipboardSet<T>(context);
            ImGui.CloseCurrentPopup();
        }
    }

    public static void ShowDefaultCopyPopup(object? value, Type type, UIContext context)
    {
        if (ImGui.BeginPopupContextItem(context.label)) {
            if (ImGui.Selectable("Copy value")) {
                EditorWindow.CurrentWindow?.CopyToClipboard(JsonSerializer.Serialize(value, type, JsonConfig.jsonOptionsIncludeFields), $"Copied value of {context.label}!");
                ImGui.CloseCurrentPopup();
            }
            if (ImGui.Selectable("Copy field name")) {
                EditorWindow.CurrentWindow?.CopyToClipboard(context.label, $"Copied {context.label}!");
                ImGui.CloseCurrentPopup();
            }
            if (ImGui.Selectable("Paste value")) {
                UndoRedo.RecordClipboardSet(context, type);
                ImGui.CloseCurrentPopup();
            }
            ImGui.EndPopup();
        }
    }

    public static void RedirectMouseInputToScene(Scene scene, bool isHovered, Vector2 viewportTopLeft)
    {
        if (scene.Controller == null || scene.MouseHandler == null) return;

        var absPos = ImGui.GetMousePos();
        var leftDown = ImGui.IsMouseDown(ImGuiMouseButton.Left);
        var rightDown = ImGui.IsMouseDown(ImGuiMouseButton.Right);
        var middleDown = ImGui.IsMouseDown(ImGuiMouseButton.Middle);

        scene.MouseHandler.UpdateMouseDown(EditorWindow.CurrentWindow!.LastMouse, leftDown, rightDown, middleDown, isHovered, absPos, viewportTopLeft);
    }
}