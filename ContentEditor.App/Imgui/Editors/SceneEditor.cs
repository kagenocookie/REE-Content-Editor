using ContentEditor.App.FileLoaders;
using ContentEditor.App.Windowing;
using ContentPatcher;
using ReeLib;

namespace ContentEditor.App.ImguiHandling;

public class SceneEditor : FileEditor, IWorkspaceContainer, IRSZFileEditor, IObjectUIHandler, IInspectorController, IWindowHandler, IFilterRoot, ISceneEditor
{
    public override string HandlerName => "Scene";

    public string Filename => Handle.Filepath;
    public ScnFile File => Handle.GetFile<ScnFile>();
    public RawScene SourceScene => Handle.GetCustomContent<RawScene>()!;

    public ContentWorkspace Workspace { get; }
    public SceneEditor? ParentEditor { get; }
    public InspectorContainer Inspector { get; private set; } = null!;

    public SceneTreeEditor? Tree => context.GetChildHandler<SceneTreeEditor>();

    private Scene? scene;
    protected override bool IsRevertable => context.Changed;

    private readonly RszSearchHelper searcher = new();
    bool IFilterRoot.HasFilterActive => searcher.HasFilterActive;
    public object? MatchedObject { get => searcher.MatchedObject; set => searcher.MatchedObject = value; }

    public SceneEditor RootSceneEditor => ParentEditor?.RootSceneEditor ?? this;

    public SceneEditor(ContentWorkspace env, FileHandle file, SceneEditor? parent = null) : base(file)
    {
        Workspace = env;
        ParentEditor = parent;
    }

    public override void Init(UIContext context)
    {
        base.Init(context);
        Inspector = new InspectorContainer(this, context);
    }

    public RSZFile GetRSZFile() => File.RSZ;
    public Scene? GetScene() => scene;

    protected override void Reset()
    {
        Inspector.CloseAll();
        context.ClearChildren();
        base.Reset();
        scene?.Dispose();
        scene = null;
    }

    protected override void OnFileChanged()
    {
        var depth = Tree?.InheritedDepth ?? 0;
        base.OnFileChanged();
        if (scene != null) {
            var window = context.GetNativeWindow()!;
            if (!MainLoop.IsMainThread) {
                window.InvokeFromUIThread(() => {
                    window.SceneManager.UnloadScene(scene);
                    Reset();
                    scene = LoadScene();
                    EnsureUIInit();
                    Tree?.InheritedDepth = depth;
                });
            } else {
                window.SceneManager.UnloadScene(scene);
                Reset();
                scene = LoadScene();
                EnsureUIInit();
                Tree?.InheritedDepth = depth;
            }
        }
    }

    private Scene? LoadScene()
    {
        context.ClearChildren();
        var window = context.GetNativeWindow();
        scene = Handle.InternalPath == null ? null : window?.SceneManager.FindLoadedScene(Handle.InternalPath);
        if (scene != null) {
            SourceScene.SetSharedInstance(scene.RootFolder);
            return scene;
        }

        var root = SourceScene.GetSharedInstance(Workspace.Env);
        if (Logger.ErrorIf(root == null, "Failed to instantiate scene")) return null;
        scene = root.Scene;
        if (scene == null) {
            scene = window?.SceneManager.CreateScene(Handle, ParentEditor?.scene?.IsActive ?? false, ParentEditor?.scene, root);
            if (Logger.ErrorIf(scene == null, "Failed to create new scene")) return null;
            if (window?.SceneManager.HasActiveMasterScene == false) {
                window.SceneManager.ChangeMasterScene(scene);
            }
        }
        return scene;
    }

    internal void EnsureUIInit()
    {
        if (scene == null) {
            scene = LoadScene();
            if (scene == null) return;
        }
        if (context.children.Count == 0) {
            var root = scene.RootFolder;
            // context.AddChild<ScnFile, List<ResourceInfo>>("Resources", File, getter: static (c) => c!.ResourceInfoList, handler: new TooltipUIHandler(new ListHandler(typeof(ResourceInfo)), "List of resources that will be preloaded together with the file ingame.\nShould be updated automatically on save."));
            context.AddChild(root.Name, root, new SceneTreeEditor(ParentEditor?.Tree));
        }
    }

    protected override void DrawFileContents()
    {
        ImGui.PushID(Filename);
        EnsureUIInit();

        searcher.ShowFileEditorInline((Tree?.InheritedDepth ?? 0) * 20);
        ImGui.Spacing();
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
