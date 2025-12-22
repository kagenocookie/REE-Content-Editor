using ContentEditor.App.Windowing;
using ContentEditor.Core;
using ContentPatcher;
using ReeLib;
using ReeLib.Bhvt;
using ReeLib.Common;
using ReeLib.Motfsm2;
using ReeLib.Tmlfsm2;

namespace ContentEditor.App.ImguiHandling;

public class BhvtEditor : FileEditor, IWorkspaceContainer, IObjectUIHandler
{
    public override string HandlerName => "Behavior Tree";

    public RszInstance? Instance { get; private set; }
    public string Filename => Handle.Filepath;
    public BhvtFile File => Handle.GetFile<BhvtFile>();

    public ContentWorkspace Workspace { get; }

    protected override bool IsRevertable => context.Changed;

    public BhvtEditor(ContentWorkspace env, FileHandle file) : base (file)
    {
        Workspace = env;
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

    internal static IEnumerable<BHVTNode> FindBhvtNodes(UIContext context, bool force) => context.FindValueInParentValues<BhvtFile>()?.GetAllNodes() ?? [];
    internal static IEnumerable<BHVTNode> FindChildBhvtNodes(UIContext context, bool force) => context.FindValueInParentValues<BHVTNode>()?.Children.Children
        .Where(x => x.ChildNode != null).Select(ch => ch.ChildNode!) ?? [];
}

public class MotFsm2FileEditor : FileEditor, IWorkspaceContainer, IObjectUIHandler
{
    public override string HandlerName => "Motion FSM2";

    public RszInstance? Instance { get; private set; }
    public string Filename => Handle.Filepath;
    public Motfsm2File File => Handle.GetFile<Motfsm2File>();

    public ContentWorkspace Workspace { get; }

    protected override bool IsRevertable => context.Changed;

    public MotFsm2FileEditor(ContentWorkspace env, FileHandle file) : base (file)
    {
        Workspace = env;
    }

    protected override void DrawFileContents()
    {
        if (context.children.Count == 0) {
            context.AddChild<Motfsm2File, List<TransitionMap>>("Transition Map List", File, new ListHandler(typeof(TransitionMap), typeof(List<TransitionMap>)), (f) => f!.TransitionMaps);
            context.AddChild<Motfsm2File, List<TransitionData>>("Transition Data List", File, new ListHandlerTyped<TransitionData>(), (f) => f!.TransitionDatas);
            context.AddChild<Motfsm2File, BhvtFile?>("Behavior Tree", File, new BhvtFileEditor(), (f) => f!.BhvtFile);
        }
        context.ShowChildrenUI();
    }

    void IObjectUIHandler.OnIMGUI(UIContext container)
    {
        this.OnIMGUI();
    }
}

[ObjectImguiHandler(typeof(TransitionMap))]
public class TransitionMapEditor : IObjectUIHandler
{
    public unsafe void OnIMGUI(UIContext context)
    {
        var map = context.Get<TransitionMap>();
        ImGui.PushID(context.label);
        ImGui.PushItemWidth(ImGui.CalcItemWidth() / 2 - ImGui.GetStyle().FramePadding.X);
        var changed = ImGui.InputScalar("ID"u8, ImGuiDataType.U32, &map.transitionId);
        ImGui.SameLine();
        changed = ImGui.InputScalar("Data Index"u8, ImGuiDataType.U32, &map.dataIndex) || changed;
        if (changed) {
            UndoRedo.RecordSet(context, map, undoId: $"tmap{context.label}");
        }
        ImGui.PopItemWidth();
        ImGui.PopID();
    }
}

public class TmlFsm2FileEditor : FileEditor, IWorkspaceContainer, IObjectUIHandler
{
    public override string HandlerName => "Timeline FSM";

    public RszInstance? Instance { get; private set; }
    public string Filename => Handle.Filepath;
    public Tmlfsm2File File => Handle.GetFile<Tmlfsm2File>();

    public ContentWorkspace Workspace { get; }

    protected override bool IsRevertable => context.Changed;

    public TmlFsm2FileEditor(ContentWorkspace env, FileHandle file) : base (file)
    {
        Workspace = env;
    }

