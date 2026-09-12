using ContentEditor.App.Windowing;
using ContentPatcher;

namespace ContentEditor.App;

public class EntityHandler : IObjectUIHandler
{
    public void OnIMGUI(UIContext context)
    {
        var instance = context.Get<ResourceEntity>();
        ImGui.Text($"{context.label}: {instance.Label}");
        ImGui.SameLine();
        if (ImGui.Button($"Copy ID:{instance.Id}")) {
            EditorWindow.CurrentWindow?.CopyToClipboard(instance.Id.ToString(), "ID copied!");
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
