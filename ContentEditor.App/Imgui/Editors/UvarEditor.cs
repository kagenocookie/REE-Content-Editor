using ContentEditor.Core;
using ContentPatcher;
using ImGuiNET;
using ReeLib;

namespace ContentEditor.App.ImguiHandling;

public class UvarEditor : FileEditor, IWorkspaceContainer, IObjectUIHandler
{
    public override string HandlerName => "UVar";

    public RszInstance? Instance { get; private set; }
    public string Filename => Handle.Filepath;
    public UVarFile File => Handle.GetContent<UVarFile>();

    public ContentWorkspace Workspace { get; }

    private UIContext? rawContext;
    protected override bool IsRevertable => context.Changed;

    public UvarEditor(ContentWorkspace env, FileHandle file) : base (file)
    {
        Workspace = env;
    }

    protected override void OnFileReverted()
    {
        Reset();
    }

    private void Reset()
    {
        if (rawContext != null) {
            // not letting the child contexts dispose - so we don't dispose the file stream
            context.children.Clear();
            rawContext = null;
        }
        failedToReadfile = false;
    }

    protected override void DrawFileContents()
    {
        if (ImGui.TreeNode("Raw data")) {
            if (rawContext == null) {
                rawContext = context.AddChild("File", File);
                rawContext.uiHandler = new PlainObjectHandler();
                WindowHandlerFactory.SetupObjectUIContext(rawContext, null);
            }

            rawContext.ShowUI();
            ImGui.TreePop();
        }

        // foreach (var entry in File.Variables) {
        //     ImGui.PushID((int)entry.nameHash);
        //     if (ImguiHelpers.TreeNodeSuffix(entry.Name, entry.Value + " | " + entry.type + " | " + entry.guid.ToString())) {

        //         ImGui.TreePop();
        //     }
        //     ImGui.PopID();
        // }
    }

    void IObjectUIHandler.OnIMGUI(UIContext container)
    {
        this.OnIMGUI();
    }
}