    protected override void DrawFileContents()
    {
        if (context.children.Count == 0) {
            context.AddChild<Tmlfsm2File, List<TimelineBhvt>>("Behavior Trees", File, new ListHandlerTyped<TimelineBhvt>(), (f) => f!.BehaviorTrees);
            context.AddChild<Tmlfsm2File, List<TimelineClip>>("Clips", File, new ListHandlerTyped<TimelineClip>(), (f) => f!.Clips);
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
            context.AddChild<BhvtFile, uint>("Behavior Tree Hash", file, getter: (f) => f!.Header.hash, setter: (f, v) => f.Header.hash = v).AddDefaultHandler();
            context.AddChild<BhvtFile, UVarFile?>("Variables", file, new NestedUIHandlerStringSuffixed(new UvarFileImguiHandler()), getter: (f) => f!.UserVariables);
            context.AddChild<BhvtFile, List<UVarFile>>("SubVariables", file, new ListHandlerTyped<UVarFile>(), (f) => f!.SubVariables);
            context.AddChild<BhvtFile, BHVTNode>("Root Node", file, getter: (f) => f!.RootNode).AddDefaultHandler<BHVTNode>();
            context.AddChild<BhvtFile, List<BhvtGameObjectReference>>("GameObject References", file, new ListHandlerTyped<BhvtGameObjectReference>(), (f) => f!.GameObjectReferences);
            context.AddChild<BhvtFile, BhvtObjectIndexTable>("Action Object Table", file, getter: (f) => f!.ActionObjectTable).AddDefaultHandler();
            context.AddChild<BhvtFile, List<BHVTNode>>("All Nodes", file, new ListHandlerTyped<BHVTNode>() { Filterable = true, CanCreateRemoveElements = false }, getter: (f) => f!.Nodes);
        }
        // if (ImGui.Button("View Node Tree")) {
        //     EditorWindow.CurrentWindow?.AddSubwindow(new BhvtNodeTree(context.Get<BhvtFile>()));
        // }
        context.ShowChildrenUI();
    }
}

[ObjectImguiHandler(typeof(BHVTNode))]
public class BHVTNodeEditor : IObjectUIHandler
{
    private static readonly ConditionalUIHandler FsmBoolHandler = new ConditionalUIHandler(BoolFieldHandler.Instance, FSMNodeCondition);
    private static bool FSMNodeCondition(UIContext c) => ((BHVTNode)c.target!).Attributes.HasFlag(NodeAttribute.IsFSMNode);
    private static bool RefTreeCondition(UIContext c) => ((BHVTNode)c.target!).Attributes.HasFlag(NodeAttribute.HasReferenceTree);

    static BHVTNodeEditor()
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

        WindowHandlerFactory.DefineInstantiator<NState>((ctx) => {
            var rootEditor = ctx.FindHandlerInParents<FileEditor>();
            if (rootEditor is not MotFsm2FileEditor mfs2) {
                Logger.Error("State is not inside a Motfsm, this won't work.");
                return new NState();
            }

            var version = mfs2.File.BhvtFile.Header.Version;
            var state = new NState();
            state.transitionMapID = (uint)System.Random.Shared.Next();
            var data = mfs2.File.TransitionDatas.FirstOrDefault();
            if (data == null) {
                mfs2.File.TransitionDatas.Add(data = new TransitionData() {
                    id = (uint)System.Random.Shared.Next(),
                    EndType = EndType.EndOfMotion
                });
            }
            mfs2.File.ChangeTransitionMapping(state.transitionMapID, data.id);
            state.TransitionData = data;

            return state;
        });
    }

