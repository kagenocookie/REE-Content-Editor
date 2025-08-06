using ContentEditor.App.Windowing;
using ContentPatcher;
using ImGuiNET;
using ReeLib;
using ReeLib.Pfb;

namespace ContentEditor.App.ImguiHandling;

public class PrefabEditor : FileEditor, IWorkspaceContainer, IRSZFileEditor, IObjectUIHandler, IInspectorController, IWindowHandler
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

    private readonly List<ObjectInspector> inspectors = new();

    public PrefabEditor(ContentWorkspace env, FileHandle file) : base(file)
    {
        Workspace = env;
    }

    public RSZFile GetRSZFile() => File.RSZ;

    protected override void OnFileReverted()
    {
        Reset();
    }

    private void Reset()
    {
        primaryInspector?.Context.ClearChildren();
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
            context.AddChild<PfbFile, List<ResourceInfo>>("Resources", File, getter: static (c) => c!.ResourceInfoList).AddDefaultHandler<List<ResourceInfo>>();

            scene = root.Scene;
            if (scene == null) {
                scene = context.GetNativeWindow()?.SceneManager.CreateScene();
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

    public void SetPrimaryInspector(object target)
    {
        if (primaryInspector == null) {
            primaryInspector = AddInspector(target);
        } else {
            primaryInspector.Target = target;
        }
    }

    public ObjectInspector AddInspector(object target)
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
}
