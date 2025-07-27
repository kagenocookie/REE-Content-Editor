using ContentPatcher;
using ImGuiNET;
using ReeLib;

namespace ContentEditor.App.ImguiHandling;

public class UserDataFileEditor : FileEditor, IWorkspaceContainer, IRSZFileEditor, IObjectUIHandler, IUIContextEventHandler
{
    public override string HandlerName => "UserData";

    public RszInstance? Instance { get; private set; }
    public string Filename => Handle.Filepath;
    public UserFile File => Handle.GetContent<UserFile>();

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

    protected override void OnFileSaved()
    {
        // Reset();
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
            WindowHandlerFactory.CreateRSZInstanceHandlerContext(fileContext);
            fileContext.SetChangedNoPropagate(context.Changed);
        }
        fileContext.ShowUI();
        ImGui.PopID();
    }

    void IObjectUIHandler.OnIMGUI(UIContext container)
    {
        this.OnIMGUI();
    }

    public bool HandleEvent(UIContext context, EditorUIEvent eventData)
    {
        if (eventData.type == UIContextEvent.Changed) {
            var ws = context.GetWorkspace();
            if (ws != null) {
                ws.ResourceManager.MarkFileResourceModified(Filename, true);
            }
        }
        if (eventData.type == UIContextEvent.Reverting) {
            if (eventData.origin.IsChildOf(context)) {
                return false;
            }

            if (eventData.origin == context) {
                var ws = context.GetWorkspace();
                if (ws != null) {
                    ws.ResourceManager.MarkFileResourceModified(Filename, false);
                }
            }

        }
        return false;
    }
}
