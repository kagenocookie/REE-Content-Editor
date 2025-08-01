using ContentEditor.App.Windowing;
using ContentPatcher;
using ImGuiNET;
using ReeLib;
using ReeLib.Pfb;

namespace ContentEditor.App.ImguiHandling;

public interface IInspectorController
{
    public void SetPrimaryInspector(object target);
    public ObjectInspector AddInspector(object target);
    public object? PrimaryTarget { get; }
}

public class PrefabEditor : FileEditor, IWorkspaceContainer, IRSZFileEditor, IObjectUIHandler, IInspectorController, IWindowHandler
{
    public override string HandlerName => "Prefab";

    public RszInstance? Instance { get; private set; }
    public string Filename => Handle.Filepath;
    public PfbFile File => Handle.GetContent<PfbFile>();

    public ContentWorkspace Workspace { get; }

    private ObjectInspector? primaryInspector;
    private GameObject? Root;
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
        Instance = File.RSZ.ObjectList.FirstOrDefault();
    }

    private void Reset()
    {
        primaryInspector?.Context.ClearChildren();
        // primaryInspector?.Context.parent?.RemoveChild(primaryInspector);
        primaryInspector = null;
        failedToReadfile = false;
        CloseInspectors();
        context.ClearChildren();
        Root?.Dispose();
        Root = null;
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
        if (Instance == null) {
            if (File.RSZ.ObjectList.Count == 0 && !TryRead(File)) return;
            Instance = File.RSZ.ObjectList[0];
        }

        ImGui.PushID(Filename);
        if (context.children.Count == 0) {
            context.ClearChildren();
            context.AddChild<PfbFile, List<ResourceInfo>>("Resources", File, getter: static (c) => c!.ResourceInfoList).AddDefaultHandler<List<ResourceInfo>>();
            Root = new GameObject(File.GameObjects![0]);
            context.AddChild(Root.Name, Root, new FullWindowWidthUIHandler(-50, new TextHeaderUIHandler("Children", new BoxedUIHandler(new GameObjectChildListEditor()))));
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
