using System.Numerics;
using ContentEditor.Core;
using ImGuiNET;

namespace ContentEditor.App.ImguiHandling;

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
        if (ImGui.Selectable(context.label, node == inspector?.PrimaryTarget)) {
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
