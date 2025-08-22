using System.Collections.Concurrent;
using ContentEditor.App.Windowing;
using ContentEditor.Editor;
using ContentPatcher;
using ReeLib;
using Silk.NET.OpenGL;

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

    public Scene? ParentScene { get; private set; }
    public Scene RootScene => ParentScene?.RootScene ?? this;

    public ContentWorkspace Workspace { get; }
    public bool Renderable { get; set; } = true;

    bool IFileHandleReferenceHolder.CanClose => true;
    IRectWindow? IFileHandleReferenceHolder.Parent => null;

    private List<RenderableComponent> renderComponents = new();

    private GL _gl;

    private Dictionary<IResourceFile, (FileHandle handle, int refcount)> _loadedResources = new();

    public Scene(ContentWorkspace workspace, Scene? parentScene = null, GL? gl = null)
    {
        Workspace = workspace;
        ParentScene = parentScene;
        _gl = gl ?? parentScene?._gl ?? EditorWindow.CurrentWindow?.GLContext ?? throw new Exception("Could not get OpenGL Context!");
        RootFolder = new("ROOT", workspace.Env, this);
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

    public GameObject? FindGameObjectByInstance(RszInstance instance)
    {
        foreach (var obj in GetAllGameObjects()) {
            if (obj.Instance == instance) {
                return obj;
            }
        }

        return null;
    }

    public GameObject? FindGameObjectByGuid(Guid guid)
    {
        foreach (var obj in GetAllGameObjects()) {
            if (obj.guid == guid) {
                return obj;
            }
        }

        return null;
    }

    public void Dispose()
    {
        RootFolder.Dispose();
    }
}
