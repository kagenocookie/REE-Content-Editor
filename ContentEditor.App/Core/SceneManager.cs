using ContentPatcher;

namespace ContentEditor.App;

public sealed class SceneManager : IDisposable
{
    private readonly List<Scene> scenes = new();
    private ContentWorkspace? env;

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