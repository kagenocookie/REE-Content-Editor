using System.Reflection;
using ContentEditor.Core;
using ContentPatcher;
using ImGuiNET;
using ReeLib;
using ReeLib.UVar;

namespace ContentEditor.App.ImguiHandling;

public class UvarEditor : FileEditor, IWorkspaceContainer, IObjectUIHandler
{
    public override string HandlerName => "UVar";

    public RszInstance? Instance { get; private set; }
    public string Filename => Handle.Filepath;
    public UVarFile File => Handle.GetContent<UVarFile>();

    public ContentWorkspace Workspace { get; }

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
        if (context.children.Count > 0) {
            // not letting the child contexts dispose - so we don't dispose the file stream
            context.children.Clear();
        }
        failedToReadfile = false;
    }

    protected override void DrawFileContents()
    {
        if (context.children.Count == 0) {
            context.AddChild("Raw data", File, new LazyPlainObjectHandler(typeof(UVarFile)));
            context.AddChild("Data", File, new UvarFileImguiHandler());
        }
        context.ShowChildrenUI();
    }

    void IObjectUIHandler.OnIMGUI(UIContext container)
    {
        this.OnIMGUI();
    }
}

[ObjectImguiHandler(typeof(UVarFile))]
public class UvarFileImguiHandler : IObjectUIHandler
{
    public void OnIMGUI(UIContext context)
    {
        if (context.children.Count == 0) {
            var file = context.Get<UVarFile>();
            context.AddChild("Name", file.Header, new StringFieldHandler(), (c) => ((HeaderStruct)c.target!).name, (c, v) => ((HeaderStruct)c.target!).name = (string)v!);
            context.AddChild("Variables", file.Variables).AddDefaultHandler<List<Variable>>();
            context.AddChild("Embedded files", file.EmbeddedUVARs, new ListHandler<UVarFile>(() => new UVarFile(new FileHandler())));
        }
        context.ShowChildrenUI();
    }
}

[ObjectImguiHandler(typeof(Variable))]
public class UvarVariableImguiHandler : IObjectUIHandler
{
    private static MemberInfo[] DisplayedFields = [
        typeof(Variable).GetField(nameof(Variable.guid))!,
        typeof(Variable).GetField(nameof(Variable.type))!,
        typeof(Variable).GetField(nameof(Variable.flags))!,
        typeof(Variable).GetField(nameof(Variable.Value))!,
        typeof(Variable).GetProperty(nameof(Variable.Expression))!,
    ];

    public void OnIMGUI(UIContext context)
    {
        var vv = context.Get<Variable>();
        if (context.children.Count == 0) {
            WindowHandlerFactory.SetupObjectUIContext(context, typeof(Variable), members: DisplayedFields);
        }
        if (ImguiHelpers.TreeNodeSuffix(context.label, vv.ToString())) {
            context.ShowChildrenUI();
            ImGui.TreePop();
        }
    }
}
