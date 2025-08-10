using ContentPatcher;
using ImGuiNET;
using ReeLib;

namespace ContentEditor.App.ImguiHandling;

public class UserDataFileEditor : FileEditor, IWorkspaceContainer, IRSZFileEditor, IObjectUIHandler
{
    public override string HandlerName => "UserData";

    public RszInstance? Instance { get; private set; }
    public string Filename => Handle.Filepath;
    public UserFile File => Handle.GetFile<UserFile>();

    public ContentWorkspace Workspace { get; }

    private UIContext? fileContext;
    protected override bool IsRevertable => context.Changed;

    public UserDataFileEditor(ContentWorkspace env, FileHandle file) : base (file)
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
        fileContext?.ClearChildren();
        fileContext?.parent?.RemoveChild(fileContext);
        fileContext = null;
        failedToReadfile = false;
    }

    protected override void DrawFileContents()
    {
        if (Instance == null) {
            if (File.RSZ.ObjectList.Count == 0 && !TryRead(File)) return;
            Instance = File.RSZ.ObjectList[0];
        }

        ImGui.PushID(Filename);
        if (context.children.Count == 0 || fileContext == null) {
            context.ClearChildren();
            fileContext = context.AddChild("Base type: " + Instance.RszClass.name, Instance);
            WindowHandlerFactory.SetupRSZInstanceHandler(fileContext);
            fileContext.SetChangedNoPropagate(context.Changed);
        }
        fileContext.ShowUI();
        ImGui.PopID();
    }

    void IObjectUIHandler.OnIMGUI(UIContext container)
    {
        this.OnIMGUI();
    }
}
