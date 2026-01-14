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

            ImGui.PushStyleColor(ImGuiCol.Text, Colors.IconPrimary with { W = AppConfig.Instance.RenderAxis.Get() ? 1 : 0.6f });
            if (ImGui.Selectable($"{AppIcons.SI_Generic3Axis} ")) {
                AppConfig.Instance.RenderAxis.Set(!AppConfig.Instance.RenderAxis);
            }
            ImGui.PopStyleColor();
            ImGui.SameLine();
            ImGui.PushStyleColor(ImGuiCol.Text, ImguiHelpers.GetColor(ImGuiCol.Text) with { W = AppConfig.Instance.RenderAxis.Get() ? 1 : 0.6f });
            ImGui.Text("Axis Gizmos");
            ImGui.PopStyleColor();

            ImGui.PushStyleColor(ImGuiCol.Text, Colors.FileTypeMESH with { W = AppConfig.Instance.RenderMeshes.Get() ? 1 : 0.6f });
            if (ImGui.Selectable($"{AppIcons.SI_FileType_MESH} ")) {
                AppConfig.Instance.RenderMeshes.Set(!AppConfig.Instance.RenderMeshes);
            }
            ImGui.PopStyleColor();
            ImGui.SameLine();
            ImGui.PushStyleColor(ImGuiCol.Text, ImguiHelpers.GetColor(ImGuiCol.Text) with { W = AppConfig.Instance.RenderMeshes.Get() ? 1 : 0.6f });
            ImGui.Text("Meshes");
            ImGui.PopStyleColor();

            ImGui.PushStyleColor(ImGuiCol.Text, Colors.FileTypeMCOL with { W = AppConfig.Instance.RenderColliders.Get() ? 1 : 0.6f });
            if (ImGui.Selectable($"{AppIcons.SI_FileType_MCOL} ")) {
                AppConfig.Instance.RenderColliders.Set(!AppConfig.Instance.RenderColliders);
            }
            ImGui.PopStyleColor();
            ImGui.SameLine();
            ImGui.PushStyleColor(ImGuiCol.Text, ImguiHelpers.GetColor(ImGuiCol.Text) with { W = AppConfig.Instance.RenderColliders.Get() ? 1 : 0.6f });
            ImGui.Text("MCOL - Physics Colliders");
            ImGui.PopStyleColor();

            ImGui.PushStyleColor(ImGuiCol.Text, Colors.FileTypeRCOL with { W = AppConfig.Instance.RenderRequestSetColliders.Get() ? 1 : 0.6f });
            if (ImGui.Selectable($"{AppIcons.SI_FileType_RCOL} ")) {
                AppConfig.Instance.RenderRequestSetColliders.Set(!AppConfig.Instance.RenderRequestSetColliders);
            }
            ImGui.PopStyleColor();
            ImGui.SameLine();
            ImGui.PushStyleColor(ImGuiCol.Text, ImguiHelpers.GetColor(ImGuiCol.Text) with { W = AppConfig.Instance.RenderRequestSetColliders.Get() ? 1 : 0.6f });
            ImGui.Text("RCOL - Request Set Colliders");
            ImGui.PopStyleColor();

            ImGui.PushStyleColor(ImGuiCol.Text, Colors.Lights with { W = AppConfig.Instance.RenderLights.Get() ? 1 : 0.6f });
            if (ImGui.Selectable($"{AppIcons.Eye} ")) {
                AppConfig.Instance.RenderLights.Set(!AppConfig.Instance.RenderLights);
            }
            ImGui.PopStyleColor();
            ImGui.SameLine();
            ImGui.PushStyleColor(ImGuiCol.Text, ImguiHelpers.GetColor(ImGuiCol.Text) with { W = AppConfig.Instance.RenderLights.Get() ? 1 : 0.6f });
            ImGui.Text("Lights");
            ImGui.PopStyleColor();

            ImGui.PopItemFlag();
            ImGui.EndPopup();
        }
        ImGui.EndChild();
    }
}
