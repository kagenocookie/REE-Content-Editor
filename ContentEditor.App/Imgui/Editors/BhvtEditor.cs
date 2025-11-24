using ContentEditor.App.Windowing;
using ContentPatcher;
using ImGuiNET;
using ReeLib;
using ReeLib.Bhvt;
using ReeLib.Motfsm2;

namespace ContentEditor.App.ImguiHandling;

public class BhvtEditor : FileEditor, IWorkspaceContainer, IObjectUIHandler
{
    public override string HandlerName => "Behavior Tree";

    public RszInstance? Instance { get; private set; }
    public string Filename => Handle.Filepath;
    public BhvtFile File => Handle.GetFile<BhvtFile>();

    public ContentWorkspace Workspace { get; }

    protected override bool IsRevertable => context.Changed;

    static BhvtEditor()
    {
        WindowHandlerFactory.DefineInstantiator<BHVTNode>((ctx) => {
            var rootEditor = ctx.FindHandlerInParents<FileEditor>();
            GameVersion version;
            NodeAttribute attrs = 0;
            if (rootEditor is BhvtEditor bhvt) {
                version = bhvt.File.Header.Version;
            } else if (rootEditor is MotFsm2FileEditor mfs2) {
                version = mfs2.File.BhvtFile.Header.Version;
                attrs = NodeAttribute.IsEnabled|NodeAttribute.IsRestartable|NodeAttribute.IsFSMNode;
            } else {
                version = GameVersion.dd2;
            }
            var parent = ctx.FindParentContextByHandler<BHVTNodeEditor>()?.Get<BHVTNode>();
            var isBhvt = rootEditor is BhvtEditor;
            return new BHVTNode(version) { Attributes = attrs, ID = new NodeID((uint)System.Random.Shared.Next(), 0), ParentID = parent?.ID ?? default };
        });
    }

    public BhvtEditor(ContentWorkspace env, FileHandle file) : base (file)
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
            context.AddChild<BhvtFile, BhvtFile?>("Behavior Tree", File, new BhvtFileEditor(), (f) => f!);
        }
        context.children[0].ShowUI();
    }

    void IObjectUIHandler.OnIMGUI(UIContext container)
    {
        this.OnIMGUI();
    }

    internal static IEnumerable<BHVTNode> FindBhvtNodes(UIContext context, bool force) => context.FindValueInParentValues<BhvtFile>()?.Nodes ?? [];
}

public class MotFsm2FileEditor : FileEditor, IWorkspaceContainer, IObjectUIHandler
{
    public override string HandlerName => "Motion FSM";

    public RszInstance? Instance { get; private set; }
    public string Filename => Handle.Filepath;
    public Motfsm2File File => Handle.GetFile<Motfsm2File>();

    public ContentWorkspace Workspace { get; }

    protected override bool IsRevertable => context.Changed;

    public MotFsm2FileEditor(ContentWorkspace env, FileHandle file) : base (file)
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
            context.AddChild<Motfsm2File, List<TransitionData>>("Transition Data", File, new ListHandlerTyped<TransitionData>(), (f) => f!.TransitionDatas);
            context.AddChild<Motfsm2File, BhvtFile?>("Behavior Tree", File, new BhvtFileEditor(), (f) => f!.BhvtFile);
        }
        context.ShowChildrenUI();
    }

    void IObjectUIHandler.OnIMGUI(UIContext container)
    {
        this.OnIMGUI();
    }
}

