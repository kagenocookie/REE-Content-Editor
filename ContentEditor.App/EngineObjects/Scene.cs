using System.Collections.ObjectModel;
using System.Numerics;
using ContentEditor.App.FileLoaders;
using ContentEditor.App.Graphics;
using ContentEditor.App.ImguiHandling;
using ContentEditor.App.Windowing;
using ContentEditor.Editor;
using ContentPatcher;
using ReeLib;
using Silk.NET.OpenGL;

namespace ContentEditor.App;

public enum SceneType
{
    Main,
    Sub,
    Independent,
}

public sealed class Scene : NodeTreeContainer, IDisposable, IAsyncResourceReceiver, IWorkspaceContainer, IRectWindow
{
    public readonly Folder RootFolder;
    public IEnumerable<Folder> Folders => RootFolder.Children;
    public IEnumerable<Folder> AllFolders => RootFolder.GetAllChildren();
    public IEnumerable<GameObject> GameObjects => RootFolder.GameObjects;

    public SceneType Type { get; set; }
    public SceneRoot Root { get; private set; }
    public Scene? ParentScene { get; private set; }
    public Scene RootScene => Root.Scene;

    private readonly List<Scene> childScenes = new();
    internal ReadOnlyCollection<Scene> ChildScenes => childScenes.AsReadOnly();
    public IEnumerable<Scene> NestedScenes {
        get {
            yield return this;
            foreach (var child in childScenes.SelectMany(cs => cs.NestedScenes)) {
                yield return child;
            }
        }
    }

    public string Name { get; }
    public string InternalPath { get; }
    public ContentWorkspace Workspace { get; }
    public bool IsActive { get; set; }
    public SceneManager SceneManager { get; internal set; } = null!;

    public SceneMouseHandler Mouse => Root.MouseHandler;
    public SceneController Controller => Root.Controller;

    public readonly SceneComponentsList<RenderableComponent> Renderable = new();
    public readonly SceneComponentsList<IUpdateable> Updateable = new();

    public bool IsRoot => ParentScene == null;

    private GL _gl;

    private UIContext? ui;

    private RenderContext renderContext;
    public RenderContext RenderContext => RootScene.renderContext;
    public RenderContext OwnRenderContext => renderContext;

    public Camera ActiveCamera => Root.Camera;

    private bool wasActivatedBefore;
    private HashSet<string> _requestedScenes = new(0);

    public bool HasRenderables => !Renderable.IsEmpty || childScenes.Any(ch => ch.HasRenderables);

    Vector2 IRectWindow.Size => renderContext.ViewportSize;
    Vector2 IRectWindow.Position => renderContext.ViewportOffset + renderContext.UIOffset;

    [Flags]
    public enum LoadType
    {
        Default = 0,
        LoadChildren = 1,
        IncludeNested = 2,
        PreloadedOnly = 4,
    }

