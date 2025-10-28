using System.ComponentModel;
using System.Numerics;
using Assimp;
using ContentEditor.App.Windowing;
using ContentPatcher;
using ImGuiNET;
using ReeLib;
using ReeLib.Bvh;
using ReeLib.Gui;
using ReeLib.Terr;

namespace ContentEditor.App.ImguiHandling;

public class GuiEditor : FileEditor, IWorkspaceContainer, IObjectUIHandler
{
    public override string HandlerName => "GUI";

    public RszInstance? Instance { get; private set; }
    public string Filename => Handle.Filepath;
    public GuiFile File => Handle.GetFile<GuiFile>();

    public ContentWorkspace Workspace { get; }

    protected override bool IsRevertable => context.Changed;

    public GuiEditor(ContentWorkspace env, FileHandle file) : base (file)
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
            context.AddChild<GuiFile, DisplayElement>("Root View Element", File, getter: (f) => f!.Root!).AddDefaultHandler();
            context.AddChild<GuiFile, List<GuiContainer>>("Containers", File, new ListHandlerTyped<GuiContainer>(), (f) => f!.Containers);
            context.AddChild<GuiFile, List<ResourceAttribute>>("Resource Attributes", File, getter: (f) => f!.ResourceAttributes!).AddDefaultHandler();
            context.AddChild<GuiFile, List<string>>("Child GUI Files", File, new ResourceListPathPicker(Workspace, KnownFileFormats.GUI), (f) => f!.ChildGUIs!);
            context.AddChild<GuiFile, List<string>>("Resources", File, new ResourceListPathPicker(Workspace), (f) => f!.ChildGUIs!);
            context.AddChild<GuiFile, List<ChildGuiOverride>>("Additional Data 1", File, new ListHandlerTyped<ChildGuiOverride>(), (f) => f!.ChildGuiOverries!);
            context.AddChild<GuiFile, List<AdditionalData2>>("Additional Data 2", File, new ListHandlerTyped<AdditionalData2>(), (f) => f!.Additional2!);
            context.AddChild<GuiFile, List<AdditionalData3>>("Additional Data 3", File, new ListHandlerTyped<AdditionalData3>(), (f) => f!.Additional3!);
        }

        context.ShowChildrenUI();
    }

    void IObjectUIHandler.OnIMGUI(UIContext container)
    {
        this.OnIMGUI();
    }
}
