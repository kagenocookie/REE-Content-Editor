using System.Reflection;
using ContentEditor.App.Windowing;
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
        if (context.children.Count == 0) {
            WindowHandlerFactory.SetupObjectUIContext(context, typeof(Variable), members: DisplayedFields);
            context.children[2].uiHandler = new ValueChangeCallbackUIHandler(context.children[2].uiHandler!, TypeChangeCallback);
            context.children[3].uiHandler = new ValueChangeCallbackUIHandler(context.children[3].uiHandler!, FlagsChangedCallback);
        }

        if (AppImguiHelpers.CopyableTreeNode<Variable>(context)) {
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

        if ((prev & Variable.UvarFlags.HasRange) != (next & Variable.UvarFlags.HasRange)) {
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

[ObjectImguiHandler(typeof(UvarExpression), Stateless = true)]
public class UvarExpressionEditor : LazyPlainObjectHandler
{
    public UvarExpressionEditor() : base(typeof(UvarExpression))
    {
    }

    protected override bool DoTreeNode(UIContext context, object instance)
    {
        var show = base.DoTreeNode(context, instance);
        ImGui.SameLine();
        if (ImGui.Button("Graph Edit")) {
            var variable = context.FindValueInParentValues<Variable>();
            EditorWindow.CurrentWindow?.AddSubwindow(new UvarExpressionGraph(context.FindValueInParentValues<UVarFile>()!, context.Get<UvarExpression>(), context, variable?.Name ?? "Unknown Expression Graph"));
        }
        return show;
    }
}

[ObjectImguiHandler(typeof(NodeParameter), Stateless = true)]
public class NodeParameterEditor : IObjectUIHandler
{
    public void OnIMGUI(UIContext context)
    {
        var instance = context.Get<NodeParameter?>();
        if (instance == null) {
            ImGui.Text($"{context.label}: NULL");
            ImGui.PushID(context.label);
            if (ImguiHelpers.SameLine() && ImGui.Button("Create")) {
                UndoRedo.RecordSet(context, WindowHandlerFactory.Instantiate<NodeParameter>(context));
                context.AddDefaultHandler();
            }
            ImGui.PopID();
            return;
        }

        var show = ImguiHelpers.TreeNodeSuffix(context.label, instance.ToString());
        if (show) {
            if (context.children.Count == 0) {
                SetupFields(context, instance);
            }
            ImGui.PushID(context.label);
            for (int i = 0; i < context.children.Count; i++) {
                UIContext? child = context.children[i];
                child.ShowUI();
            }
            ImGui.PopID();
            ImGui.TreePop();
        }
    }

    private static void SetupFields(UIContext context, NodeParameter instance)
    {
        context.AddChild<NodeParameter, uint>(nameof(NodeParameter.nameHash).PrettyPrint(), instance, getter: x => x!.nameHash, setter: (x, v) => x.nameHash = v).AddDefaultHandler();
        context.AddChildContextSetter<NodeParameter, NodeParameter.NodeValueType>(nameof(NodeParameter.type).PrettyPrint(), instance, getter: x => x!.type, setter: (c, x, v) => {
            if (x.type != v) {
                var prevValue = x.value;
                x.ChangeType(v);
                if (EditorWindow.CurrentWindow != null) {
                    UndoRedo.AttachCallbackToCurrentAction(UndoRedo.CallbackType.Undo, () => {
                        x.value = prevValue;
                    });
                }
                c.parent?.ClearChildren();
            }
        }).AddDefaultHandler();
        context.AddChild<NodeParameter, object?>(nameof(NodeParameter.value).PrettyPrint(), instance, getter: x => x!.value, setter: (x, v) => x.value = v).AddDefaultHandler();

        if (instance.type == NodeParameter.NodeValueType.Guid) {
            var bhvtCtx = context.FindParentContextByHandler<BhvtFileEditor>();
            if (bhvtCtx == null) {
                context.AddChild<NodeParameter, RszInstance?>(nameof(NodeParameter.ReferenceObject).PrettyPrint(), instance, getter: x => x!.ReferenceObject, setter: (x, v) => x.ReferenceObject = v).AddDefaultHandler();
                return;
            }
            var bhvt = bhvtCtx.Get<BhvtFile>();

            RSZFile[] instanceSources = [
                bhvt.ExpressionTreeConditionsRsz,
                bhvt.StaticExpressionTreeConditionsRsz
            ];

            var refObjHandler = new SwappableRszInstanceHandler("via.behaviortree.Condition", rszSourceOverride: instanceSources, allowNull: true);
            context.AddChild<NodeParameter, RszInstance>(nameof(NodeParameter.ReferenceObject).PrettyPrint(), instance, refObjHandler, x => x?.ReferenceObject, (x, v) => {
                x.ReferenceObject = v;
                if (x.type == NodeParameter.NodeValueType.Guid && v?.Values.Length >= 2 && v.Values[1] is Guid guid) {
                    x.value = guid;
                }
            });
        }
    }
}