using System.Collections;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Text.Json;
using ContentEditor.App.Graphics;
using ContentEditor.App.Windowing;
using ContentEditor.Core;
using Hexa.NET.ImNodes;
using ReeLib.Common;

namespace ContentEditor.App;

public static class AppImguiHelpers
{
    private static readonly ConcurrentDictionary<uint, string> fileBrowseResults = new();

    public unsafe static void Image(uint handle, Vector2 size)
    {
        ImGui.Image(new ImTextureRef(null, new ImTextureID(handle)), size);
    }

    public unsafe static void Image(uint handle, Vector2 size, Vector2 uv0, Vector2 uv1)
    {
        ImGui.Image(new ImTextureRef(null, new ImTextureID(handle)), size, uv0, uv1);
    }

    public unsafe static ImTextureRef AsTextureRef(this Texture texture) => new ImTextureRef(null, new ImTextureID(texture.Handle));

    public static bool InputFilepath(string label, [NotNull] ref string? path, FileFilter[]? extensions = null)
    {
        var id = ImGui.GetID(label);
        var w = ImGui.CalcItemWidth();
        var buttonWidth = ImGui.CalcTextSize("Browse...").X + ImGui.GetStyle().FramePadding.X * 4;
        ImGui.PushID(label);
        if (ImGui.Button("Browse...")) {
            PlatformUtils.ShowFileDialog((list) => fileBrowseResults[id] = list[0], path, extensions, false);
        }
        ImGui.SameLine();
        path ??= "";
        ImGui.SetNextItemWidth(w - buttonWidth);
        var changed = ImGui.InputText(label, ref path, 280, ImGuiInputTextFlags.ElideLeft);
        ImGui.PopID();

        if (Path.IsPathFullyQualified(path) && ImGui.BeginPopupContextItem(label)) {
            if (ImGui.Selectable("Open in Explorer")) {
                FileSystemUtils.ShowFileInExplorer(path);
            }
            ImGui.EndPopup();
        }

        if (fileBrowseResults.TryRemove(id, out var browseInput)) {
            path = browseInput;
            changed = true;
        }

        return changed;
    }

    public static float NodeContentAvailX(int nodeId)
    {
        return ImNodes.GetNodeDimensions(nodeId).X - (ImGui.GetCursorPosX() - ImNodes.GetNodeEditorSpacePos(nodeId).X) - ImNodes.GetStyle().NodePadding.X;
    }

    public static void NodeSeparator(int nodeId)
    {
        ImGui.GetWindowDrawList().AddLine(ImGui.GetCursorScreenPos(), ImGui.GetCursorScreenPos() + new Vector2(AppImguiHelpers.NodeContentAvailX(nodeId), 0), ImguiHelpers.GetColorU32(ImGuiCol.Separator));
        ImGui.Spacing();
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

        if (Path.IsPathFullyQualified(path) && ImGui.BeginPopupContextItem(label)) {
            if (ImGui.Selectable("Open in Explorer")) {
                FileSystemUtils.ShowFileInExplorer(path);
            }
            ImGui.EndPopup();
        }

        if (fileBrowseResults.TryRemove(id, out var browseInput)) {
            path = browseInput;
            changed = true;
        }

        return changed;
    }

    public static void ShowJsonCopyPopup<T>(in T value, UIContext context)
    {
        if (ImGui.BeginPopupContextItem(context.label)) {
            ShowJsonCopyPopupButtons(in value, context);
            ImGui.EndPopup();
        }
    }

    public static void ShowJsonCopyPopupButtons<T>(in T value, UIContext context)
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

