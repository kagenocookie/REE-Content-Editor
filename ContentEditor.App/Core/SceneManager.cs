using System.Diagnostics;
using ReeLib;

namespace ContentEditor.App;

public sealed class SceneManager : IDisposable
{
    private readonly List<Scene> scenes = new();
    private Workspace? env;

    public Scene CreateScene()
    {
        if (env == null) throw new Exception("Workspace unset");

        var scene = new Scene(env);
        scenes.Add(scene);
        return scene;
    }

    public void RemoveScene(Scene scene)
    {
        scenes.Remove(scene);
    }

    internal void ChangeWorkspace(Workspace workspace)
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