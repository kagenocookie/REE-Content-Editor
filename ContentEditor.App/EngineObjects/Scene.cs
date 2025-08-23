using System.Collections.Concurrent;
using System.Numerics;
using ContentEditor.App.Graphics;
using ContentEditor.App.Windowing;
using ContentEditor.Editor;
using ContentPatcher;
using ReeLib;
using Silk.NET.OpenGL;

namespace ContentEditor.App;

public sealed class Scene : NodeTreeContainer, IDisposable, IFileHandleReferenceHolder
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

    private RenderContext renderContext;
    public RenderContext RenderContext => renderContext;

    private Camera camera;
    public Camera Camera => camera;

    public Scene(ContentWorkspace workspace, Scene? parentScene = null, GL? gl = null)
    {
        Workspace = workspace;
        ParentScene = parentScene;
        _gl = gl ?? parentScene?._gl ?? EditorWindow.CurrentWindow?.GLContext ?? throw new Exception("Could not get OpenGL Context!");
        renderContext = new OpenGLRenderContext(_gl);
        RootFolder = new("ROOT", workspace.Env, this);
        var camGo = new GameObject("__editorCamera", workspace.Env);
        camera = Component.Create<Camera>(camGo, workspace.Env);
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

    public TResourceType? LoadResource<TResourceType>(string path) where TResourceType : class, IResourceFile
    {
        if (!Renderable) return default;

        if (!Workspace.ResourceManager.TryGetOrLoadFile(path, out var fileHandle)) {
            Logger.Error("Failed to load resource " + path);
            return null;
        }

        var resource = fileHandle.GetResource<TResourceType>();
        if (_loadedResources.TryGetValue(resource, out var handleRefs)) {
            _loadedResources[resource] = (handleRefs.handle, handleRefs.refcount - 1);
            fileHandle = handleRefs.handle;
        } else {
            _loadedResources[resource] = (handleRefs.handle, 1);
            fileHandle.References.Add(this);
        }

        return resource;
    }

    public void AddResourceReference(IResourceFile resource)
    {
        if (_loadedResources.TryGetValue(resource, out var handleRefs)) {
            _loadedResources[resource] = (handleRefs.handle, handleRefs.refcount - 1);
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
        renderContext.CameraMatrix = camera.GameObject.WorldTransform;
        renderContext.BeforeRender();
        foreach (var render in renderComponents) {
            render.Render(renderContext);
        }
        renderContext.AfterRender();
    }

    public void Dispose()
    {
        renderContext.Dispose();
        RootFolder.Dispose();
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
}
