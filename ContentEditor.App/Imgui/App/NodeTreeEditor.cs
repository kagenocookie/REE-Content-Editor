using ContentEditor.Core;
using ImGuiNET;

namespace ContentEditor.App.ImguiHandling;

public abstract class NodeTreeEditor<TNodeHolder, TSelf> : IObjectUIHandler where TNodeHolder : NodeObject<TNodeHolder>
    where TSelf : NodeTreeEditor<TNodeHolder, TSelf>, new()
{
    protected virtual void HandleContextMenu(TNodeHolder node, UIContext context) {}

    public void OnIMGUI(UIContext context)
    {
        var node = context.Get<TNodeHolder>();
        var showChildren = context.StateBool;
        if (node.Children.Count == 0) {
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
        var inspector = context.FindInterfaceInParentHandlers<IInspectorController>();
        if (ImGui.Selectable(context.label, node == inspector?.PrimaryTarget)) {
            if (inspector != null) {
                inspector.SetPrimaryInspector(node);
            }
        }
        if (ImGui.BeginPopupContextItem()) {
            HandleContextMenu(node, context);
            ImGui.EndPopup();
        }

        if (showChildren) {
            ImGui.Indent(ImGui.GetStyle().IndentSpacing);
            for (int i = 0; i < node.Children.Count; i++) {
                var child = node.Children[i];
                while (i >= context.children.Count || context.children[i].target != child) {
                    context.children.RemoveAtAfter(i);
                    context.AddChild(child.Name + "##" + context.children.Count, child, new TSelf());
                }
                var childCtx = context.children[i];
                var isNameMismatch = !childCtx.label.StartsWith(child.Name) || !childCtx.label.AsSpan().Slice(child.Name.Length, 2).SequenceEqual("##");
                if (isNameMismatch) {
                    childCtx.label = child.Name + "##" + i;
                }
                childCtx.ShowUI();
            }
            ImGui.Unindent(ImGui.GetStyle().IndentSpacing);
        }
    }
}
