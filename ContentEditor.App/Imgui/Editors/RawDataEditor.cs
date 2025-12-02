using ContentEditor.Core;
using ContentPatcher;
using ImGuiNET;
using ReeLib;

namespace ContentEditor.App.ImguiHandling;

public class RawDataEditor : FileEditor, IWorkspaceContainer, IObjectUIHandler
{
    public override string HandlerName => Handle.Format.format.ToString();

    public RszInstance? Instance { get; private set; }
    public string Filename => Handle.Filepath;

    public ContentWorkspace Workspace { get; }

    private UIContext? rawContext;
    protected override bool IsRevertable => context.Changed;

    public RawDataEditor(ContentWorkspace env, FileHandle file) : base (file)
    {
        Workspace = env;
    }

    protected override void Reset()
    {
        base.Reset();
        rawContext = null;
    }

    protected virtual object GetContent() => Handle.Resource;

    protected override void DrawFileContents()
    {
        var content = GetContent();
        if (rawContext == null) {
            if (content is BaseFile) {
                rawContext = context.AddChild("Data", content, new LazyPlainObjectHandler(content.GetType()) { AutoOpen = ImGuiCond.Always });
            } else {
                rawContext = context.AddChild("Raw data", content, new PlainObjectHandler());
                WindowHandlerFactory.SetupObjectUIContext(rawContext, null, true);
                if (rawContext.children.Count == 1 && rawContext.children.First().uiHandler is LazyPlainObjectHandler lazy) {
                    lazy.AutoOpen = ImGuiCond.Always;
                }
            }
        }

        rawContext.ShowUI();
    }

    void IObjectUIHandler.OnIMGUI(UIContext container)
    {
        this.OnIMGUI();
    }
}

public class RawDataEditor<TFile> : RawDataEditor where TFile : BaseFile
{
    public RawDataEditor(ContentWorkspace env, FileHandle file) : base(env, file)
    {
    }

    protected override object GetContent() => Handle.GetFile<TFile>();
}