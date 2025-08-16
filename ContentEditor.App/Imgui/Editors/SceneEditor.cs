using ContentEditor.App.Windowing;
using ContentPatcher;
using ImGuiNET;
using ReeLib;
using ReeLib.Pfb;

namespace ContentEditor.App.ImguiHandling;

public class SceneEditor : FileEditor, IWorkspaceContainer, IRSZFileEditor, IObjectUIHandler, IInspectorController, IWindowHandler, IFilterRoot, ISceneEditor
{
    public override string HandlerName => "Scene";

    public string Filename => Handle.Filepath;
    public ScnFile File => Handle.GetFile<ScnFile>();
    public RawScene Prefab => Handle.GetCustomContent<RawScene>();

    public ContentWorkspace Workspace { get; }

    private ObjectInspector? primaryInspector;
    private Scene? scene;
    protected override bool IsRevertable => context.Changed;

    public object? PrimaryTarget => primaryInspector?.Target;

    private readonly RszSearchHelper searcher = new();
    bool IFilterRoot.HasFilterActive => searcher.HasFilterActive;
    public object? MatchedObject { get => searcher.MatchedObject; set => searcher.MatchedObject = value; }

    private readonly List<ObjectInspector> inspectors = new();

    public SceneEditor(ContentWorkspace env, FileHandle file, SceneEditor? parent = null) : base(file)
    {
        Workspace = env;
        primaryInspector = parent?.primaryInspector;
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
            var root = Prefab.GetSharedInstance(Workspace.Env);
            if (Logger.ErrorIf(root == null, "Failed to instantiate prefab")) return;
            context.AddChild<ScnFile, List<ResourceInfo>>("Resources", File, getter: static (c) => c!.ResourceInfoList).AddDefaultHandler<List<ResourceInfo>>();

            context.AddChild("Filter", searcher, searcher);
            scene = root.Scene;
            if (scene == null) {
                scene = context.GetNativeWindow()?.SceneManager.CreateScene();
                if (Logger.ErrorIf(scene == null, "Failed to create new scene")) return;
                scene.Add(root);
            }
            context.AddChild(root.Name, root, new FullWindowWidthUIHandler(-50, new TextHeaderUIHandler("Scene objects", new BoxedUIHandler(new FolderNodeEditor()))));
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
