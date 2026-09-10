using ContentEditor.App.Windowing;
using ContentPatcher;

namespace ContentEditor.App;

[ObjectImguiHandler(typeof(EnumMappingResource))]
public class EnumMappingEditor : IObjectUIHandler
{
    public void OnIMGUI(UIContext context)
    {
        var res = context.Get<EnumMappingResource>();
        var str = $"{res.ResourceTypeID} | ID {res.ID}: {res.Label} => {res.Value}";
        ImGui.Text(str);
        if (ImGui.BeginPopupContextItem(str)) {
            if (ImGui.Selectable("Copy ID")) {
                EditorWindow.CurrentWindow?.CopyToClipboard(res.ID.ToString());
            }
            if (ImGui.Selectable("Copy Label")) {
                EditorWindow.CurrentWindow?.CopyToClipboard(res.Label.ToString());
            }
            if (ImGui.Selectable("Copy Value")) {
                EditorWindow.CurrentWindow?.CopyToClipboard(res.Value.ToString());
            }
            ImGui.EndPopup();
        }
    }
}
