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
        btnSize.X = 4 * (btnSize.X + style.FramePadding.X) + style.WindowPadding.X * 2;
        ImGui.SetNextWindowPos(offset + new Vector2(size.X - btnSize.X - 8, 8));
        // ImGui.SetNextWindowSize(new Vector2(btnSize.X, btnSize.Y + style.WindowPadding.Y * 2));
        ImGui.BeginChild(WidgetName, new Vector2(btnSize.X, btnSize.Y + style.FramePadding.Y * 2));
        // ImGui.Begin("##toolbar", ImGuiWindowFlags.NoBringToFrontOnFocus|ImGuiWindowFlags.NoCollapse|ImGuiWindowFlags.NoResize|ImGuiWindowFlags.NoTitleBar|ImGuiWindowFlags.NoDocking|ImGuiWindowFlags.NoScrollbar);
        var meshIcon = AppIcons.Mesh.ToString();
        ImGui.SetCursorPos(ImGui.GetCursorPos() + style.FramePadding);

        // axis/grid display toggle
        ImGui.PushStyleColor(ImGuiCol.Text, Colors.Info with { W = AppConfig.Instance.RenderAxis.Get() ? 1 : 0.6f });
        if (ImGui.Button(meshIcon + "##axis")) {
            AppConfig.Instance.RenderAxis.Set(!AppConfig.Instance.RenderAxis);
        }
        ImGui.PopStyleColor();
        if (ImGui.IsItemHovered()) ImGui.SetItemTooltip("Show Gizmos");

        // meshes
        ImGui.SameLine();
        ImGui.PushStyleColor(ImGuiCol.Text, AppConfig.Instance.RenderMeshes.Get() ? Colors.Default : Colors.Faded);
        if (ImGui.Button(meshIcon + "##mesh")) {
            AppConfig.Instance.RenderMeshes.Set(!AppConfig.Instance.RenderMeshes);
        }
        ImGui.PopStyleColor();
        if (ImGui.IsItemHovered()) ImGui.SetItemTooltip("Show Meshes");

        // colliders
        ImGui.SameLine();
        ImGui.PushStyleColor(ImGuiCol.Text, Colors.Colliders with { W = AppConfig.Instance.RenderColliders.Get() ? 1 : 0.6f });
        if (ImGui.Button(meshIcon + "##coll")) {
            AppConfig.Instance.RenderColliders.Set(!AppConfig.Instance.RenderColliders);
        }
        ImGui.PopStyleColor();
        if (ImGui.IsItemHovered()) ImGui.SetItemTooltip("Show Colliders");

        // rcol colliders
        ImGui.SameLine();
        ImGui.PushStyleColor(ImGuiCol.Text, Colors.RequestSetColliders with { W = AppConfig.Instance.RenderRequestSetColliders.Get() ? 1 : 0.6f });
        if (ImGui.Button(meshIcon + "##rcol")) {
            AppConfig.Instance.RenderRequestSetColliders.Set(!AppConfig.Instance.RenderRequestSetColliders);
        }
        ImGui.PopStyleColor();
        if (ImGui.IsItemHovered()) ImGui.SetItemTooltip("Show Request Set Colliders (rcol)");

        ImGui.EndChild();
    }
}