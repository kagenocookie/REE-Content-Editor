using ContentEditor.App.Windowing;
using ContentPatcher;
using ImGuiNET;

namespace ContentEditor.App;

public class ContentEditorEntityImguiHandler : IObjectUIHandler
{
    public void OnIMGUI(UIContext context)
    {
        var instance = context.Get<ResourceEntity>();
        ImGui.Text($"{context.label}: {instance.Label}");
        ImGui.SameLine();
        if (ImGui.Button($"Copy ID:{instance.Id}")) {
            EditorWindow.CurrentWindow?.CopyToClipboard(instance.Id.ToString());
        }

        foreach (var child in context.children) {
            child.ShowUI();
        }
    }
}
