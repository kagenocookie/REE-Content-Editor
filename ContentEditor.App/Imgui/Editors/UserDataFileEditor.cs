using ContentPatcher;
using ImGuiNET;
using ReeLib;

namespace ContentEditor.App.ImguiHandling;

public class UserDataFileEditor : FileEditor, IWorkspaceContainer, IRSZFileEditor, IObjectUIHandler, IFilterRoot
{
    public override string HandlerName => "UserData";

    public string Filename => Handle.Filepath;
    public UserFile File => Handle.GetFile<UserFile>();

    public ContentWorkspace Workspace { get; }

    private UIContext? fileContext;
    protected override bool IsRevertable => context.Changed;

    private readonly RszSearchHelper searcher = new();
    bool IFilterRoot.HasFilterActive => searcher.HasFilterActive;
    public object? MatchedObject { get => searcher.MatchedObject; set => searcher.MatchedObject = value; }

    public UserDataFileEditor(ContentWorkspace env, FileHandle file) : base (file)
    {
        Workspace = env;
    }

    public RSZFile GetRSZFile() => File.RSZ;
    protected override bool CanSave => context.GetWorkspace()?.Env.TryGetFileExtensionVersion("user", out _) != false || Handle.HandleType != FileHandleType.Embedded;

    protected override void Reset()
    {
        fileContext?.ClearChildren();
        fileContext?.parent?.RemoveChild(fileContext);
        fileContext = null;
        base.Reset();
    }

    protected override void DrawFileContents()
    {
        if (File.RSZ.ObjectList.Count == 0 && !TryRead(File)) return;

        ImGui.PushID(Filename);
        if (context.children.Count == 0 || fileContext == null) {
            context.ClearChildren();
            var instance = File.RSZ.ObjectList[0];
            // context.AddChild("Filter", searcher, searcher);
            fileContext = context.AddChild("Base type: " + instance.RszClass.name, instance);
            WindowHandlerFactory.SetupRSZInstanceHandler(fileContext);
            fileContext.SetChangedNoPropagate(context.Changed);
        }
        context.ShowChildrenUI();
        ImGui.PopID();
    }

    void IObjectUIHandler.OnIMGUI(UIContext container)
    {
        this.OnIMGUI();
    }

    bool IFilterRoot.IsMatch(object? obj) => searcher.IsMatch(obj);
}