[ObjectImguiHandler(typeof(BhvtFile))]
public class BhvtFileEditor : IObjectUIHandler
{
    public void OnIMGUI(UIContext context)
    {
        if (context.children.Count == 0) {
            var file = context.Get<BhvtFile>();
            context.AddChild<BhvtFile, UVarFile?>("Variables", file, new NestedUIHandlerStringSuffixed(new UvarFileImguiHandler()), getter: (f) => f!.Variable);
            context.AddChild<BhvtFile, uint>("Behavior Tree Hash", file, getter: (f) => f!.Header.hash, setter: (f, v) => f.Header.hash = v).AddDefaultHandler();
            context.AddChild<BhvtFile, List<UVarFile>>("ReferenceTrees", file, new ListHandlerTyped<UVarFile>(), (f) => f!.ReferenceTrees);
            context.AddChild<BhvtFile, BHVTNode>("Root Node", file, getter: (f) => f!.RootNode).AddDefaultHandler<BHVTNode>();
            context.AddChild<BhvtFile, List<BhvtGameObjectReference>>("GameObject References", file, new ListHandlerTyped<BhvtGameObjectReference>(), (f) => f!.GameObjectReferences);
            context.AddChild<BhvtFile, BhvtObjectIndexTable>("Action Object Table", file, getter: (f) => f!.ActionObjectTable).AddDefaultHandler();
            context.AddChild<BhvtFile, List<BHVTNode>>("All Nodes", file, new ListHandlerTyped<BHVTNode>() { Filterable = true, CanCreateRemoveElements = false }, getter: (f) => f!.Nodes);
        }
        context.ShowChildrenUI();
    }
}

[ObjectImguiHandler(typeof(BHVTNode))]
public class BHVTNodeEditor : IObjectUIHandler
{
    public void OnIMGUI(UIContext context)
    {
        if (context.children.Count == 0) {
            var file = context.Get<BHVTNode>();
            context.AddChild<BHVTNode, NodeID>("ID", file, getter: (f) => f!.ID, setter: (f, v) => f.ID = v).AddDefaultHandler();
            context.AddChild<BHVTNode, string>("Name", file, getter: (f) => f!.Name, setter: (f, v) => f.Name = v ?? "").AddDefaultHandler();
            context.AddChild<BHVTNode, int>("Priority", file, getter: (f) => f!.Priority, setter: (f, v) => f.Priority = v).AddDefaultHandler();
            context.AddChild<BHVTNode, NodeID>("Parent Node ID", file, NodeIDEditor.InstanceDisabled, (f) => f!.ParentID);
            // TODO: selector must be nullable
            context.AddChild<BHVTNode, RszInstance>("Selector", file, getter: (f) => f!.Selector, setter: (f, v) => f.Selector = v).AddDefaultHandler<RszInstance>();
            context.AddChild<BHVTNode, List<RszInstance>>("SelectorCallers", file, new ListHandlerTyped<RszInstance>(), getter: (f) => f!.SelectorCallers).AddDefaultHandler();
            context.AddChild<BHVTNode, RszInstance>("SelectorCallerCondition", file, getter: (f) => f!.SelectorCallerCondition, setter: (f, v) => f.SelectorCallerCondition = v).AddDefaultHandler<RszInstance>();
            context.AddChild<BHVTNode, bool>("isBranch", file, getter: (f) => f!.isBranch, setter: (f, v) => f.isBranch = v).AddDefaultHandler();
            context.AddChild<BHVTNode, bool>("isEnd", file, getter: (f) => f!.isEnd, setter: (f, v) => f.isEnd = v).AddDefaultHandler();
            context.AddChild<BHVTNode, NodeAttribute>("Attributes", file, new CsharpFlagsEnumFieldHandler<NodeAttribute, ushort>(), (f) => f!.Attributes, (f, v) => f.Attributes = v);
            context.AddChild<BHVTNode, WorkFlags>("Work Flags", file, new CsharpFlagsEnumFieldHandler<WorkFlags, ushort>(), (f) => f!.WorkFlags, (f, v) => f.WorkFlags = v);
            context.AddChild<BHVTNode, List<NAction>>("Actions", file, new ListHandlerTyped<NAction>(), getter: (f) => f!.Actions.Actions);
            context.AddChild<BHVTNode, List<uint>>("Tags", file, getter: (f) => f!.Tags).AddDefaultHandler();
            context.AddChild<BHVTNode, List<NState>>("States", file, new ListHandlerTyped<NState>(), (f) => f!.States.States);
            context.AddChild<BHVTNode, List<NAllState>>("AllStates (?)", file, new ListHandlerTyped<NAllState>() { Filterable = true }, (f) => f!.AllStates.AllStates);
            context.AddChild<BHVTNode, List<NTransition>>("Transitions", file, new ListHandlerTyped<NTransition>(), (f) => f!.Transitions.Transitions);
            context.AddChild<BHVTNode, List<NChild>>("Children", file, new ListHandlerTyped<NChild>() { Filterable = true }, (f) => f!.Children.Children);
            context.AddChild<BHVTNode, UVarFile?>("Reference Tree", file, new InstancePickerHandler<UVarFile>(true, (ctx, force) => {
                return ctx.FindValueInParentValues<BhvtFile>()?.ReferenceTrees ?? [];
            }), (f) => f!.ReferenceTree);
        }
        context.ShowChildrenNestedUI();
    }
}

