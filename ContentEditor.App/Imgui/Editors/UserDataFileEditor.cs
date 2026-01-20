using ContentPatcher;
using ReeLib;

namespace ContentEditor.App.ImguiHandling;

public class UserDataFileEditor : RawRSZFileEditor, IRSZFileEditor, IFilterRoot
{
    public override string HandlerName => "UserData";

    protected override RSZFile RSZ => Handle.GetFile<UserFile>().RSZ;

    private readonly RszSearchHelper searcher = new();
    bool IFilterRoot.HasFilterActive => searcher.HasFilterActive;
    public object? MatchedObject { get => searcher.MatchedObject; set => searcher.MatchedObject = value; }

    public UserDataFileEditor(ContentWorkspace env, FileHandle file) : base (env, file)
    {
    }

    protected override bool CanSave => context.GetWorkspace()?.Env.TryGetFileExtensionVersion("user", out _) != false || Handle.HandleType != FileHandleType.Embedded;

    protected override void DrawFileContents()
    {
        if (RSZ.ObjectList.Count == 0 && !TryRead(Handle.GetFile<UserFile>())) return;

        base.DrawFileContents();
    }

    bool IFilterRoot.IsMatch(object? obj) => searcher.IsMatch(obj);
}

public class RawRSZFileEditor : FileEditor, IWorkspaceContainer, IRSZFileEditor, IObjectUIHandler
{
    public override string HandlerName => "RSZ Data";

    public string Filename => Handle.Filepath;
    protected virtual RSZFile RSZ => Handle.GetFile<RSZFile>();

    public ContentWorkspace Workspace { get; }

    private UIContext? fileContext;
    protected override bool IsRevertable => context.Changed && (!Workspace.Env.IsEmbeddedUserdataAny || Handle.HandleType != FileHandleType.Embedded);

    public RawRSZFileEditor(ContentWorkspace env, FileHandle file) : base (file)
    {
        Workspace = env;
    }

    public RSZFile GetRSZFile() => RSZ;
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
        if (RSZ.ObjectList.Count == 0 && !TryRead(RSZ)) return;

        ImGui.PushID(Filename);
        if (context.children.Count == 0 || fileContext == null) {
            context.ClearChildren();
            var instance = RSZ.ObjectList[0];
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
}