    public static void ShowJsonCopyManualSet<TVal>(in TVal value, UIContext context, Action<UIContext, TVal> setter, string? mergeId)
    {
        if (ImGui.Selectable("Copy value")) {
            EditorWindow.CurrentWindow?.CopyToClipboard(JsonSerializer.Serialize(value, JsonConfig.jsonOptionsIncludeFields), $"Copied value of {context.label}!");
            ImGui.CloseCurrentPopup();
        }
        if (ImGui.Selectable("Paste value")) {
            try {
                var data = EditorWindow.CurrentWindow?.GetClipboard();
                if (string.IsNullOrEmpty(data)) return;

                var val = JsonSerializer.Deserialize<TVal>(data, JsonConfig.jsonOptionsIncludeFields);
                if (val == null) {
                    Logger.Error($"Failed to deserialize {typeof(TVal).Name}.");
                    return;
                }
                UndoRedo.RecordCallbackSetter<UIContext, TVal>(context, context, value, val, setter, mergeId);
            } catch (Exception e) {
                Logger.Error($"Failed to deserialize {typeof(TVal).Name}: " + e.Message);
            }
            ImGui.CloseCurrentPopup();
        }
    }

    public static void ShowJsonCopyPopup(object? value, Type type, UIContext context)
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

    public static bool JsonCopyableTreeNode<T>(UIContext context, JsonSerializerOptions? jsonOptions = null)
    {
        var value = context.Get<T>()!;
        var show = ImguiHelpers.TreeNodeSuffix(context.label, value.ToString() ?? "NULL");
        if (ImGui.BeginPopupContextItem(context.label)) {
            if (ImGui.Selectable("Copy value")) {
                EditorWindow.CurrentWindow?.CopyToClipboard(JsonSerializer.Serialize(value, jsonOptions?? JsonConfig.jsonOptionsIncludeFields), $"Copied value {value}!");
                ImGui.CloseCurrentPopup();
            }
            if (ImGui.Selectable("Paste value")) {
                UndoRedo.RecordClipboardSet<T>(context, jsonOptions);
                UndoRedo.AttachClearChildren(UndoRedo.CallbackType.Both, context);
                ImGui.CloseCurrentPopup();
            }
            ImGui.EndPopup();
        }
        return show;
    }

    public static bool CopyableTreeNode<T>(UIContext context) where T : class
    {
        var instance = context.Get<T>()!;
        var show = ImguiHelpers.TreeNodeSuffix(context.label, instance.ToString() ?? "NULL");
        if (ImGui.BeginPopupContextItem(context.label)) {
            ShowVirtualCopyPopupButtons<T>(instance, context);
            ImGui.EndPopup();
        }
        return show;
    }

    public static void ShowVirtualCopyPopupButtons<T>(UIContext context) where T : class => ShowVirtualCopyPopupButtons<T>(context.Get<T>(), context);
    public static void ShowVirtualCopyPopupButtons<T>(T target, UIContext context) where T : class
    {
        if (ImGui.Selectable("Copy")) {
            VirtualClipboard.CopyToClipboard(target.DeepCloneGeneric<T>());
        }
        if (VirtualClipboard.TryGetFromClipboard<T>(out var newClip) && ImGui.Selectable("Paste (replace)")) {
            UndoRedo.RecordSet(context, newClip.DeepCloneGeneric<T>());
            context.ClearChildren();
        }
    }

    private static IList? _dragDropSourceList;
    private static object? _dragDropPayload;
    private static UIContext? _dragDropSourceContext;
    public static unsafe bool DragDropReorder<T>(UIContext context, bool allowMigrateAcrossLists, [MaybeNullWhen(false)] out T droppedInstance) where T : class
    {
        droppedInstance = null;
        if (context.target is not IList list) {
            Debug.Fail("target is not list");
            return false;
        }

        if (ImGui.BeginDragDropSource(ImGuiDragDropFlags.PayloadNoCrossContext|ImGuiDragDropFlags.PayloadNoCrossProcess|ImGuiDragDropFlags.SourceAllowNullId)) {
            _dragDropPayload = context.Get<T>();
            _dragDropSourceList = list;
            _dragDropSourceContext = context;
            var payloadName = typeof(T).FullName;
            if (!allowMigrateAcrossLists) payloadName += $"{list.GetHashCode()}";
            ImGui.SetDragDropPayload(payloadName, null, 0);
            ImGui.Text(context.label + "    " + _dragDropPayload.ToString());
            ImGui.EndDragDropSource();
        }

        var didDrop = false;
        if (ImGui.BeginDragDropTarget()) {
            var payloadName = typeof(T).FullName;
            if (!allowMigrateAcrossLists) payloadName += $"{list.GetHashCode()}";

            var payload = ImGui.GetDragDropPayload();
            if (payload.Handle != null && payload.IsDataType(payloadName)) {
                var pos = ImGui.GetCursorScreenPos();
                var sourceIndex = list.IndexOf(_dragDropPayload);
                if (sourceIndex == -1 || sourceIndex > list.IndexOf(context.GetRaw())) {
                    pos.Y -= ImGui.GetItemRectSize().Y + ImGui.GetStyle().FramePadding.Y * 2;
                }
                ImGui.GetWindowDrawList().AddLine(pos, pos + new Vector2(ImGui.CalcItemWidth(), 0), ImguiHelpers.GetColorU32(ImGuiCol.PlotHistogram));

                if (_dragDropSourceContext != context && ImGui.AcceptDragDropPayload(payloadName).Handle != null) {
                    droppedInstance = _dragDropPayload as T;
                    if (droppedInstance != null) {
                        UndoRedo.RecordListMove(_dragDropSourceContext, context, _dragDropSourceList, droppedInstance, list, context.Get<T>());
                        didDrop = true;
                    } else {
                        Logger.Error("Drag&drop failed");
                    }
                }
            } else {
                ImGui.SetMouseCursor(ImGuiMouseCursor.NotAllowed);
            }

            ImGui.EndDragDropTarget();
        }
        return didDrop;
    }

