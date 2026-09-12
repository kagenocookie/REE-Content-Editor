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
        ImguiHelpers.BeginRect();
        ImGui.Text(context.label);
        ImGui.Spacing();
        foreach (var (type, res) in group.Resources) {
            ImGui.PushID(type);
            var child = context.GetChildByValue(group.Get(type));
            if (child == null) {
                var field = context.GetEntityField()!;
                var subtype = group.ResourceType.Subtypes![type];
                child = context.AddChildContextSetter<GroupedResource, IContentResource?>(type.PrettyPrint(), group, getter: (c) => c!.Get(type), setter: (c, g, v) => g.Set(type, v));
                WindowHandlerFactory.SetupEntityResourceContent(child, field, subtype);
            }
            child.ShowUI();
            ImGui.PopID();
        }
        ImguiHelpers.EndRect();
        ImGui.Spacing();
    }
}
