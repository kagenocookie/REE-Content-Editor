using System.Numerics;
using ContentEditor.Core;
using ImGuiNET;

namespace ContentEditor.App.ImguiHandling;

public interface IFilterRoot
{
    bool HasFilterActive { get; }
    bool IsMatch(object? obj);
    object? MatchedObject { get; set; }
}

public static class NodeEditorUtils
{
    public static bool ShowFilteredNode<TNodeHolder>(IFilterRoot filter, TNodeHolder node)
        where TNodeHolder : INodeObject<TNodeHolder>
    {
        var match = filter.IsMatch(node);
        if (match) {
            AppImguiHelpers.PrependIcon(node);
            if (ImGui.Selectable(node.Name, false)) {
                filter.MatchedObject = node;
            }
        }

        match = ShowFilteredChildren<TNodeHolder, TNodeHolder>(filter, node) || match;
        return match;
    }

    private static bool AnyChildMatches<TNodeHolder>(IFilterRoot filter, TNodeHolder node)
        where TNodeHolder : INodeObject<TNodeHolder>
    {
        foreach (var subchild in node.GetAllChildren()) {
            if (filter.IsMatch(subchild)) {
                return true;
            }

            if (subchild is Folder subfolder && typeof(TNodeHolder) == typeof(Folder)) {
                foreach (var go in subfolder.GameObjects) {
                    if (filter.IsMatch(go) || AnyChildMatches<GameObject>(filter, go)) {
                        return true;
                    }
                }
            }
        }

        if (node is Folder folder && typeof(TNodeHolder) == typeof(Folder)) {
            foreach (var go in folder.GameObjects) {
                if (filter.IsMatch(go) || AnyChildMatches<GameObject>(filter, go)) {
                    return true;
                }
            }
        }

        return false;
    }

    public static bool ShowFilteredChildren<TNodeHolder, TChildType>(IFilterRoot filter, TNodeHolder node)
        where TNodeHolder : INodeObject<TChildType>
        where TChildType : INodeObject<TChildType>
    {
        var i = 0;
        bool hasAnyChildMatch = false;
        foreach (var child in node.Children) {
            var hasChildMatch = AnyChildMatches(filter, child);

            hasAnyChildMatch = hasAnyChildMatch || hasChildMatch;
            var selfMatch = filter.IsMatch(child);
            if (!selfMatch && !hasChildMatch) {
                continue;
            }
            if (!selfMatch) {
                ImGui.PushStyleColor(ImGuiCol.Text, Colors.Faded);
            }
            AppImguiHelpers.PrependIcon(child);
            if (ImGui.Selectable(child.Name, false)) {
                filter.MatchedObject = child;
            }
            if (!selfMatch) {
                ImGui.PopStyleColor();
            }

            if (hasChildMatch) {
                ImGui.PushID(i++);
                ImGui.Indent(ImGui.GetStyle().IndentSpacing);
                hasAnyChildMatch = ShowFilteredChildren<TChildType, TChildType>(filter, child) || hasAnyChildMatch;
                ImGui.Unindent(ImGui.GetStyle().IndentSpacing);
                ImGui.PopID();
            }
        }

        if (node is Folder folder2 && typeof(TChildType) == typeof(Folder)) {
            hasAnyChildMatch = NodeEditorUtils.ShowFilteredChildren<Folder, GameObject>(filter, folder2) || hasAnyChildMatch;
        }
        return hasAnyChildMatch;
    }

}

public abstract class NodeTreeEditor<TNodeHolder, TSelf> : IObjectUIHandler
    where TNodeHolder : NodeObject<TNodeHolder>
    where TSelf : IObjectUIHandler, new()
{
    protected Vector4 nodeColor = Vector4.One;
    protected bool EnableContextMenu = true;

    protected virtual void HandleContextMenu(TNodeHolder node, UIContext context) { }

    protected virtual void HandleSelect(UIContext context, TNodeHolder node)
    {
        context.FindHandlerInParents<IInspectorController>()?.SetPrimaryInspector(node);
    }

    public virtual void OnIMGUI(UIContext context)
    {
        var node = context.Get<TNodeHolder>();
        var filter = context.FindHandlerInParents<IFilterRoot>();
        if (filter?.HasFilterActive == true) {
            if (filter.MatchedObject == null) {
                NodeEditorUtils.ShowFilteredNode(filter, node);
                return;
            }

            if (filter.MatchedObject == node) {
                ImGui.SetNextItemOpen(true, ImGuiCond.Once);
                HandleSelect(context, node);
            } else if (filter.MatchedObject is TNodeHolder matchNode) {
                if (node.IsParentOf(matchNode)) {
                    ImGui.SetNextItemOpen(true, ImGuiCond.Once);
                    context.StateBool = true;
                }
            } else if (filter.MatchedObject is INodeObject<TNodeHolder> node2 && node is INodeObject<TNodeHolder> selfNode) {
                if (selfNode.IsParentOf(node2)) {
                    ImGui.SetNextItemOpen(true, ImGuiCond.Once);
                    context.StateBool = true;
                }
            }
        }
        var showChildren = context.StateBool;
        if (context.children.Count == 0 && node.Children.Count == 0) {
            // ImGui.Button(context.label);
        } else if (!context.StateBool) {
            if (ImGui.ArrowButton($"arrow##{context.label}", ImGuiDir.Right)) {
                showChildren = context.StateBool = true;
            }
            ImGui.SameLine();
        } else {
            if (ImGui.ArrowButton($"arrow##{context.label}", ImGuiDir.Down)) {
                showChildren = context.StateBool = false;
            }
            ImGui.SameLine();
        }
        var inspector = context.FindHandlerInParents<IInspectorController>();
        ImGui.PushStyleColor(ImGuiCol.Text, nodeColor);
        AppImguiHelpers.PrependIcon(node);
        if (ImGui.Selectable(node.Name, node == inspector?.PrimaryTarget)) {
            HandleSelect(context, node);
        }
        ImGui.PopStyleColor();
        if (EnableContextMenu && ImGui.BeginPopupContextItem()) {
            HandleContextMenu(node, context);
            ImGui.EndPopup();
        }

        if (showChildren) {
            ImGui.Indent(ImGui.GetStyle().IndentSpacing);
            ShowChildren(context, node);
            ImGui.Unindent(ImGui.GetStyle().IndentSpacing);
        }
    }

    protected void ShowChildren(UIContext context, TNodeHolder node)
    {
        var offset = context.children.FindIndex(ch => ch.uiHandler is TSelf);
        if (offset == -1) offset = context.children.Count;

        for (int i = 0; i < node.Children.Count; i++) {
            var child = node.Children[i];
            while (i + offset >= context.children.Count || context.children[i + offset].target != child) {
                context.children.RemoveAtAfter(i + offset);
                context.AddChild(child.Name + "##" + context.children.Count, child, new TSelf());
            }
            var childCtx = context.children[i + offset];
            var isNameMismatch = !childCtx.label.StartsWith(child.Name) || !childCtx.label.AsSpan().Slice(child.Name.Length, 2).SequenceEqual("##");
            if (isNameMismatch) {
                childCtx.label = child.Name + "##" + i;
            }
            ImGui.PushID(childCtx.label);
            childCtx.ShowUI();
            ImGui.PopID();
        }
    }
}
