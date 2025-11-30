using System.Reflection;
using System.Reflection.Metadata;
using ContentEditor.Core;
using ContentPatcher;
using ImGuiNET;
using ReeLib;
using ReeLib.UVar;

namespace ContentEditor.App.ImguiHandling;

public class CfilEditor : FileEditor, IWorkspaceContainer, IObjectUIHandler
{
    public override string HandlerName => "CFIL";

    public RszInstance? Instance { get; private set; }
    public string Filename => Handle.Filepath;
    public CfilFile File => Handle.GetFile<CfilFile>();

    public ContentWorkspace Workspace { get; }

    protected override bool IsRevertable => context.Changed;

    public CfilEditor(ContentWorkspace env, FileHandle file) : base (file)
    {
        Workspace = env;
    }

    protected override void OnFileReverted()
    {
        Reset();
    }

    private void Reset()
    {
        if (context.children.Count > 0) {
            context.children.Clear();
        }
        failedToReadfile = false;
    }

    protected override void DrawFileContents()
    {
        if (context.children.Count == 0) {
            context.AddChild<CfilFile, Guid>("Layer Guid", File, new GuidFieldHandler(), (f) => f!.LayerGuid, (f, v) => f.LayerGuid = v);
            context.AddChild<CfilFile, Guid[]>("Masks", File, new ResizableArrayHandler(typeof(Guid)), (f) => f!.MaskGuids, (f, v) => f.MaskGuids = v!);
        }
        context.ShowChildrenUI();
    }

    void IObjectUIHandler.OnIMGUI(UIContext container)
    {
        this.OnIMGUI();
    }
}
