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
        var border = group.ResourceType.OriginalConfig?.GetParam<bool>("border", true) == true;
        if (nested) {
            if (!ImGui.TreeNode(context.label)) {
                return;
            }
        } else {
            if (border) {
                ImguiHelpers.BeginRect();
            }
            if (!context._label.StartsWith("##")) {
                ImGui.Text(context.label);
                ImGui.Spacing();
            }
        }
        var field = context.GetEntityField()!;
        foreach (var (type, res) in group.Resources) {
            ImGui.PushID(type);
            var subres = group.Get(type);
            if (subres == null || subres is NulledResource) {
                ImGui.Text(type.PrettyPrint());
                ImGui.SameLine();
                ImGui.TextColored(Colors.Faded, Lang.General.ObjectIsNull);
                ImGui.SameLine();
                if (ImGui.Button(Lang.Buttons.Create)) {
                    var workspace = context.GetWorkspace();
                    var entity = context.GetOwnerEntity();
                    if (workspace == null || entity == null) {
                        Logger.Error(Lang.Errors.MissingEntityContext);
                        ImGui.PopID();
                        continue;
                    }

                    var subresourceType = field.Config.Subtypes![type];
                    var resource = workspace.ResourceManager.CreateSubResource(entity, field, ResourceState.Active, subresourceType);
                    UndoRedo.RecordCallbackSetter(context, group, subres, resource, (g, v) => g.Set(type, v));
                    UndoRedo.AttachClearChildren(UndoRedo.CallbackType.Both, context);
                }
                ImGui.PopID();
                continue;
            }

            var child = context.GetChildByValue(subres);
            var subtype = group.ResourceType.Subtypes![type];
            if (child == null) {
                child = context.AddChildContextSetter<GroupedResource, IContentResource?>(type.PrettyPrint(), group, getter: (c) => c!.Get(type), setter: (c, g, v) => g.Set(type, v));
                WindowHandlerFactory.SetupEntityResourceContent(child, field, subtype);
            }

            if (!subtype.IsRequired) {
                using var pfb = ImguiHelpers.InlinePrefix();
                if (ImGui.Button($"{AppIcons.SI_GenericDelete}")) {
                    var workspace = context.GetWorkspace();
                    // note: the way it's set up right now, this doesn't handle nested group resources as force dnull, since the subgroup itself does not have a resource path
                    // a full fix with the current NulledResource method would mean going down the hierarchy and marking only the leaf resources as null but keeping the intermediate groups
                    // keeping it as is for now because there's not much usecase for forced deletion anyway
                    var nullItem = string.IsNullOrEmpty(subres.FileResourcePath) ? null : new NulledResource(subres.ResourceType, subres.FileResourcePath);
                    UndoRedo.RecordCallbackSetter(context, group, subres, nullItem, (g, v) => g.Set(type, v));
                    UndoRedo.AttachClearChildren(UndoRedo.CallbackType.Both, context);
                    ImGui.PopID();
                    continue;
                }

                ImguiHelpers.Tooltip(Lang.Buttons.Delete);
            }
            child.ShowUI();
            ImGui.PopID();
        }
        if (nested) {
            ImGui.TreePop();
        } else {
            if (border) {
                ImguiHelpers.EndRect();
            }
            ImGui.Spacing();
        }
    }
}
