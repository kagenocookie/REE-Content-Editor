using System.Diagnostics;

namespace ContentEditor.App;

public class SceneManager
{
    private readonly List<Scene> scenes = new();

    public Scene CreateScene()
    {
        var scene = new Scene();
        scenes.Add(scene);
        return scene;
    }

    public void RemoveScene(Scene scene)
    {
        scenes.Remove(scene);
    }

    public void Update(float deltaTime)
    {
        foreach (var scene in scenes) {
            scene.ExecuteDeferredActions();
        }

        // TODO invoke all object updates once we have some behaviors
    }
}