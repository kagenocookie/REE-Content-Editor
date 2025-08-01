using ContentEditor.Core;
using ImGuiNET;

namespace ContentEditor.App.ImguiHandling;

public abstract class NodeTreeEditor<TNodeHolder, TSelf> : IObjectUIHandler where TNodeHolder : NodeObject<TNodeHolder>
    where TSelf : NodeTreeEditor<TNodeHolder, TSelf>, new()
{
    protected virtual void HandleContextMenu(UIContext context) {}

    public void OnIMGUI(UIContext context)
    {
        var node = context.Get<TNodeHolder>();
        bool showChildren = false;
        if (node.Children.Count == 0) {
            // ImGui.Button(context.label);
        } else if (context.state == null) {
            showChildren = false;
            if (ImGui.ArrowButton($"arrow##{context.label}", ImGuiDir.Right)) {
                showChildren = true;
                context.state = string.Empty;
            }
            ImGui.SameLine();
        } else {
            showChildren = true;
            if (ImGui.ArrowButton($"arrow##{context.label}", ImGuiDir.Down)) {
                showChildren = false;
                context.state = null;
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
            HandleContextMenu(context);
            ImGui.EndPopup();
        }

        if (showChildren) {
            ImGui.Indent(ImGui.GetStyle().IndentSpacing);
            int i = 0;
            foreach (var child in node.Children) {
                while (i >= context.children.Count || context.children[i].target != child) {
                    context.children.RemoveAtAfter(i);
                    context.AddChild(child.Name + "##" + context.children.Count, child, new TSelf());
                }
                var childCtx = context.children[i++];
                childCtx.ShowUI();
            }
            ImGui.Unindent(ImGui.GetStyle().IndentSpacing);
        }
    }
}
