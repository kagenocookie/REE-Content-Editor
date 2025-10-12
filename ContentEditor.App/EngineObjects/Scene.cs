using System.Collections.ObjectModel;
using ContentEditor.App.FileLoaders;
using ContentEditor.App.Graphics;
using ContentEditor.App.Windowing;
using ContentPatcher;
using ReeLib;
using Silk.NET.Maths;
using Silk.NET.OpenGL;

namespace ContentEditor.App;

public sealed class Scene : NodeTreeContainer, IDisposable, IAsyncResourceReceiver
{
    public readonly Folder RootFolder;
    public IEnumerable<Folder> Folders => RootFolder.Children;
    public IEnumerable<GameObject> GameObjects => RootFolder.GameObjects;

    public Scene? ParentScene { get; private set; }
    public Scene RootScene => ParentScene?.RootScene ?? this;
    private readonly List<Scene> childScenes = new();
    internal ReadOnlyCollection<Scene> ChildScenes => childScenes.AsReadOnly();

    public string Name { get; }
    public string InternalPath { get; }
    public ContentWorkspace Workspace { get; }
    public bool IsActive { get; set; }
    public SceneManager SceneManager { get; internal set; } = null!;

    private List<RenderableComponent> renderComponents = new();
    private List<IUpdateable> updateComponents = new();

    private GL _gl;

    private RenderContext renderContext;
    public RenderContext RenderContext => RootScene.renderContext;
    public RenderContext OwnRenderContext => renderContext;

    private Camera camera;
    public Camera Camera => camera;
    public Camera ActiveCamera => RootScene.Camera;

    private bool wasActivatedBefore;
    private HashSet<string> _requestedScenes = new(0);

    private bool HasRenderables => renderComponents.Count > 0 || childScenes.Any(ch => ch.HasRenderables);

    internal Scene(string name, string internalPath, ContentWorkspace workspace, Scene? parentScene = null, Folder? rootFolder = null, GL? gl = null)
    {
        Name = name;
        InternalPath = internalPath;
        Workspace = workspace;
        ParentScene = parentScene;
        _gl = gl ?? parentScene?._gl ?? EditorWindow.CurrentWindow?.GLContext ?? throw new Exception("Could not get OpenGL Context!");
        renderContext = new OpenGLRenderContext(_gl);
        RootFolder = rootFolder ?? new("ROOT", workspace.Env, this);
        var camGo = new GameObject("__editorCamera", workspace.Env);
        camera = Component.Create<Camera>(camGo, workspace.Env);
        parentScene?.childScenes.Add(this);
    }

    public GameObject? Find(ReadOnlySpan<char> path) => RootFolder.Find(path);

    public Scene? GetChildScene(string? nameOrPath)
    {
        foreach (var child in childScenes) {
            if (child.Name.Equals(nameOrPath, StringComparison.InvariantCultureIgnoreCase) ||
                child.InternalPath.Equals(nameOrPath, StringComparison.InvariantCultureIgnoreCase) ||
                child.RootFolder.Name.Equals(nameOrPath, StringComparison.InvariantCultureIgnoreCase)) {
                return child;
            }
        }

        return null;
    }

    public void RequestLoadChildScene(Folder childFolder)
    {
        if (childFolder.Scene != this || string.IsNullOrEmpty(childFolder.ScenePath)) return;
        if (GetChildScene(childFolder.ScenePath) != null) return;
        if (!_requestedScenes.Add(childFolder.ScenePath)) return;

        Workspace.ResourceManager.TryResolveFileInBackground(childFolder.ScenePath, this, (envScene) => {
            var scene = envScene.GetCustomContent<RawScene>();
            if (scene == null) return;

            var childSceneRoot = scene.GetSharedInstance(Workspace.Env);
            var childScene = childSceneRoot.Scene;
            if (childScene == null) {
                childFolder.ChildScene = SceneManager.CreateScene(envScene, IsActive, this, childSceneRoot);
            }
        });
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

    public Folder? FindFolder(string name)
    {
        foreach (var folder in Folders) {
            if (folder.Name.Equals(name, StringComparison.InvariantCultureIgnoreCase)) {
                return folder;
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

    internal void Update(float deltaTime)
    {
        foreach (var comp in updateComponents) {
            comp.Update(deltaTime);
        }
    }

    internal void Render(float deltaTime)
    {
        if (!HasRenderables) return;

        var rctx = RenderContext;
        if (rctx != this.renderContext) {
            foreach (var render in renderComponents) {
                if (!render.GameObject.ShouldDraw) continue;

                render.Render(rctx);
            }
            return;
        }

        rctx.DeltaTime = deltaTime;
        if (Matrix4X4.Invert(ActiveCamera.GameObject.WorldTransform, out var inverted)) {
            rctx.ViewMatrix = inverted;
        } else {
            rctx.ViewMatrix = Matrix4X4<float>.Identity;
        }
        rctx.BeforeRender();
        foreach (var render in renderComponents) {
            if (!render.GameObject.ShouldDraw) continue;

            render.Render(rctx);
        }
        rctx.AfterRender();
    }

    public void SetActive(bool active)
    {
        if (active == IsActive) return;

        IsActive = active;
        RootFolder.SetActive(active);
        if (!wasActivatedBefore && active && RootScene == this) {
            wasActivatedBefore = true;
            var lookTarget = renderComponents.FirstOrDefault()?.GameObject ?? GetAllGameObjects().FirstOrDefault();
            if (lookTarget != null) {
                ActiveCamera.LookAt(lookTarget, true);
            }
        }

        foreach (var child in childScenes) {
            child.SetActive(active);
        }
    }

    public void Dispose()
    {
        SetActive(false);
        renderContext.Dispose();
        RootFolder.Dispose();
        while (childScenes.Count != 0) {
            childScenes.Last().Dispose();
        }
        ParentScene?.childScenes.Remove(this);
    }

    internal void AddUpdateComponent<TComponent>(TComponent component) where TComponent : Component, IUpdateable
    {
        updateComponents.Add(component);
    }

    internal void RemoveUpdateComponent<TComponent>(TComponent component) where TComponent : Component, IUpdateable
    {
        updateComponents.Remove(component);
    }

    internal void AddRenderComponent(RenderableComponent renderComponent)
    {
        renderComponents.Add(renderComponent);
    }

    internal void RemoveRenderComponent(RenderableComponent renderComponent)
    {
        renderComponents.Remove(renderComponent);
    }

    public override string ToString() => Name;

    public void ReceiveResource(FileHandle file, Action<FileHandle> callback)
    {
        DeferAction(() => callback.Invoke(file));
    }
}
