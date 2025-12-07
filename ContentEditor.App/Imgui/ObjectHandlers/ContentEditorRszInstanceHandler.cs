using ContentEditor.App.ImguiHandling;
using ContentEditor.Core;
using ContentPatcher;
using ReeLib;

namespace ContentEditor.App;

[CustomFieldHandler(typeof(ObjectCustomField))]
public sealed class ContentEditorRszInstanceHandler(ObjectCustomField field) : IObjectUIHandler, IObjectUIInstantiator
{
    public static Func<CustomField, IObjectUIHandler> GetFactory() => (field) => new ContentEditorRszInstanceHandler((ObjectCustomField)field);

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
            WindowHandlerFactory.SetupRSZInstanceHandler(child);
        }
        ImGui.Spacing();
        ImguiHelpers.BeginRect();
        var nested = field.forceNested ?? instance.Instance.Fields.Length > 2;
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
