using ContentEditor.App.ImguiHandling;
using ContentEditor.Core;
using ContentPatcher;

namespace ContentEditor.App;

[ObjectImguiHandler(typeof(GroupedResource))]
public class GroupedResourceUIHandler : IObjectUIHandler
{
    public void OnIMGUI(UIContext context)
    {
        var group = context.Get<GroupedResource>();
        var nested = group.ResourceType.OriginalConfig?.GetParam<bool>("nested", false) == true;
        if (nested) {
            if (!ImGui.TreeNode(context.label)) {
                return;
            }
        } else {
            ImguiHelpers.BeginRect();
            ImGui.Text(context.label);
            ImGui.Spacing();
        }
        foreach (var (type, res) in group.Resources) {
            ImGui.PushID(type);
            var subres = group.Get(type);
            if (subres == null) {
                ImGui.Text(type.PrettyPrint());
                ImGui.SameLine();
                ImGui.TextColored(Colors.Faded, Lang.General.ObjectIsNull);
                ImGui.SameLine();
                if (ImGui.Button(Lang.Buttons.Create)) {
                    var workspace = context.GetWorkspace();
                    var entity = context.GetOwnerEntity();
                    var field = context.GetEntityField();
                    if (workspace == null || entity == null || field == null) {
                        Logger.Error(Lang.Errors.MissingEntityContext);
                        ImGui.PopID();
                        continue;
                    }

                    var subresourceType = field.Config.Subtypes![type];
                    var resource = workspace.ResourceManager.CreateSubResource(entity, field, ResourceState.Active, subresourceType);
                    UndoRedo.RecordCallbackSetter(context, group, null, resource, (g, v) => g.Set(type, v));
                    UndoRedo.AttachClearChildren(UndoRedo.CallbackType.Both, context);
                }
                ImGui.PopID();
                continue;
            }
            var child = context.GetChildByValue(subres);
            if (child == null) {
                var field = context.GetEntityField()!;
                var subtype = group.ResourceType.Subtypes![type];
                child = context.AddChildContextSetter<GroupedResource, IContentResource?>(type.PrettyPrint(), group, getter: (c) => c!.Get(type), setter: (c, g, v) => g.Set(type, v));
                WindowHandlerFactory.SetupEntityResourceContent(child, field, subtype);
            }
            child.ShowUI();
            ImGui.PopID();
        }
        if (nested) {
            ImGui.TreePop();
        } else {
            ImguiHelpers.EndRect();
            ImGui.Spacing();
        }
    }
}
