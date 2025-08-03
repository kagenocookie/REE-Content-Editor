using System.Collections.Concurrent;

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
    private readonly Folder RootFolder = new("ROOT");
    public IEnumerable<Folder> Folders => RootFolder.Children;
    public IEnumerable<GameObject> GameObjects => RootFolder.GameObjects;

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
    }

    public IEnumerable<GameObject> GetAllGameObjects() => RootFolder.GetAllGameObjects();

    public void Dispose()
    {
        RootFolder.Dispose();
    }
}
