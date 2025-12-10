using System.Numerics;
using ContentEditor.App.Windowing;
using ContentEditor.Core;
using ContentPatcher;
using Hexa.NET.ImNodes;
using ReeLib;
using ReeLib.Bhvt;
using ReeLib.Motfsm2;
using ReeLib.Tmlfsm2;
using ReeLib.UVar;
using ReeLib.via;

namespace ContentEditor.App.ImguiHandling;

public class UvarExpressionGraph : IWorkspaceContainer, IWindowHandler
{
    public string HandlerName => "UVar Expression Graph";
    public bool HasUnsavedChanges => false;

    public ContentWorkspace Workspace => workspace;
    public UVarFile File { get; }
    public UvarExpression Expression { get; }
    public UIContext ParentContext { get; private set; } = null!;
    public string ContextHint { get; }

    private ContentWorkspace workspace = null!;
    private UIContext context = null!;

    private Vector2 nodeSize = new Vector2(280, 140) * UI.UIScale;
    private Vector2 nodeDefaultMargins = new Vector2(140, 24) * UI.UIScale;

    private ImNodesContextPtr? nodeCtx;
    private bool arePositionsSetup;

    public UvarExpressionGraph(UVarFile file, UvarExpression expression, UIContext? parentContext, string contextHint)
    {
        File = file;
        Expression = expression;
        ParentContext = parentContext!;
        ContextHint = contextHint;
    }

    public void Init(UIContext context)
    {
        this.context = context;
        ParentContext ??= context;
        workspace = context.GetWorkspace()!;
    }

    public void OnIMGUI()
    {
        DrawFileContents();
    }

    public void OnWindow() => this.ShowDefaultWindow(context);

    protected void DrawFileContents()
    {
        ImGui.Text("Edited Variable: " + ContextHint);
        DrawNodes();
    }

    private void DrawNodes()
    {
        if (nodeCtx == null) {
            nodeCtx = UI.InitImNodeContext();
        }
        ImNodes.SetCurrentContext(nodeCtx.Value);
        ImNodes.BeginNodeEditor();
        if (!arePositionsSetup) {
            ResetNodePositions();
            arePositionsSetup = true;
        }

        ImNodes.PushAttributeFlag(ImNodesAttributeFlags.EnableLinkDetachWithDragClick);
        foreach (var node in Expression.Nodes) {
            DrawNode(node);
        }
        ImNodes.PopAttributeFlag();
        ImNodes.MiniMap(ImNodesMiniMapLocation.BottomRight);
        ImNodes.EndNodeEditor();

        int startAttr = 0, endAttr = 0, linkId = 0;
        if (ImNodes.IsLinkCreated(ref startAttr, ref endAttr)) {
            var startNode = Expression.Nodes[startAttr];
            var endNodeId = (endAttr & 0x7fff0000) >> 16;
            var inputSlotIndex = endAttr & 0xff;

            UndoRedo.RecordListAdd(ParentContext, Expression.Connections, new UvarExpression.NodeConnection() {
                inputSlot = (short)inputSlotIndex,
                outputSlot = 0,
                sourceId = startNode.nodeId,
                targetId = (short)endNodeId,
            });
        }
        if (ImNodes.IsLinkDestroyed(ref linkId)) {
            var conn = Expression.Connections[linkId];
            UndoRedo.RecordListRemove(ParentContext, Expression.Connections, Expression.Connections.IndexOf(conn));
        }
    }

    private void ResetNodePositions()
    {
        if (Expression.outputNodeId >= Expression.Nodes.Count) {
            return;
        }

        var remaining = new List<UvarNode>();
        remaining.Add(Expression.Nodes[Expression.outputNodeId]);
        var dictNodeToXY = new Dictionary<int, Int2>();
        int x = 0;
        while (remaining.Count > 0) {
            var current = remaining.ToList();
            remaining.Clear();
            var y = 0;
            foreach (var cur in current) {
                dictNodeToXY[cur.nodeId] = new Int2(x, y++);
                foreach (var conn in Expression.Connections.OrderBy(x => x.inputSlot)) {
                    if (conn.targetId != cur.nodeId) continue;

                    remaining.Add(Expression.Nodes[conn.sourceId]);
                }
            }
            x++;
        }

        foreach (var (nodeId, xy) in dictNodeToXY) {
            var sameXItems = dictNodeToXY.Where(kv => kv.Value.x == xy.x);
            var maxY = sameXItems.Any() ? sameXItems.Max(ii => ii.Value.y) : 0;
            ImNodes.SetNodeGridSpacePos(nodeId, (nodeSize + nodeDefaultMargins) * new Vector2(x - xy.x, xy.y));
        }
    }

