using System.Numerics;
using ContentEditor.App.ImguiHandling;

namespace ContentEditor.App;

public class SceneCameraControls : ISceneWidget
{
    public static string WidgetName => nameof(SceneCameraControls);

    public void OnIMGUI(UIContext context)
    {
        var scene = context.root.target as Scene;
        if (scene == null) return;

        var window = context.GetWindow();

        var size = window?.Size ?? ImGui.GetWindowViewport().Size - ImGui.GetStyle().WindowPadding;
        var offset = window?.Position ?? new Vector2(0, 0);

        var style = ImGui.GetStyle();
        var btnSize = style.FramePadding * 2 + new Vector2(UI.FontSize, UI.FontSize);
        btnSize.X = (btnSize.X + style.FramePadding.X) + style.FramePadding.X;
        ImGui.SetNextWindowPos(offset + new Vector2(size.X - (btnSize.X - 8) * 3, 8));
        ImGui.BeginChild(WidgetName, new Vector2(btnSize.X, btnSize.Y + style.FramePadding.Y * 2));
        ImGui.SetCursorPos(ImGui.GetCursorPos() + style.FramePadding);
        if (ImGui.Button($"{AppIcons.GameObject}")) {
            ImGui.OpenPopup(WidgetName);
        }
        if (ImGui.BeginPopup(WidgetName)) {
            scene.Controller.ShowCameraControls();
            if (Math.Abs(scene.Controller.MoveSpeed - AppConfig.Settings.SceneView.MoveSpeed) > 0.001f) {
                AppConfig.Settings.SceneView.MoveSpeed = scene.Controller.MoveSpeed;
                AppConfig.Settings.Save();
            }
            ImGui.EndPopup();
        }

        ImGui.EndChild();
    }
}