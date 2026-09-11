using ContentEditor.App.ImguiHandling;
using ContentEditor.Core;
using ContentPatcher;
using ReeLib;

namespace ContentEditor.App;

[ObjectImguiHandler(typeof(RSZObjectResource))]
public sealed class ContentEditorRszInstanceHandler : IObjectUIHandler
{
    public void OnIMGUI(UIContext context)
    {
        var instance = context.Get<RSZObjectResource>();
        var field = context.GetEntityField<ObjectField>();
        if (instance == null) {
            ImGui.Text(context.label);
            var workspace = context.GetWorkspace();
            if (workspace != null) {
                ImGui.SameLine();
                ImGui.PushID(context.label);
                if (ImGui.Button("Create")) {
                    context.CreateEntityResource(workspace, field?.Field);
                }
                ImGui.PopID();
            }
            return;
        }
        if (context.children.Count == 0 || context.children[0].uiHandler == null) {
            var child = context.AddChild(context.label, instance.Instance, setter: (ctx, val) => instance.Instance = (RszInstance?)val!);
            WindowHandlerFactory.SetupRSZInstanceHandler(child);
        }
        ImGui.Spacing();
        ImguiHelpers.BeginRect();
        var nested = field?.forceNested ?? instance.Instance.Fields.Length > 2;
        if (nested) {
            if (ImGui.TreeNode(context.label)) {
                context.children[0].ShowUI();
                ImGui.TreePop();
            }
        } else {
            context.children[0].ShowUI();
        }
        ImguiHelpers.EndRect(4);
        ImGui.Spacing();
    }
}
