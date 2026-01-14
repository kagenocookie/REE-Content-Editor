using ContentEditor.Core;
using ReeLib;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;

namespace ContentEditor.App;
public class HierarchyTreeWidget
{
    public string Name = string.Empty;
    public string? EntryKey;
    public Dictionary<string, HierarchyTreeWidget> Children = new();
    // SILVER: These should be calculated dynamically but that sounds like a future problem to me
    const float ActionColumnOffset = 30f;
    const float ActionColumnWidth = 160f;
    public static HierarchyTreeWidget Build(IEnumerable<string> entries)
    {
        var root = new HierarchyTreeWidget();

        foreach (var key in entries) {
            var parts = key.Split('/', StringSplitOptions.RemoveEmptyEntries);
            var current = root;

            for (int i = 0; i < parts.Length; i++) {
                string part = parts[i];
                bool isFile = Path.HasExtension(part);

                if (!current.Children.TryGetValue(part, out var node)) {
                    node = new HierarchyTreeWidget { Name = part, EntryKey = isFile ? key : null };
                    current.Children[part] = node;
                }
                current = node;
            }
        }
        return root;
    }

    public static void Draw(HierarchyTreeWidget node, Action<HierarchyTreeWidget>? drawActions = null, Action<HierarchyTreeWidget>? onOpenFile = null, int hierarchyLayer = 0)
    {
        foreach (var child in node.Children.Values.OrderBy(c => c.EntryKey != null).ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)) {

            ImGui.PushID(child.Name);

            bool isActionHovered = false;
            float rowY = ImGui.GetCursorPosY() + 5f;
            float contentX = ImGui.GetCursorPosX();
            ImGui.SetCursorPosX(ActionColumnOffset);
            ImGui.BeginChild("##actions", new Vector2(ActionColumnWidth, ImGui.GetTextLineHeight() + 10f), ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
            if (!(hierarchyLayer == 0 && child.EntryKey == null)) {
                drawActions?.Invoke(child);
            }
            ImGui.EndChild();
            isActionHovered = ImGui.IsItemHovered();

            ImGui.SetCursorPos(new Vector2(contentX + ActionColumnWidth, rowY));
            if (child.EntryKey != null) {
                char icon = AppIcons.SI_File;
                Vector4 col = ImGui.GetStyle().Colors[(int)ImGuiCol.Text];

                if (Path.HasExtension(child.Name)) {
                    var (fileIcon, fileCol) = AppIcons.GetIcon(PathUtils.ParseFileFormat(child.Name).format);

                    if (fileIcon != '\0') {
                        icon = fileIcon;
                        col = fileCol;
                    }
                }
                ImGui.Dummy(new Vector2(5f, 0));
                ImGui.SameLine();
                ImGui.TextColored(col, $"{icon}");
                ImGui.SameLine();
                ImGui.PushStyleColor(ImGuiCol.Text, isActionHovered ? Colors.TextActive : ImguiHelpers.GetColor(ImGuiCol.Text));
                if (ImGui.Selectable(child.Name)) {
                    onOpenFile?.Invoke(child);
                }
                ImGui.PopStyleColor();
            } else {
                bool isNestedFolder = ImGui.TreeNodeEx($"{AppIcons.SI_FolderEmpty} " + child.Name, ImGuiTreeNodeFlags.DrawLinesToNodes | ImGuiTreeNodeFlags.DefaultOpen | ImGuiTreeNodeFlags.SpanAllColumns);
                if (isNestedFolder) {
                    Draw(child, drawActions, onOpenFile, hierarchyLayer + 1);
                    ImGui.TreePop();
                }
            }
            ImGui.PopID();
        }
    }
}
