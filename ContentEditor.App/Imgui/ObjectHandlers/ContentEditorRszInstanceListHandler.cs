using ContentEditor.App.ImguiHandling;
using ContentPatcher;
using ImGuiNET;
using ReeLib;

namespace ContentEditor.App;

[CustomFieldHandler(typeof(RszArrayCustomField))]
public sealed class ContentEditorRszInstanceListHandler(CustomField field) : IObjectUIHandler, IObjectUIInstantiator
{
    public static Func<CustomField, IObjectUIHandler> GetFactory() => (field) => new ContentEditorRszInstanceListHandler(field);

    public void OnIMGUI(UIContext context)
    {
        var list = context.Get<RSZObjectListResource>()?.Instances;
        if (context.children.Count == 0) {
            var child = context.AddChild(context.label, list);
            child.uiHandler = new ArrayRSZHandler(new RszField() { name = "", type = RszFieldType.Object, original_type = field.ResourceIdentifier });
        }
        if (list == null) {
            ImGui.Text(context.label);
            var workspace = context.GetWorkspace();
            if (workspace != null) {
                if (ImGui.Button("Create")) {
                    context.CreateEntityResource<RSZObjectListResource>(workspace, field);
                }
            }
            ImGui.SameLine();
            return;
        }
        context.children[0].ShowUI();
    }
}