    public void OnIMGUI(UIContext context)
    {
        var node = context.Get<BHVTNode>();
        if (node == null) {
            ImGui.Text(context.label + ": null");
            ImGui.SameLine();
            if (ImGui.Button("Create")) {
                UndoRedo.RecordSet(context, WindowHandlerFactory.Instantiate(context, typeof(BHVTNode)));
            }
            return;
        }

        if (context.children.Count == 0) {
            context.AddChild<BHVTNode, NodeID>("ID", node, getter: (f) => f!.ID, setter: (f, v) => f.ID = v).AddDefaultHandler();
            context.AddChild<BHVTNode, string>("Name", node, getter: (f) => f!.Name, setter: (f, v) => f.Name = v ?? "").AddDefaultHandler();
            context.AddChild<BHVTNode, int>("Priority", node, getter: (f) => f!.Priority, setter: (f, v) => f.Priority = v).AddDefaultHandler();
            context.AddChild<BHVTNode, NodeID>("Parent Node ID", node, NodeIDEditor.InstanceDisabled, (f) => f!.ParentID);
            context.AddChild<BHVTNode, NodeAttribute>("Attributes", node, new CsharpFlagsEnumFieldHandler<NodeAttribute, ushort>(), (f) => f!.Attributes, (f, v) => f.Attributes = v);
            context.AddChild<BHVTNode, string>("Reference Tree", node, new ConditionalUIHandler(new ResourcePathPicker(context.GetWorkspace(), KnownFileFormats.MotionFsm2), RefTreeCondition), f => f!.ReferenceTree, (f, v) => f.ReferenceTree = v);
            context.AddChild<BHVTNode, RszInstance>("Selector", node, new NestedRszClassnamePickerHandler("via.behaviortree.Selector", allowNull: true), (f) => f!.Selector, (f, v) => f.Selector = v);
            context.AddChild<BHVTNode, List<RszInstance>>("SelectorCallers", node, new RszListInstanceHandler("via.behaviortree.SelectorCaller"), getter: (f) => f!.SelectorCallers);
            context.AddChild<BHVTNode, RszInstance>("SelectorCallerCondition", node, new NestedRszClassnamePickerHandler("via.behaviortree.Condition", allowNull: true), (f) => f!.SelectorCallerCondition, (f, v) => f.SelectorCallerCondition = v);
            context.AddChild<BHVTNode, bool>("isBranch", node, FsmBoolHandler, (f) => f!.isBranch, (f, v) => f.isBranch = v);
            context.AddChild<BHVTNode, bool>("isEnd", node, FsmBoolHandler, (f) => f!.isEnd, (f, v) => f.isEnd = v);
            context.AddChild<BHVTNode, WorkFlags>("Work Flags", node, new ConditionalUIHandler(new CsharpFlagsEnumFieldHandler<WorkFlags, ushort>(), FSMNodeCondition), (f) => f!.WorkFlags, (f, v) => f.WorkFlags = v);
            context.AddChild<BHVTNode, List<NAction>>("Actions", node, new ListHandlerTyped<NAction>(), getter: (f) => f!.Actions.Actions);
            context.AddChild<BHVTNode, List<uint>>("Tags", node, new ConditionalUIHandler(new ListHandler(typeof(uint), typeof(List<uint>)), FSMNodeCondition), (f) => f!.Tags);
            context.AddChild<BHVTNode, List<NState>>("States", node, new ListHandlerTyped<NState>(), (f) => f!.States.States);
            context.AddChild<BHVTNode, List<NAllState>>("AllStates (?)", node, new ListHandlerTyped<NAllState>() { Filterable = true }, (f) => f!.AllStates.AllStates);
            context.AddChild<BHVTNode, List<NTransition>>("Transitions", node, new ListHandlerTyped<NTransition>(), (f) => f!.Transitions.Transitions);
            context.AddChild<BHVTNode, List<NChild>>("Children", node, new NodeChildrenListHandler(), (f) => f!.Children.Children);
        }

        var show = ImguiHelpers.TreeNodeSuffix(context.label, node.ToString());
        if (ImGui.BeginPopupContextItem(context.label)) {
            if (ImGui.Selectable("Copy Node")) {
                VirtualClipboard.CopyToClipboard(node.Clone());
            }
            ImGui.EndPopup();
        }
        if (show) {
            context.ShowChildrenUI();
            ImGui.TreePop();
        }
    }
}

