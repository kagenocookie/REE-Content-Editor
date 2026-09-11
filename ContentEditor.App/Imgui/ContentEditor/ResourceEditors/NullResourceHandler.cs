using ContentEditor;
using ContentEditor.App.ImguiHandling;
using ContentPatcher;

namespace ContentEditor.App;

public class NullResourceHandler : IObjectUIHandler
{
    public void OnIMGUI(UIContext context)
    {
        // ImGui.Text(context.label);
        var workspace = context.GetWorkspace();
        if (workspace == null || workspace.CurrentBundle == null) {
            return;
        }
        var param = context.EntityParams;
        if (param != null && !string.IsNullOrEmpty(param.ResourceType)) {
            ImGui.SameLine();
            if (ImGui.Button(Lang.Buttons.Create)) {
                // TODO determine ID source (is there an entity or not? which field specifically are we in?)
                var entity = context.GetOwnerEntity();
                var field = param.EntityField == null ? null : entity?.Config.GetField(param.EntityField);
                IContentResource resource;
                if (entity != null && field != null) {
                    context.CreateEntityResource(workspace, field);
                    return;
                    // resource = workspace.ResourceManager.CreateEntityResource(entity, field, ResourceState.Active);
                } else {
                    resource = workspace.ResourceManager.CreateResource(param.ResourceType, ResourceState.Active, id: param.ResourceId).resource;
                }

                context.Set(resource);
                context.parent?.ClearChildren();
            }
        }

        ImGui.TextColored(Colors.Faded, Lang.General.ObjectIsNull);
    }
}
