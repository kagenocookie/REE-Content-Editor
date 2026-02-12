using ContentPatcher;
using ReeLib;
using ReeLib.Wel;

namespace ContentEditor.App.ImguiHandling;

public class WelEditor : FileEditor, IWorkspaceContainer, IObjectUIHandler
{
    public override string HandlerName => "Event List";

    public RszInstance? Instance { get; private set; }
    public string Filename => Handle.Filepath;
    public WelFile File => Handle.GetFile<WelFile>();

    public ContentWorkspace Workspace { get; }

    protected override bool IsRevertable => context.Changed;

    public WelEditor(ContentWorkspace env, FileHandle file) : base (file)
    {
        Workspace = env;
    }

    protected override void DrawFileContents()
    {
        if (context.children.Count == 0) {
            context.AddChild<WelFile, string>("Bank Path", File, new ResourcePathPicker(context.GetWorkspace(), KnownFileFormats.SoundBank), (f) => f!.BankPath, (f, v) => f.BankPath = v ?? "");
            context.AddChild<WelFile, List<EventInfo>>("Events", File, new ListHandlerTyped<EventInfo>() { Filterable = true }, (f) => f!.Events);
        }
        context.ShowChildrenUI();
    }

    protected override void OnFileSaved()
    {
        base.OnFileSaved();
        context.ClearChildren();
    }

    void IObjectUIHandler.OnIMGUI(UIContext container)
    {
        this.OnIMGUI();
    }
}
