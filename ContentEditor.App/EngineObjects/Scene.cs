using System.Collections.Concurrent;
using ReeLib;

namespace ContentEditor.App;

public class NodeTreeContainer
{
    private readonly ConcurrentQueue<Action> deferred = new();

    public void DeferAction(Action action)
    {
        deferred.Enqueue(action);
    }

    internal void ExecuteDeferredActions()
    {
        while (deferred.TryDequeue(out var act)) {
            try {
                act.Invoke();
            } catch (Exception e) {
                Logger.Error(e, "Deferred action failed");
            }
        }
    }
}

public sealed class Scene : NodeTreeContainer, IDisposable
{
    public readonly Folder RootFolder;
    public IEnumerable<Folder> Folders => RootFolder.Children;
    public IEnumerable<GameObject> GameObjects => RootFolder.GameObjects;

    public Scene(Workspace env)
    {
        RootFolder = new("ROOT", env, this);
    }

    public GameObject? Find(ReadOnlySpan<char> path) => RootFolder.Find(path);

    public GameObject? Find(Guid guid)
    {
        foreach (var go in GetAllGameObjects()) {
            if (go.guid == guid) {
                return go;
            }
        }
        return null;
    }

    public void Add(GameObject obj)
    {
        RootFolder.GameObjects.Add(obj);
        obj.MoveToScene(this);
    }

    public void Add(Folder folder)
    {
        RootFolder.Children.Add(folder);
        folder.MoveToScene(this);
    }

    public IEnumerable<GameObject> GetAllGameObjects() => RootFolder.GetAllGameObjects();

    public void Dispose()
    {
        RootFolder.Dispose();
    }
}
