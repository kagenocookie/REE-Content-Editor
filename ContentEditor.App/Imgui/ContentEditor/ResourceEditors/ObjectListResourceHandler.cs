using ContentEditor.App.ImguiHandling;
using ContentPatcher;
using ReeLib;

namespace ContentEditor.App;

[ObjectImguiHandler(typeof(RSZObjectListResource))]
public sealed class ObjectListResourceHandler : IObjectUIHandler
{
    public void OnIMGUI(UIContext context)
    {
        var list = context.Get<RSZObjectListResource>()?.Instances;
        var field = context.GetEntityField<ObjectArray>()!;
        if (context.children.Count == 0) {
            var child = context.AddChild(context.label, list);
            child.uiHandler = new ArrayRSZHandler(new RszField() { name = "", type = RszFieldType.Object, original_type = field.ResourceType });
        }
        if (list == null) {
            ImGui.Text(context.label);
            var workspace = context.GetWorkspace();
            if (workspace != null) {
                ImGui.PushID(context.label);
                if (ImGui.Button("Create")) {
                    context.CreateEntityResource(workspace, field.Field, context.EntityParams?.ResourceType);
                }
                ImGui.PopID();
            }
            ImGui.SameLine();
            return;
        }
        context.children[0].ShowUI();
    }
}