    private unsafe void DrawNode(UvarNode node)
    {
        ImNodes.BeginNode(node.nodeId);
        ImGui.PushItemWidth(nodeSize.X);
        var contentAvail = AppImguiHelpers.NodeContentAvailX(node.nodeId);

        ImNodes.BeginNodeTitleBar();
        ImGui.Text($"{node.Name} (#{node.nodeId})");
        ImGui.SameLine();
        var isOutput = node.nodeId == Expression.outputNodeId;
        using (var _ = ImguiHelpers.ScopedIndent(contentAvail - ImGui.CalcTextSize("Result"u8).X - 32 * UI.UIScale - ImGui.GetStyle().FramePadding.X * 4)) {
            if (ImGui.Checkbox("Result"u8, ref isOutput)) {
                if (isOutput) {
                    UndoRedo.RecordCallbackSetter(ParentContext, Expression, Expression.outputNodeId, node.nodeId, (ex, id) => ex.outputNodeId = id);
                }
            }
        }
        ImNodes.EndNodeTitleBar();
        ImGui.Dummy(new Vector2(nodeSize.X, 1));
        foreach (var param in node.AllowedParameters) {
            var actualParam = node.Parameters.FirstOrDefault(pp => pp.nameHash == param.nameHash);
            if (actualParam == null) {
                ImGui.Text(param.Name);
                ImGui.SameLine();
                ImGui.Button("Create"u8);
            } else {
                switch (actualParam.type) {
                    case NodeParameter.NodeValueType.Int32: {
                        var n = actualParam.value is int ? (int)actualParam.value : 0;
                        var newN = n;
                        if (ImGui.DragScalar(param.Name, ImGuiDataType.S32, &newN, 0.01f)) {
                            UndoRedo.RecordCallbackSetter(ParentContext, actualParam, n, newN, (c, v) => c.value = v, actualParam.GetHashCode().ToString());
                        }
                        break;
                    }
                    case NodeParameter.NodeValueType.UInt32: {
                        var n = actualParam.value is uint ? (uint)actualParam.value : 0u;
                        var newN = n;
                        if (ImGui.DragScalar(param.Name, ImGuiDataType.S32, &newN, 0.01f)) {
                            UndoRedo.RecordCallbackSetter(ParentContext, actualParam, n, newN, (c, v) => c.value = v, actualParam.GetHashCode().ToString());
                        }
                        break;
                    }
                    case NodeParameter.NodeValueType.Single: {
                        var n = actualParam.value is float ? (float)actualParam.value : 0u;
                        var newN = n;
                        if (ImGui.DragScalar(param.Name, ImGuiDataType.Float, &newN, 0.01f)) {
                            UndoRedo.RecordCallbackSetter(ParentContext, actualParam, n, newN, (c, v) => c.value = v, actualParam.GetHashCode().ToString());
                        }
                        break;
                    }
                    case NodeParameter.NodeValueType.Guid: {
                        var guid = actualParam.value is Guid ? (Guid)actualParam.value : new Guid();
                        var str = guid.ToString();
                        if (ImGui.InputText(param.Name, ref str, 37, ImGuiInputTextFlags.CharsNoBlank)) {
                            if (Guid.TryParse(str, out var newguid)) {
                                UndoRedo.RecordCallbackSetter(ParentContext, actualParam, guid, newguid, (c, v) => c.value = v, actualParam.GetHashCode().ToString());
                            } else {
                                ImGui.TextColored(Colors.Error, "Invalid GUID"u8);
                            }
                        }
                        break;
                    }
                }
            }
        }

        AppImguiHelpers.NodeSeparator(node.nodeId);

        var inputNames = node.GetNodeInputs();
        for (int inputId = 0; inputId < inputNames.Length; inputId++) {
            var input = inputNames[inputId];
            var inputAttrId = node.nodeId << 16 | inputId;
            ImNodes.BeginInputAttribute(inputAttrId, ImNodesPinShape.Circle);
            ImGui.Text(input);
            ImNodes.EndInputAttribute();
            ImNodes.LinkDetachWithModifierClick();

            for (int i = 0; i < Expression.Connections.Count; i++) {
                UvarExpression.NodeConnection conn = Expression.Connections[i];
                if (conn.targetId == node.nodeId && conn.inputSlot == inputId) {
                    ImNodes.Link(i, conn.sourceId, inputAttrId);
                    break;
                }
            }
        }

        if (!isOutput) {
            ImNodes.BeginOutputAttribute(node.nodeId, ImNodesPinShape.Circle);
            using (var _ = ImguiHelpers.ScopedIndent(contentAvail - ImGui.CalcTextSize("Output"u8).X)) {
                ImGui.Text("Output"u8);
            }
            ImNodes.EndOutputAttribute();
        }

        ImGui.PopItemWidth();
        ImNodes.EndNode();
    }

    public bool RequestClose()
    {
        if (nodeCtx.HasValue) {
            ImNodes.DestroyContext(nodeCtx.Value);
        }
        return false;
    }
}