[ObjectImguiHandler(typeof(NTransition))]
public class BhvtTransitionEditor : IObjectUIHandler
{
    public void OnIMGUI(UIContext context)
    {
        if (context.children.Count == 0) {
            var file = context.Get<NTransition>();
            context.AddChild<NTransition, List<uint>>("States", file, getter: (f) => f!.transitionEvents).AddDefaultHandler();
            context.AddChild<NTransition, BHVTNode>("StartNode", file, new InstancePickerHandler<BHVTNode>(true, BhvtEditor.FindBhvtNodes), getter: (f) => f!.StartNode, setter: (f, v) => f.StartNode = v);
            context.AddChild<NTransition, RszInstance>("Condition", file, new RszInstanceHandler(), getter: (f) => f!.Condition, setter: (f, v) => f.Condition = v);
        }
        context.ShowChildrenNestedUI();
    }
}

[ObjectImguiHandler(typeof(NChild))]
public class NChildEditor : IObjectUIHandler
{
    public void OnIMGUI(UIContext context)
    {
        if (context.children.Count == 0) {
            var file = context.Get<NChild>();
            context.AddChild<NChild, RszInstance>("Condition", file, new RszInstanceHandler(), getter: (f) => f!.Condition, setter: (f, v) => f.Condition = v);
            context.AddChild<NChild, BHVTNode>("Node", file, getter: (f) => f!.ChildNode, setter: (f, v) => f.ChildNode = v).AddDefaultHandler<BHVTNode>();
        }
        context.ShowChildrenNestedUI();
    }
}

[ObjectImguiHandler(typeof(NodeID), Stateless = true)]
public class NodeIDEditor : IObjectUIHandler
{
    public static readonly NodeIDEditor Instance = new();
    public static readonly NodeIDEditor InstanceDisabled = new(true);

    private readonly bool disabled;

    public NodeIDEditor() { }

    private NodeIDEditor(bool disabled)
    {
        this.disabled = disabled;
    }

    public unsafe void OnIMGUI(UIContext context)
    {
        var nodeId = context.Get<NodeID>();
        if (disabled) ImGui.BeginDisabled();
        if (ImGui.DragScalarN(context.label, ImGuiDataType.U32, (IntPtr)(&nodeId), 2, 0.01f)) {
            UndoRedo.RecordSet(context, nodeId);
        }
        if (disabled) ImGui.EndDisabled();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled) && ImGui.IsKeyPressed(ImGuiKey.MouseRight)) {
            ImGui.OpenPopup(context.label);
        }
        ImGui.BeginDisabled(false);
        if (ImGui.BeginPopup(context.label)) {
            if (ImGui.Selectable("Copy ID")) {
                EditorWindow.CurrentWindow?.CopyToClipboard(nodeId.ID.ToString(), $"Copied {nodeId.ID.ToString()}!");
                ImGui.CloseCurrentPopup();
            }
            ImGui.EndPopup();
        }
        ImGui.EndDisabled();
    }
}