[ObjectImguiHandler(typeof(NTransition))]
public class BhvtTransitionEditor : IObjectUIHandler
{
    public void OnIMGUI(UIContext context)
    {
        if (context.children.Count == 0) {
            var file = context.Get<NTransition>();
            context.AddChild<NTransition, BHVTNode>("StartNode", file, new InstancePickerHandler<BHVTNode>(true, BhvtEditor.FindChildBhvtNodes), (f) => f!.StartNode, (f, v) => f.StartNode = v);
            // context.AddChild<NTransition, RszInstance>("Condition", file, new NestedUIHandlerStringSuffixed(new RszClassnamePickerHandler("via.behaviortree.Condition", allowNull: true, allowCopy: true)), (f) => f!.Condition, (f, v) => f.Condition = v);
            context.AddChild<NTransition, RszInstance>("Condition", file, new NestedRszClassnamePickerHandler("via.behaviortree.Condition", allowNull: true), (f) => f!.Condition, (f, v) => f.Condition = v);
            context.AddChild<NTransition, List<uint>>("States", file, getter: (f) => f!.transitionEvents).AddDefaultHandler();
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
            context.AddChild<NChild, RszInstance>("Condition", file, new NestedRszClassnamePickerHandler("via.behaviortree.Condition", allowNull: true), (f) => f!.Condition, (f, v) => f.Condition = v);
            context.AddChild<NChild, BHVTNode>("Node", file, getter: (f) => f!.ChildNode, setter: (f, v) => f.ChildNode = v).AddDefaultHandler<BHVTNode>();
        }
        if (context.ShowChildrenNestedReorderableUI<NChild>(false)) {
            var listCtx = context.FindParentContextByValue<List<NChild>>();
            UndoRedo.AttachCallbackToLastAction(UndoRedo.CallbackType.Both, () => listCtx?.ClearChildren());
        }
    }
}

public class NodeChildrenListHandler : ListHandlerTyped<NChild>
{
    public NodeChildrenListHandler()
    {
        Filterable = true;
    }

    protected override bool ShowContextMenuItems(UIContext context)
    {
        if (base.ShowContextMenuItems(context)) {
            return true;
        }

        if (VirtualClipboard.TryGetFromClipboard<BHVTNode>(out var newNode) && ImGui.Selectable("Paste as new child node")) {
            var nodelist = context.Get<List<NChild>>();
            var parent = context.FindValueInParentValues<BHVTNode>();
            var fileCtx = context.FindParentContextByValue<BhvtFile>();
            var file = fileCtx?.Get<BhvtFile>();
            if (parent == null) {
                Logger.Error("Parent node not found");
                return true;
            }
            if (file != null) {
                newNode.MakeUnique(file);
            }
            var newchild = new NChild();
            newchild.ChildNode = newNode;
            newNode.ParentID = parent.ID;
            var children = newNode.AllChildNodes.ToList();
            UndoRedo.RecordCallback(context, () => {
                nodelist.Add(newchild);
                file?.Nodes.AddRange(children);
            }, () => {
                nodelist.Remove(newchild);
                foreach (var ch in children) file?.Nodes.Remove(ch);
            });

            context.ClearChildren();
            fileCtx?.ClearChildren();
            return true;
        }

        return false;
    }

    protected override object? CreateNewElement(UIContext context)
    {
        var version = context.FindHandlerInParents<BHVTNode>()?.Transitions.Version ?? GameVersion.re3;
        var child = new NChild();
        child.ChildNode = WindowHandlerFactory.Instantiate<BHVTNode>(context);
        return child;
    }
}

[ObjectImguiHandler(typeof(NAction))]
public class NActionEditor : IObjectUIHandler
{
    public void OnIMGUI(UIContext context)
    {
        if (context.children.Count == 0) {
            var file = context.Get<NAction>();
            context.AddChild<NAction, uint>("Action EX", file, getter: (f) => f!.ActionEx, setter: (f, v) => f.ActionEx = v).AddDefaultHandler();
            context.AddChild<NAction, RszInstance>("Action", file, new NestedRszClassnamePickerHandler("via.behaviortree.Action"), (f) => f!.Instance, (f, v) => f.Instance = v);
        }
        context.ShowChildrenNestedReorderableUI<NAction>(false);
    }
}

