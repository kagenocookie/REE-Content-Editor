using ContentEditor.App.Graphics;
using ContentEditor.App.Windowing;
using ContentEditor.Editor;
using ContentPatcher;
using ReeLib;
using Silk.NET.Maths;
using Silk.NET.OpenGL;

namespace ContentEditor.App;

public sealed class Scene : NodeTreeContainer, IDisposable, IFileHandleReferenceHolder
{
    public readonly Folder RootFolder;
    public IEnumerable<Folder> Folders => RootFolder.Children;
    public IEnumerable<GameObject> GameObjects => RootFolder.GameObjects;

    public Scene? ParentScene { get; private set; }
    public Scene RootScene => ParentScene?.RootScene ?? this;
    private readonly List<Scene> ChildScenes = new();

    public string Name { get; }
    public ContentWorkspace Workspace { get; }
    public bool IsActive { get; set; }

    bool IFileHandleReferenceHolder.CanClose => true;
    IRectWindow? IFileHandleReferenceHolder.Parent => null;

    private List<RenderableComponent> renderComponents = new();

    private GL _gl;

    private Dictionary<IResourceFile, (FileHandle handle, int refcount)> _loadedResources = new();

    private RenderContext renderContext;
    public RenderContext RenderContext => renderContext;

    private Camera camera;
    public Camera Camera => camera;
    public Camera ActiveCamera => RootScene.Camera;

    private bool wasActivatedBefore;

    public Scene(string name, ContentWorkspace workspace, Scene? parentScene = null, GL? gl = null)
    {
        Name = name;
        Workspace = workspace;
        ParentScene = parentScene;
        _gl = gl ?? parentScene?._gl ?? EditorWindow.CurrentWindow?.GLContext ?? throw new Exception("Could not get OpenGL Context!");
        renderContext = new OpenGLRenderContext(_gl);
        RootFolder = new("ROOT", workspace.Env, this);
        var camGo = new GameObject("__editorCamera", workspace.Env);
        camera = Component.Create<Camera>(camGo, workspace.Env);
        parentScene?.ChildScenes.Add(this);
    }

    public GameObject? Find(ReadOnlySpan<char> path) => RootFolder.Find(path);

    public Scene? GetChildScene(string sceneName)
    {
        foreach (var child in ChildScenes) {
            if (child.Name.Equals(sceneName, StringComparison.InvariantCultureIgnoreCase) || PathUtils.GetInternalFromNativePath(child.Name).Equals(sceneName, StringComparison.InvariantCultureIgnoreCase)) {
                return child;
            }
        }

        return null;
    }

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

    public TResourceType? LoadResource<TResourceType>(string path) where TResourceType : class, IResourceFile
    {
        if (!Workspace.ResourceManager.TryResolveFile(path, out var fileHandle)) {
            Logger.Error("Failed to load resource " + path);
            return null;
        }

        var resource = fileHandle.GetResource<TResourceType>();
        if (_loadedResources.TryGetValue(resource, out var handleRefs)) {
            _loadedResources[resource] = (handleRefs.handle, handleRefs.refcount + 1);
        } else {
            _loadedResources[resource] = (fileHandle, 1);
            fileHandle.References.Add(this);
        }

        return resource;
    }

    public void AddResourceReference(FileHandle file)
    {
        if (_loadedResources.TryGetValue(file.Resource, out var handleRefs)) {
            _loadedResources[file.Resource] = (file, handleRefs.refcount + 1);
        } else {
            _loadedResources[file.Resource] = (file, 1);
            file.References.Add(this);
        }
    }

    public void AddResourceReference(IResourceFile resource)
    {
        if (_loadedResources.TryGetValue(resource, out var handleRefs)) {
            _loadedResources[resource] = (handleRefs.handle, handleRefs.refcount + 1);
        } else {
            throw new Exception("Attempted to add scene reference to unknown resource");
        }
    }

    internal void UnloadResource(IResourceFile resource)
    {
        if (_loadedResources.Remove(resource, out var handleRefs)) {
            if (handleRefs.refcount == 1) {
                handleRefs.handle.References.Remove(this);
            } else {
                _loadedResources[resource] = (handleRefs.handle, handleRefs.refcount - 1);
            }
        }
    }

    internal void Render(float deltaTime)
    {
        if (renderComponents.Count == 0) return;

        renderContext.DeltaTime = deltaTime;
        if (Matrix4X4.Invert(ActiveCamera.GameObject.WorldTransform, out var inverted)) {
            renderContext.ViewMatrix = inverted;
        } else {
            renderContext.ViewMatrix = Matrix4X4<float>.Identity;
        }
        renderContext.BeforeRender();
        foreach (var render in renderComponents) {
            if (!render.GameObject.ShouldDraw) continue;

            render.Render(renderContext);
        }
        renderContext.AfterRender();
    }

    public void SetActive(bool active)
    {
        if (active == IsActive) return;

        IsActive = active;
        RootFolder.SetActive(active);
        if (!wasActivatedBefore && active) {
            wasActivatedBefore = true;
            var lookTarget = renderComponents.FirstOrDefault()?.GameObject ?? GetAllGameObjects().FirstOrDefault();
            if (lookTarget != null) {
                ActiveCamera.LookAt(lookTarget, true);
            }
        }

        foreach (var child in ChildScenes) {
            child.SetActive(active);
        }
    }

    public void Dispose()
    {
        SetActive(false);
        renderContext.Dispose();
        RootFolder.Dispose();
        while (ChildScenes.Count != 0) {
            ChildScenes.Last().Dispose();
        }
        ParentScene?.ChildScenes.Remove(this);
    }

    internal void AddRenderComponent(RenderableComponent renderComponent)
    {
        renderComponents.Add(renderComponent);
    }

    internal void RemoveRenderComponent(RenderableComponent renderComponent)
    {
        renderComponents.Remove(renderComponent);
    }

    void IFileHandleReferenceHolder.Close()
    {
        // foreach (var render in renderComponents) {
        //     render.OnExitScene(RootScene);
        // }
    }

    internal void StoreResource(FileHandle fileHandle)
    {
        _loadedResources.TryAdd(fileHandle.Resource, (fileHandle, 0));
    }

    public override string ToString() => Name;
}
