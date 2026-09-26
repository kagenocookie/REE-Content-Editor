using ContentEditor.App.ImguiHandling;
using ContentEditor.App.Windowing;
using ContentEditor.Core;
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

        var workspace = context.GetWorkspace();
        var isZero = (workspace?.ResourceManager.GetEntityZeroId(instance.Type) ?? 0) == instance.Id;

        for (int i = 0; i < context.children.Count; i++) {
            if (i != 0) {
                ImGui.Spacing();
            }
            var child = context.children[i];
            ImGui.PushID(i);
            var fieldValue = child.Get<IContentResource?>();
            if (fieldValue != null && fieldValue is not NulledResource) {
                var field = instance.Config.GetField(child.EntityParams?.EntityField ?? "");
                if (field != null && !field.IsRequired) {
                    using var pfb = ImguiHelpers.InlinePrefix();
                    if (ImGui.Button($"{AppIcons.SI_GenericDelete}")) {
                        UndoRedo.RecordCallbackSetter(context, instance, fieldValue, null, (e, v) => workspace!.ResourceManager.UpdateEntityField(e, field.name, v));
                        UndoRedo.AttachClearChildren(UndoRedo.CallbackType.Both, context);
                        ImGui.PopID();
                        return;
                    }
                    ImguiHelpers.Tooltip(Lang.Buttons.Delete);
                }
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

            // TODO if merged-group resource type, show subtype selection here when possible

            var resource = workspace.ResourceManager.CreateEntityField(entity, field, ResourceState.Active);
            // note: we dont't need to use context.Set() here since we're clearing the context either way

            UndoRedo.RecordCallbackSetter(context, entity, null, resource, (e, v) => workspace.ResourceManager.UpdateEntityField(e, field.name, v));
            UndoRedo.AttachClearChildren(UndoRedo.CallbackType.Both, parentContext);
        }
    }
}