[ObjectImguiHandler(typeof(NState))]
public class NStateEditor : IObjectUIHandler
{
    public void OnIMGUI(UIContext context)
    {
        if (context.children.Count == 0) {
            var file = context.Get<NState>();
            context.AddChild<NState, uint>("State EX", file, getter: (f) => f!.stateEx, setter: (f, v) => f.stateEx = v).AddDefaultHandler();
            context.AddChild<NState, uint>("Transition Map ID", file, getter: (f) => f!.transitionMapID, setter: (f, v) => f.transitionMapID = v).AddDefaultHandler();
            context.AddChild<NState, BHVTNode>("Target Node", file, new InstancePickerHandler<BHVTNode>(false, BhvtEditor.FindBhvtNodes), (f) => f!.TargetNode, (f, v) => f.TargetNode = v);
            context.AddChildContextSetter<NState, RszInstance>("Condition", file, new NestedRszClassnamePickerHandler("via.behaviortree.Condition", allowNull: true),
                (f) => f!.Condition,
                (ctx, f, newVal) => {
                    f.Condition = newVal;
                    ctx.FindParentContextByHandler<NStateEditor>()?.FindNestedChildByHandler<NestedUIHandlerStringSuffixed>()?.ClearChildren();
                });
            context.AddChild<NState, TransitionData>("Transition Parameters", file, new TransitionDataEditor(), (f) => f!.TransitionData, (f, v) => f.TransitionData = v);
            context.AddChild<NState, List<RszInstance>>("Transition Events", file, new RszListInstanceHandler("via.behaviortree.TransitionEvent"), (f) => f!.TransitionEvents);
        }
        context.ShowChildrenNestedUI();
    }
}

public class TransitionDataEditor : LazyPlainObjectHandler
{
    public TransitionDataEditor() : base(typeof(TransitionData))
    {
        _instancePicker = new InstancePickerHandler<TransitionData>(false, (ctx, force) => {
            var root = ctx.FindHandlerInParents<MotFsm2FileEditor>()?.File;
            return root?.TransitionDatas ?? [];
        }, (ctx, newInstance) => {
            var parentCtx = ctx.FindParentContextByValue<NState>();
            var parent = parentCtx?.Get<NState>();
            var root = ctx.FindHandlerInParents<MotFsm2FileEditor>()?.File;
            if (parent == null || root == null) {
                Logger.Warn("Could not find parent state or motfsm2 file, you may have found an edge case.");
                return;
            }
            var prevData = parent.TransitionData;
            UndoRedo.RecordCallback(ctx, () => {
                root.ChangeTransitionMapping(parent.transitionMapID, newInstance!.id);
                parent.TransitionData = newInstance;
                parentCtx!.ClearChildren();
            }, () => {
                root.ChangeTransitionMapping(parent.transitionMapID, prevData?.id ?? root.TransitionDatas.First().id);
                parent.TransitionData = prevData;
                parentCtx!.ClearChildren();
            });
        }, labelOverride: "Swap Transition Data");
    }

    private InstancePickerHandler<TransitionData> _instancePicker;

    protected override bool DoTreeNode(UIContext context, object instance)
    {
        if (ImguiHelpers.TreeNodeSuffix(context.label, instance.ToString() ?? string.Empty)) {
            _instancePicker.OnIMGUI(context);
            var root = context.FindHandlerInParents<MotFsm2FileEditor>()?.File;
            if (ImGui.Button("Create new transition data")) {
                var stateCtx = context.FindParentContextByValue<NState>();
                var state = stateCtx?.Get<NState>();
                if (root == null || state == null) {
                    Logger.Error("Could not find parent state or motfsm2 file");
                    return true;
                }

                var data = ((TransitionData)instance).DeepCloneGeneric<TransitionData>();
                data.id = (uint)System.Random.Shared.Next();
                var prevData = state.TransitionData;

                UndoRedo.RecordCallback(context, () => {
                    state.TransitionData = data;
                    root.TransitionDatas.Add(data);
                    root.ChangeTransitionMapping(state.transitionMapID, data.id);
                    stateCtx!.ClearChildren();
                }, () => {
                    state.TransitionData = prevData;
                    root.TransitionDatas.Remove(data);
                    root.ChangeTransitionMapping(state.transitionMapID, prevData?.id ?? root.TransitionDatas.First().id);
                    stateCtx!.ClearChildren();
                });
            }
            if (root != null) {
                var maps = root.TransitionMaps;
                var datas = root.TransitionDatas;
                var curIndex = datas?.IndexOf((TransitionData)instance);
                var usedCount = maps.Count(tm => tm.dataIndex == curIndex);
                if (usedCount > 1) {
                    ImGui.SameLine();
                    ImGui.TextColored(Colors.Note, "This transition data is used by " + usedCount + " nodes");
                }
            }
            return true;
        }
        return false;
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
        if (ImGui.DragScalarN(context.label, ImGuiDataType.U32, &nodeId, 2, 0.01f)) {
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
