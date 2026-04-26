using System.Numerics;
using ContentEditor.App.ImguiHandling;
using ContentEditor.Core;

namespace ContentEditor.App;

public class SceneVisibilitySettings : ISceneWidget
{
    public static string WidgetName => nameof(SceneVisibilitySettings);

    public void OnIMGUI(UIContext context)
    {
        var window = context.GetWindow();

        var size = window?.Size ?? ImGui.GetWindowViewport().Size - ImGui.GetStyle().WindowPadding;
        var offset = window?.Position ?? new Vector2(0, 0);

        var style = ImGui.GetStyle();
        var btnSize = style.FramePadding * 2 + new Vector2(UI.FontSize, UI.FontSize);
        btnSize.X = (btnSize.X + style.FramePadding.X) + style.FramePadding.X;
        ImGui.SetNextWindowPos(offset + new Vector2(size.X - btnSize.X - 2, 2));
        ImGui.BeginChild(WidgetName, new Vector2(btnSize.X, btnSize.Y + style.FramePadding.Y * 2));
        ImGui.SetCursorPos(ImGui.GetCursorPos() + style.FramePadding);
        if (ImGui.Button($"{AppIcons.Eye}")) {
            ImGui.OpenPopup(WidgetName);
        }
        ImguiHelpers.Tooltip("Object Visibility");
        if (ImGui.BeginPopup(WidgetName)) {
            ImGui.PushItemFlag(ImGuiItemFlags.AutoClosePopups, false);

            ShowToggle("Axis Gizmos", $"{AppIcons.SI_Generic3Axis} ", Colors.IconPrimary, AppConfig.Instance.RenderAxis);
            ShowToggle("Meshes", $"{AppIcons.SI_FileType_MESH} ", Colors.FileTypeMESH, AppConfig.Instance.RenderMeshes);
            ShowToggle("MCOL - Physics Colliders", $"{AppIcons.SI_FileType_MCOL} ", Colors.FileTypeMCOL, AppConfig.Instance.RenderColliders);
            ShowToggle("RCOL - Request Set Colliders", $"{AppIcons.SI_FileType_RCOL} ", Colors.FileTypeRCOL, AppConfig.Instance.RenderRequestSetColliders);
            ShowToggle("Chains", $"{AppIcons.SI_MeshViewerChain} ", Colors.FileTypeCHAIN, AppConfig.Instance.RenderChains);
            ShowToggle("Lights", $"{AppIcons.Eye} ", Colors.Lights, AppConfig.Instance.RenderLights);

            ImGui.PopItemFlag();
            ImGui.EndPopup();
        }
        ImGui.EndChild();
    }

    private static void ShowToggle(string text, string icon, Vector4 color, AppConfig.SettingWrapper<bool> setting)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, color with { W = setting.Get() ? 1 : 0.6f });
        if (ImGui.Selectable(icon)) {
            setting.Set(!setting);
        }
        ImGui.PopStyleColor();
        ImGui.SameLine();
        ImGui.PushStyleColor(ImGuiCol.Text, ImguiHelpers.GetColor(ImGuiCol.Text) with { W = setting.Get() ? 1 : 0.6f });
        ImGui.Text(text);
        ImGui.PopStyleColor();
    }
}
