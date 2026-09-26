using Assimp;
using ContentEditor.App.ImguiHandling;
using ContentEditor.App.Windowing;
using ReeLib;

namespace ContentEditor.App;

public static class AppExtensions
{
    public static string GetFormatIDFromExtension(this AssimpContext context, string extension)
    {
        foreach (var fmt in context.GetSupportedExportFormats()) {
            if (fmt.FileExtension == extension) {
                return fmt.FormatId;
            }
        }

        throw new NotImplementedException("Unsupported export format " + extension);
    }
    public static bool ComponentAvailable<T>(this Workspace env) where T : IFixedClassnameComponent
    {
        return env.RszParser.GetRSZClass(T.Classname) != null;
    }

    public static SceneView CreateEmbedded3DScene(this UIContext context, string sceneName)
    {
        var window = EditorWindow.CurrentWindow;
        var scene = EditorWindow.CurrentWindow!.SceneManager.CreateScene(sceneName, "", true);
        scene.Type = SceneType.Independent;
        scene.Root.Controller.Keyboard = EditorWindow.CurrentWindow.LastKeyboard;
        scene.Root.Controller.MoveSpeed = AppConfig.Settings.MeshViewer.MoveSpeed;
        scene.OwnRenderContext.AddDefaultSceneGizmos();
        scene.AddWidget<SceneVisibilitySettings>();

        var sceneView = new SceneView(context.GetWorkspace()!, scene);
        var scnWnd = WindowData.CreateEmbeddedWindow(context, context.GetWindow()!, sceneView, sceneName);
        return sceneView;
    }
}