    internal Scene(string name, string internalPath, ContentWorkspace workspace, Scene? parentScene = null, Folder? rootFolder = null, GL? gl = null)
    {
        Name = name;
        InternalPath = internalPath;
        Workspace = workspace;
        ParentScene = parentScene;
        _gl = gl ?? parentScene?._gl ?? EditorWindow.CurrentWindow?.GLContext ?? throw new Exception("Could not get OpenGL Context!");
        renderContext = new OpenGLRenderContext(_gl);
        RootFolder = rootFolder ?? new("ROOT", workspace.Env, this);
        if (parentScene == null) {
            Root = new SceneRoot(this);
            Type = SceneType.Main;
            renderContext.ClearColor = AppConfig.Instance.BackgroundColor.Get();
        } else {
            Root = parentScene.Root;
            Type = SceneType.Sub;
        }

        parentScene?.childScenes.Add(this);
        renderContext.ResourceManager = workspace.ResourceManager;
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

    public void RequestLoadChildScene(Folder childFolder, LoadType subfolderLoadType = LoadType.Default)
    {
        if (childFolder.Scene != this || string.IsNullOrEmpty(childFolder.ScenePath)) return;
        if (GetChildScene(childFolder.ScenePath) != null) return;
        if (!_requestedScenes.Add(childFolder.ScenePath)) return;

        Workspace.ResourceManager.TryResolveFileInBackground(childFolder.ScenePath, this, (envScene) => {
            var scene = envScene.GetCustomContent<RawScene>();
            if (scene == null) return;

            var childSceneRoot = scene.GetSharedInstance(Workspace.Env);
            childFolder.ChildScene = childSceneRoot.Scene ?? SceneManager.CreateScene(envScene, IsActive, this, childSceneRoot);

            if ((subfolderLoadType & LoadType.LoadChildren) != 0 && childFolder.ChildScene != null)
            {
                var childrenLoadType = subfolderLoadType;
                if ((childrenLoadType & LoadType.IncludeNested) == 0) childrenLoadType = childrenLoadType & ~LoadType.LoadChildren;

                foreach (var childSubfolder in childFolder.ChildScene.AllFolders) {
                    if ((subfolderLoadType & LoadType.PreloadedOnly) != 0 && !childSubfolder.Standby) {
                        continue;
                    }

                    childSubfolder.RequestLoad(childrenLoadType);
                }
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
        foreach (var comp in Updateable.components) {
            comp.Update(deltaTime);
        }

        if (ParentScene == null) {
            Root.Update(deltaTime);
        }
    }

    internal void Render(float deltaTime)
    {
        var rctx = RenderContext;
        var cam = ActiveCamera;
        if (!HasRenderables) {
            if (rctx == this.renderContext) {
                // if root, at least render the empty scene
                cam.Update(rctx.ViewportSize);
                rctx.ViewMatrix = cam.ViewMatrix;
                rctx.ProjectionMatrix = cam.ProjectionMatrix;
                rctx.ViewProjectionMatrix = cam.ViewProjectionMatrix;

                rctx.DeltaTime = deltaTime;
                rctx.BeforeRender();
                Root.Render();
                rctx.ExecuteRender();
                rctx.AfterRender();
            }
            return;
        }

        if (rctx != this.renderContext) {
            foreach (var render in Renderable.components) {
                if (!render.GameObject.ShouldDraw) continue;
                if (!cam.IsVisible(render)) continue;

                render.Render(rctx);
            }
            foreach (var child in childScenes) {
                child.Render(deltaTime);
            }
            return;
        }

        cam.Update(rctx.ViewportSize);
        rctx.ViewMatrix = cam.ViewMatrix;
        rctx.ProjectionMatrix = cam.ProjectionMatrix;
        rctx.ViewProjectionMatrix = cam.ViewProjectionMatrix;

        rctx.DeltaTime = deltaTime;
        rctx.BeforeRender();
        foreach (var render in Renderable.components) {
            if (!render.GameObject.ShouldDraw) continue;
            if (!cam.IsVisible(render)) continue;

            render.Render(rctx);
        }
        foreach (var child in childScenes) {
            child.Render(deltaTime);
        }
        Root.Render();
        rctx.ExecuteRender();
        rctx.AfterRender();
    }

    public void RenderUI()
    {
        Root.RenderUI();
        if (ui != null) {
            var cursorStart = ((IRectWindow)this).Position;
            for (int i = 0; i < ui.children.Count; i++) {
                ImGui.SetCursorScreenPos(cursorStart);
                ui.children[i].ShowUI();
            }
        }
    }

    public void AddWidget<THandler>() where THandler : ISceneWidget, new()
    {
        ui ??= UIContext.CreateRootContext(Name, this);
        var child = ui.GetChildHandler<THandler>();
        if (child == null) {
            ui.AddChild(THandler.WidgetName, this, new THandler());
        }
    }

    public void SetActive(bool active)
    {
        if (active == IsActive) return;

        IsActive = active;
        RootFolder.SetActive(active);
        if (!wasActivatedBefore && active && RootScene == this) {
            wasActivatedBefore = true;
            var lookTarget = Renderable.components.FirstOrDefault()?.GameObject ?? GetAllGameObjects().FirstOrDefault();
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
        if (ParentScene == null) Root.Dispose();
        renderContext.Dispose();
        RootFolder.Dispose();
        while (childScenes.Count != 0) {
            childScenes.Last().Dispose();
        }
        ParentScene?.childScenes.Remove(this);
    }

    public override string ToString() => Name;

    public void ReceiveResource(FileHandle file, Action<FileHandle> callback)
    {
        DeferAction(() => callback.Invoke(file));
    }
}
