using ContentEditor.App.Windowing;
using ContentPatcher;
using ImGuiNET;
using ReeLib;
using ReeLib.Pfb;

namespace ContentEditor.App.ImguiHandling;

public interface ISceneEditor
{
    public Scene? GetScene();
    public Scene? GetRootScene(UIContext context)
    {
        var parentSceneHolderCtx = context.FindParentContextByHandler<ISceneEditor>();
        return (parentSceneHolderCtx?.uiHandler as ISceneEditor)?.GetRootScene(parentSceneHolderCtx!) ?? GetScene();
    }
}

public class PrefabEditor : FileEditor, IWorkspaceContainer, IRSZFileEditor, IObjectUIHandler, IInspectorController, IWindowHandler, IFilterRoot, ISceneEditor
{
    public override string HandlerName => "Prefab";

    public string Filename => Handle.Filepath;
    public PfbFile File => Handle.GetFile<PfbFile>();
    public Prefab Prefab => Handle.GetCustomContent<Prefab>();

    public ContentWorkspace Workspace { get; }

    private ObjectInspector? primaryInspector;
    private Scene? scene;
    protected override bool IsRevertable => context.Changed;

    public object? PrimaryTarget => primaryInspector?.Target;

    private readonly RszSearchHelper searcher = new();
    bool IFilterRoot.HasFilterActive => searcher.HasFilterActive;
    public object? MatchedObject { get => searcher.MatchedObject; set => searcher.MatchedObject = value; }

    private readonly List<ObjectInspector> inspectors = new();

    public PrefabEditor(ContentWorkspace env, FileHandle file) : base(file)
    {
        Workspace = env;
    }

    public RSZFile GetRSZFile() => File.RSZ;
    public Scene? GetScene() => scene;

    protected override void OnFileReverted()
    {
        Reset();
    }

    private void Reset()
    {
        primaryInspector = null;
        failedToReadfile = false;
        CloseInspectors();
        context.ClearChildren();
        scene?.Dispose();
        scene = null;
    }

    private void CloseInspectors()
    {
        for (int i = inspectors.Count - 1; i >= 0; i--) {
            var inspector = inspectors[i];
            EditorWindow.CurrentWindow?.CloseSubwindow(inspector);
        }
    }

    protected override void DrawFileContents()
    {
        ImGui.PushID(Filename);
        if (context.children.Count == 0 || scene == null) {
            scene?.Dispose();
            context.ClearChildren();
            var root = Prefab.GetSharedInstance();
            if (Logger.ErrorIf(root == null, "Failed to instantiate prefab")) return;
            context.AddChild<PfbFile, List<ResourceInfo>>("Resources", File, getter: static (c) => c!.ResourceInfoList, handler: new TooltipUIHandler(new ListHandler(typeof(ResourceInfo)), "List of resources that will be preloaded together with the file ingame.\nShould be updated automatically on save."));

            context.AddChild("Filter", searcher, searcher);
            scene = root.Scene;
            if (scene == null) {
                scene = context.GetNativeWindow()?.SceneManager.CreateScene(((ISceneEditor)this).GetRootScene(context));
                if (Logger.ErrorIf(scene == null, "Failed to create new scene")) return;
                scene.Add(root);
            }
            context.AddChild(root.Name, root, new FullWindowWidthUIHandler(-50, new TextHeaderUIHandler("Children", new BoxedUIHandler(new GameObjectNodeEditor()))));
        }
        context.ShowChildrenUI();
        ImGui.PopID();
    }

    void IObjectUIHandler.OnIMGUI(UIContext container)
    {
        this.OnIMGUI();
    }

    void IWindowHandler.OnClosed()
    {
        CloseInspectors();
    }

    public void SetPrimaryInspector(object? target)
    {
        if (primaryInspector == null) {
            primaryInspector = AddInspector(target);
        } else {
            primaryInspector.Target = target;
        }
    }

    public ObjectInspector AddInspector(object? target)
    {
        var inspector = new ObjectInspector(this);
        var window = EditorWindow.CurrentWindow!.AddSubwindow(inspector);
        var child = context.AddChild("Inspector", window, NullUIHandler.Instance);
        inspectors.Add(inspector);
        inspector.Target = target;
        inspector.Closed += () => OnInspectorClosed(inspector);
        return inspector;
    }

    private void OnInspectorClosed(ObjectInspector inspector)
    {
        inspectors.Remove(inspector);
        if (primaryInspector == inspector) {
            primaryInspector = null;
        }
    }

    void IInspectorController.EmitSave()
    {
        foreach (var inspector in inspectors) inspector.Context.Save();
    }

    bool IFilterRoot.IsMatch(object? obj) => searcher.IsMatch(obj);
}