    public static bool ShowChildrenNestedReorderableUI<T>(this UIContext context, bool allowMigrateAcrossLists, Func<UIContext, bool>? contextMenu = null) where T : class
    {
        return ShowChildrenNestedReorderableUI<T>(context, allowMigrateAcrossLists, out _, contextMenu);
    }
    public static bool ShowChildrenNestedReorderableUI<T>(this UIContext context, bool allowMigrateAcrossLists, [MaybeNullWhen(false)] out T droppedInstance, Func<UIContext, bool>? contextMenu = null) where T : class
    {
        var show = ImguiHelpers.TreeNodeSuffix(context.label, context.GetRaw()?.ToString() ?? "");
        var didDrop = AppImguiHelpers.DragDropReorder<T>(context, allowMigrateAcrossLists, out droppedInstance);
        if (contextMenu != null && ImGui.BeginPopupContextItem(context.label)) {
            if (contextMenu.Invoke(context)) {
                ImGui.CloseCurrentPopup();
            }
            ImGui.EndPopup();
        }
        if (show) {
            for (int i = 0; i < context.children.Count; i++) {
                context.children[i].ShowUI();
            }
            ImGui.TreePop();
        }
        return didDrop;
    }

    public static bool ShowRecentFiles(RecentFileList files, ref string selectedPath)
    {
        var options = files.ToArray();

        ImGui.SetNextItemAllowOverlap();
        var w = ImGui.CalcItemWidth();

        if (ImguiHelpers.ValueCombo("Recent files", options, options, ref selectedPath)) {
            files.AddRecent(selectedPath);
            return true;
        }

        ImGui.SameLine();
        ImGui.SetCursorScreenPos(new Vector2(ImGui.GetItemRectMin().X + w - (ImGui.GetFrameHeight() * 2 + ImGui.GetStyle().FramePadding.X), ImGui.GetItemRectMin().Y));
        ImGui.SetNextItemAllowOverlap();
        if (ImGui.Button($"{AppIcons.SI_BookmarkClear}")) {
            files.Clear();
            if (!string.IsNullOrEmpty(selectedPath)) {
                files.AddRecent(selectedPath);
            }
        }
        ImguiHelpers.Tooltip("Clear recent files");
        return false;
    }

    public static void RedirectMouseInputToScene(Scene scene, bool isHovered)
    {
        var absPos = ImGui.GetMousePos();
        var leftDown = ImGui.IsMouseDown(ImGuiMouseButton.Left);
        var rightDown = ImGui.IsMouseDown(ImGuiMouseButton.Right);
        var middleDown = ImGui.IsMouseDown(ImGuiMouseButton.Middle);

        scene.Root.MouseHandler.UpdateMouseState(EditorWindow.CurrentWindow!.LastMouse, leftDown, rightDown, middleDown, isHovered, absPos);
    }
}