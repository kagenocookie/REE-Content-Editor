using System.Numerics;
using ContentEditor.App.ImguiHandling;
using ImGuiNET;

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
        ImGui.SetNextWindowPos(offset + new Vector2(size.X - btnSize.X - 8, 8));
        ImGui.BeginChild(WidgetName, new Vector2(btnSize.X, btnSize.Y + style.FramePadding.Y * 2));
        ImGui.SetCursorPos(ImGui.GetCursorPos() + style.FramePadding);
        if (ImGui.Button($"{AppIcons.Eye}")) {
            ImGui.OpenPopup(WidgetName);
        }
        if (ImGui.BeginPopup(WidgetName)) {
            ImGui.PushStyleColor(ImGuiCol.Text, Colors.Info with { W = AppConfig.Instance.RenderAxis.Get() ? 1 : 0.6f });
            if (ImGui.Button($"{AppIcons.SI_Generic3Axis} Axis Gizmos")) {
                AppConfig.Instance.RenderAxis.Set(!AppConfig.Instance.RenderAxis);
            }
            ImGui.PopStyleColor();

            ImGui.PushStyleColor(ImGuiCol.Text, AppConfig.Instance.RenderMeshes.Get() ? Colors.Default : Colors.Faded);
            if (ImGui.Button($"{AppIcons.SI_FileType_MESH} Meshes")) {
                AppConfig.Instance.RenderMeshes.Set(!AppConfig.Instance.RenderMeshes);
            }
            ImGui.PopStyleColor();

            ImGui.PushStyleColor(ImGuiCol.Text, Colors.Colliders with { W = AppConfig.Instance.RenderColliders.Get() ? 1 : 0.6f });
            if (ImGui.Button($"{AppIcons.SI_FileType_MCOL} Physics Colliders")) {
                AppConfig.Instance.RenderColliders.Set(!AppConfig.Instance.RenderColliders);
            }
            ImGui.PopStyleColor();

            ImGui.PushStyleColor(ImGuiCol.Text, Colors.RequestSetColliders with { W = AppConfig.Instance.RenderRequestSetColliders.Get() ? 1 : 0.6f });
            if (ImGui.Button($"{AppIcons.SI_FileType_RCOL} Request Set Colliders")) {
                AppConfig.Instance.RenderRequestSetColliders.Set(!AppConfig.Instance.RenderRequestSetColliders);
            }
            ImGui.PopStyleColor();

            ImGui.PushStyleColor(ImGuiCol.Text, Colors.Lights with { W = AppConfig.Instance.RenderLights.Get() ? 1 : 0.6f });
            if (ImGui.Button($"{AppIcons.Eye} Lights")) {
                AppConfig.Instance.RenderLights.Set(!AppConfig.Instance.RenderLights);
            }
            ImGui.PopStyleColor();

            ImGui.EndPopup();
        }

        ImGui.EndChild();
    }
}
