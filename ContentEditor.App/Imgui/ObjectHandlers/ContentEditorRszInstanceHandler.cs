using ContentEditor.App.ImguiHandling;
using ContentPatcher;
using ImGuiNET;
using ReeLib;

namespace ContentEditor.App;

[CustomFieldHandler(typeof(ObjectCustomField))]
public sealed class ContentEditorRszInstanceHandler(CustomField field) : IObjectUIHandler, IObjectUIInstantiator
{
    public static Func<CustomField, IObjectUIHandler> GetFactory() => (field) => new ContentEditorRszInstanceHandler(field);

    public void OnIMGUI(UIContext context)
    {
        var instance = context.Get<RSZObjectResource>();
        if (instance == null) {
            ImGui.Text(context.label);
            var workspace = context.GetWorkspace();
            if (workspace != null) {
                ImGui.SameLine();
                ImGui.PushID(context.label);
                if (ImGui.Button("Create")) {
                    context.CreateEntityResource<RSZObjectResource>(workspace, field);
                }
                ImGui.PopID();
            }
            return;
        }
        if (context.children.Count == 0) {
            var child = context.AddChild(context.label, instance.Instance, setter: (ctx, val) => instance.Instance = (RszInstance?)val!);
            WindowHandlerFactory.CreateRSZInstanceHandlerContext(child);
        }
        context.children[0].ShowUI();
    }
}
