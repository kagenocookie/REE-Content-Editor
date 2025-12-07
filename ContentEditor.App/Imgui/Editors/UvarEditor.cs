using System.Reflection;
using ContentEditor.Core;
using ContentPatcher;
using ReeLib;
using ReeLib.UVar;

namespace ContentEditor.App.ImguiHandling;

public class UvarEditor : FileEditor, IWorkspaceContainer, IObjectUIHandler
{
    public override string HandlerName => "UVar";

    public RszInstance? Instance { get; private set; }
    public string Filename => Handle.Filepath;
    public UVarFile File => Handle.GetFile<UVarFile>();

    public ContentWorkspace Workspace { get; }

    protected override bool IsRevertable => context.Changed;

    public UvarEditor(ContentWorkspace env, FileHandle file) : base (file)
    {
        Workspace = env;
    }

    protected override void DrawFileContents()
    {
        if (context.children.Count == 0) {
            context.AddChild("Data", File, new UvarFileImguiHandler());
        }
        context.ShowChildrenUI();
    }

    void IObjectUIHandler.OnIMGUI(UIContext container)
    {
        this.OnIMGUI();
    }
}

[ObjectImguiHandler(typeof(UVarFile), Stateless = true)]
public class UvarFileImguiHandler : IObjectUIHandler
{
    public void OnIMGUI(UIContext context)
    {
        if (context.children.Count == 0) {
            var file = context.Get<UVarFile>();
            context.AddChild("Name", file.Header, new StringFieldHandler(), (c) => ((HeaderStruct)c.target!).name, (c, v) => ((HeaderStruct)c.target!).name = (string)v!);
            context.AddChild("Variables", file.Variables, new UvarVariableListHandler() { Filterable = true });
            context.AddChild("Embedded files", file.EmbeddedUVARs, new ListHandler<UVarFile>(() => new UVarFile(new FileHandler())));
        }
        context.ShowChildrenUI();
    }
}

[ObjectImguiHandler(typeof(List<Variable>), Stateless = true)]
public class UvarVariableListHandler : ListHandlerTyped<Variable>
{
    protected override bool MatchesFilter(object? obj, string filter)
    {
        if (obj is Variable vv) {
            return vv.Name.Contains(filter, StringComparison.InvariantCultureIgnoreCase) || Guid.TryParse(filter, out var guid) && vv.guid == guid;
        }
        return false;
    }
}

[ObjectImguiHandler(typeof(Variable), Stateless = true)]
public class UvarVariableImguiHandler : IObjectUIHandler
{
    private static MemberInfo[] DisplayedFields = [
        typeof(Variable).GetProperty(nameof(Variable.Name))!,
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
            context.children[2].uiHandler = new ValueChangeCallbackUIHandler(context.children[2].uiHandler!, TypeChangeCallback);
            context.children[3].uiHandler = new ValueChangeCallbackUIHandler(context.children[3].uiHandler!, FlagsChangedCallback);
        }
        if (ImguiHelpers.TreeNodeSuffix(context.label, vv.ToString())) {
            for (int i = 0; i < context.children.Count; i++) {
                var child = context.children[i];
                child.ShowUI();
                if ((i == 2 || i == 3) && child.StateBool) {
                    context.children[4].uiHandler = WindowHandlerFactory.CreateUIHandler(context.children[4].GetRaw(), null);
                    context.children[4].ClearChildren();
                    child.StateBool = false;
                }
            }
            ImGui.TreePop();
        }
    }

    private static void FlagsChangedCallback(UIContext context, object? before, object? after)
    {
        if (before == null || after == null) return;
        var prev = (Variable.UvarFlags)before;
        var next = (Variable.UvarFlags)after;

        if ((prev & Variable.UvarFlags.IsVec3) != (next & Variable.UvarFlags.IsVec3)) {
            try {
                context.parent!.Get<Variable>().ResetValue();
                context.StateBool = true;
            } catch (Exception e) {
                Logger.Error(e, "Unsupported Uvar variable type and flag combination");
                context.Set(before);
            }
        }
    }

    private static void TypeChangeCallback(UIContext context, object? before, object? after)
    {
        try {
            context.parent!.Get<Variable>().ResetValue();
            context.StateBool = true;
        } catch (Exception e) {
            Logger.Error(e, "Unsupported Uvar variable type and flag combination");
            context.Set(before);
        }
    }
}
