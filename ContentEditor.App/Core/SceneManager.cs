using ContentEditor.Editor;
using ContentPatcher;

namespace ContentEditor.App;

public sealed class SceneManager(IRectWindow window) : IDisposable
{
    private readonly List<Scene> scenes = new();
    private ContentWorkspace? env;

    public IRectWindow Window { get; } = window;

    public Scene CreateScene(Scene? parentScene = null)
    {
        if (env == null) throw new Exception("Workspace unset");

        var scene = new Scene(env, parentScene);
        scenes.Add(scene);
        return scene;
    }

    public void RemoveScene(Scene scene)
    {
        scenes.Remove(scene);
    }

    internal void ChangeWorkspace(ContentWorkspace workspace)
    {
        if (env == workspace) return;

        ClearScenes();
        env = workspace;
    }

    public void Update(float deltaTime)
    {
        foreach (var scene in scenes) {
            scene.ExecuteDeferredActions();
        }

        // TODO invoke all object updates once we have some behaviors
    }

    public void Render(float deltaTime)
    {
        // should we sort scenes with render buffers first and then the main viewport scenes to minimize viewport switching?
        foreach (var scene in scenes) {
            scene.RenderContext.ViewportSize = Window.Size;
            scene.Render(deltaTime);
            // if (scene.RenderTargetTextureHandle != 0) {
            //     scene.OpenGL.Viewport(new System.Drawing.Size((int)Window.Size.X, (int)Window.Size.Y));
            // }
        }
    }

    private void ClearScenes()
    {
        foreach (var scene in scenes) {
            scene.Dispose();
        }
        scenes.Clear();
    }

    public void Dispose()
    {
        ClearScenes();
    }
}