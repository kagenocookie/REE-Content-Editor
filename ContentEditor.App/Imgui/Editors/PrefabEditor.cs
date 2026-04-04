using ContentEditor.App.FileLoaders;
using ContentEditor.App.Windowing;
using ContentPatcher;
using ReeLib;

namespace ContentEditor.App.ImguiHandling;

public interface ISceneEditor
{
    public Scene? GetScene();
    public Scene? GetRootScene(UIContext context)
    {
        var parentSceneHolderCtx = context.FindParentContextByHandler<ISceneEditor>(true);
        return (parentSceneHolderCtx?.uiHandler as ISceneEditor)?.GetRootScene(parentSceneHolderCtx!) ?? GetScene();
    }
}

public class PrefabEditor : FileEditor, IWorkspaceContainer, IRSZFileEditor, IObjectUIHandler, IInspectorController, IWindowHandler, IFilterRoot, ISceneEditor
{
    public override string HandlerName => "Prefab";

    public string Filename => Handle.Filepath;
    public PfbFile File => Handle.GetFile<PfbFile>();
    public Prefab Prefab => Handle.GetCustomContent<Prefab>()!;

    public ContentWorkspace Workspace { get; }

    public InspectorContainer Inspector { get; private set; } = null!;

    public SceneTreeEditor? Tree => context.GetChildHandler<SceneTreeEditor>();

    private Scene? scene;
    protected override bool IsRevertable => context.Changed;

    private readonly RszSearchHelper searcher = new();
    bool IFilterRoot.HasFilterActive => searcher.HasFilterActive;
    public object? MatchedObject { get => searcher.MatchedObject; set => searcher.MatchedObject = value; }

    public PrefabEditor(ContentWorkspace env, FileHandle file) : base(file)
    {
        Workspace = env;
    }

    public RSZFile GetRSZFile() => File.RSZ;
    public Scene? GetScene() => scene;

    public override void Init(UIContext context)
    {
        base.Init(context);
        Inspector = new InspectorContainer(this, context);
    }

    protected override void OnFileReverted()
    {
        Reset();
    }

    protected override void OnFileChanged()
    {
        base.OnFileChanged();
        if (scene != null) {
            var window = context.GetNativeWindow()!;
            if (!MainLoop.IsMainThread) {
                window.InvokeFromUIThread(() => {
                    window!.SceneManager.UnloadScene(scene);
                    Reset();
                    scene = LoadScene();
                });
            } else {
                window!.SceneManager.UnloadScene(scene);
                Reset();
                scene = LoadScene();
            }
        }
    }

    protected override void Reset()
    {
        Inspector.CloseAll();
        context.ClearChildren();
        base.Reset();
        scene?.Dispose();
        scene = null;
    }

    private Scene? LoadScene()
    {
        context.ClearChildren();
        var window = context.GetNativeWindow();
        scene = Handle.InternalPath == null ? null : window?.SceneManager.FindLoadedScene(Handle.InternalPath);
        if (scene != null) return scene;

        var root = Prefab.GetSharedInstance();
        if (Logger.ErrorIf(root == null, "Failed to instantiate prefab")) return null;
        scene = root.Scene;
        if (scene == null) {
            scene = window?.SceneManager.CreateScene(Handle, false, ((ISceneEditor)this).GetRootScene(context));
            if (Logger.ErrorIf(scene == null, "Failed to create new scene")) return null;
            scene.Add(root);
            if (window?.SceneManager.HasActiveMasterScene == false) {
                window.SceneManager.ChangeMasterScene(scene);
            }
        }
        return scene;
    }

    protected override void DrawFileContents()
    {
        if (scene == null) {
            scene = LoadScene();
            if (scene == null) return;
        }

        ImGui.PushID(Filename);
        if (context.children.Count == 0) {
            var root = scene.RootFolder.GameObjects.First();
            // context.AddChild<PfbFile, List<ResourceInfo>>("Resources", File, getter: static (c) => c!.ResourceInfoList, handler: new TooltipUIHandler(new ListHandler(typeof(ResourceInfo)), "List of resources that will be preloaded together with the file ingame.\nShould be updated automatically on save."));
            context.AddChild(root.Name, root, new GameObjectTreeEditor());
        }
        searcher.ShowFileEditorInline();
        ImGui.Spacing();
        ImGui.Separator();
        context.ShowChildrenUI();
        if (WindowData.IsFocused && Inspector.PrimaryTarget is IVisibilityTarget vis && AppConfig.Instance.Key_Scene_FocusUI.Get().IsPressed()) {
            Tree?.ScrollTo(vis);
        }
        ImGui.PopID();
    }

    void IObjectUIHandler.OnIMGUI(UIContext container)
    {
        this.OnIMGUI();
    }

    void IWindowHandler.OnClosed()
    {
        Inspector.CloseAll();
    }

    bool IFilterRoot.IsMatch(object? obj) => searcher.IsMatch(obj);
}
