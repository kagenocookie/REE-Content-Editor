using ContentEditor.App.ImguiHandling;
using ContentEditor.App.Windowing;
using ContentPatcher;

namespace ContentEditor.App;

[ObjectImguiHandler(typeof(ResourceEntity))]
public class EntityHandler : IObjectUIHandler
{
    public static readonly EntityHandler Instance = new();

    public void OnIMGUI(UIContext context)
    {
        var instance = context.Get<ResourceEntity>();
        ImGui.Text(instance.Label);
        ImGui.SameLine();
        if (ImGui.Button($"Copy ID:{instance.Id}")) {
            EditorWindow.CurrentWindow?.CopyToClipboard(instance.Id.ToString(), "ID copied!");
        }
        if (context.children.Count == 0) {
            WindowHandlerFactory.CreateEntityHandler(context);
        }

        var isZero = (context.GetWorkspace()?.ResourceManager.GetEntityZeroId(instance.Type) ?? 0) == instance.Id;

        for (int i = 0; i < context.children.Count; i++) {
            if (i != 0) {
                ImGui.Spacing();
            }
            var child = context.children[i];
            ImGui.PushID(i);
            var fieldValue = child.GetRaw();
            if (fieldValue != null) {
                child.ShowUI();
            } else if (isZero) {
                ImGui.Text(child.label);
                ImGui.SameLine();
                ImGui.TextColored(Colors.Faded, Lang.General.ObjectIsNull);
            } else {
                ShowNullField(child, context);
            }
            ImGui.PopID();
        }
    }

    private static void ShowNullField(UIContext context, UIContext parentContext)
    {
        var workspace = parentContext.GetWorkspace();
        ImGui.Text(context.label);
        ImGui.SameLine();
        ImGui.TextColored(Colors.Faded, Lang.General.ObjectIsNull);
        if (workspace == null || workspace.CurrentBundle == null) {
            return;
        }

        var param = context.EntityParams;
        if (string.IsNullOrEmpty(param?.ResourceType)) return;

        ImGui.SameLine();
        if (ImGui.Button(Lang.Buttons.Create)) {
            var entity = context.GetOwnerEntity();
            var field = context.GetEntityField();
            if (field == null || entity == null) {
                Logger.Error("Entity field could not be determined");
                return;
            }

            // TODO if group resource type, show subtype selection here when possible

            var resource = workspace.ResourceManager.CreateEntityField(entity, field, ResourceState.Active);
            context.Set(resource);
            UndoRedo.RecordCallbackSetter(context, entity, null, resource, (e, v) => e.Set(field.name, v));
            UndoRedo.AttachClearChildren(UndoRedo.CallbackType.Both, parentContext);
        }
    }
}
