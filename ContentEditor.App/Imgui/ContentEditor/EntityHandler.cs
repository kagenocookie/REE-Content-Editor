using ContentEditor.App.Windowing;
using ContentPatcher;

namespace ContentEditor.App;

[ObjectImguiHandler(typeof(ResourceEntity))]
public class EntityHandler : IObjectUIHandler
{
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

        for (int i = 0; i < context.children.Count; i++) {
            var child = context.children[i];
            ImGui.PushID(i);
            child.ShowUI();
            ImGui.PopID();
            ImGui.Spacing();
        }
    }
}
