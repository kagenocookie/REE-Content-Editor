using System.Numerics;
using ContentEditor.Core;

namespace ContentEditor.App.ImguiHandling;

public abstract class TreeHandler<TBaseNode> : IObjectUIHandler where TBaseNode : class
{
    internal int InheritedDepth { get; set; }

    private readonly Stack<(UIContext ctx, int depth)> _stack = new();
    private readonly List<TBaseNode> _roots = new();
    private readonly List<TBaseNode> _selectedHierarchy = new();

    protected abstract IEnumerable<TBaseNode> GetRootChildren(UIContext context);
    protected abstract IEnumerable<TBaseNode> GetChildren(TBaseNode node);
    protected virtual bool IsExpandable(TBaseNode node) => GetChildren(node).Any();

    /// <summary>
    /// Get a list of nodes in the tree hierarchy for the selected node ordered from leaf to root.
    /// </summary>
    protected virtual IEnumerable<TBaseNode> GetSelectedItemHierarchy(UIContext context) => [];

    protected abstract void ShowPrefixColumn(TBaseNode node, UIContext context);

    protected virtual void ShowNodeDisabled(TBaseNode node, UIContext context)
    {
        ImGui.TextColored(Colors.Faded, GetNodeText(node));
    }
    protected virtual string GetNodeText(TBaseNode node)
    {
        var icon = AppIcons.GetIcon(node);
        if (icon == '\0') {
            return node.ToString() ?? node.GetType().Name;
        } else {
            return $"{icon} {node}";
        }
    }
    protected virtual void ShowNode(TBaseNode node, UIContext context)
    {
        if (context.uiHandler != null) {
            context.ShowUI();
        }
    }
    protected int CurrentIndent { get; private set; }
    protected float prefixColWidth;

    public void OnIMGUI(UIContext context)
    {
        _roots.Clear();
        _roots.AddRange(GetRootChildren(context));
        _selectedHierarchy.Clear();
        _selectedHierarchy.AddRange(GetSelectedItemHierarchy(context));
        _stack.Clear();
        int i = 0;
        foreach (var item in _roots) {
            if (context.children.Count <= i) {
                var child = context.AddChild($"{item}##{i}", item);
                SetupNodeItemContext(child, item);
            } else if (context.children[i].Get<TBaseNode>() != item) {
                context.children.RemoveAtAfter(i);
                var child = context.AddChild($"{item}##{i}", item);
                SetupNodeItemContext(child, item);
            }
            i++;
        }
        context.children.RemoveAtAfter(i);

        foreach (var child in context.children.Reverse<UIContext>()) {
            _stack.Push((child, 0));
        }

        int handledCount = 0;
        var indent = ImGui.GetStyle().IndentSpacing;

        var sizeX = ImGui.GetContentRegionAvail().X;
        var framePadding = ImGui.GetStyle().FramePadding;
        var lineHeight = UI.FontSize + framePadding.Y * 2;
        var buttonRect = new Vector2(lineHeight + framePadding.Y * 2);
        var fixedLeftMargin = ImGui.GetStyle().WindowPadding.X;
        var prefixStartIndent = ImGui.GetStyle().WindowPadding.X;

        var filter = context.FindHandlerInParents<IFilterRoot>();

        while (_stack.TryPop(out var next)) {
            var (ctx, depth) = next;
            var node = ctx.Get<TBaseNode>();

            var filteredChild = false;
            if (filter?.HasFilterActive == true && !filter.IsMatch(node)) {
                filteredChild = true;
                // check if any children match, else continue
                bool CheckAnyChildMatch(TBaseNode node, IFilterRoot filter)
                {
                    foreach (var c in GetChildren(node)) {
                        if (filter.IsMatch(c) || CheckAnyChildMatch(c, filter)) return true;
                    }
                    return false;
                }
                if (!CheckAnyChildMatch(node, filter)) {
                    continue;
                }
            }

            // undo the default frame padding to give the appearance of a zero-spacing table
            if (handledCount != 0) ImGui.SetCursorPosY(ImGui.GetCursorPosY() - framePadding.Y);

            var pos = ImGui.GetCursorScreenPos();
            if (handledCount++ % 2 != 0) {
                ImGui.GetWindowDrawList().AddRectFilled(pos, new System.Numerics.Vector2(pos.X + sizeX, pos.Y + lineHeight), ImguiHelpers.GetColorU32(ImGuiCol.TableRowBgAlt));
            }
            if (_selectedHierarchy.Contains(node)) {
                // would we want a dedicated selected / selected-hierarchy color?
                var selectedColor = ImguiHelpers.GetColor(ImGuiCol.FrameBgActive);
                if (_selectedHierarchy[0] == node) {
                    ImGui.GetWindowDrawList().AddRectFilled(pos, new System.Numerics.Vector2(pos.X + sizeX, pos.Y + lineHeight), ImGui.ColorConvertFloat4ToU32(selectedColor));
                } else {
                    ImGui.GetWindowDrawList().AddRectFilled(pos, new System.Numerics.Vector2(pos.X + sizeX, pos.Y + lineHeight), ImGui.ColorConvertFloat4ToU32(selectedColor with { W = selectedColor.W * 0.2f }));
                }
            }

            ImGui.PushID(handledCount);
            ImGui.SetCursorPosX(prefixStartIndent);
            ShowPrefixColumn(node, ctx);
            var prefixWidth = ImGui.GetCursorScreenPos().X - pos.X;
            if (prefixWidth != prefixColWidth) {
                prefixColWidth = prefixWidth;
            }

            ImGui.Dummy(new Vector2(0, lineHeight));
            ImGui.SameLine();
            var contentOffset = indent * (depth + InheritedDepth) + fixedLeftMargin + prefixColWidth;
            ImGui.SetCursorPosX(contentOffset);
            CurrentIndent = depth + InheritedDepth;
            var showChildren = ctx.StateBool;
            if (IsExpandable(node)) {
                if (ImGui.ArrowButton($"arrow", showChildren ? ImGuiDir.Down : ImGuiDir.Right)) {
                    showChildren = ctx.StateBool = !showChildren;
                }
                ImGui.SameLine();
            } else {
                ImGui.SetCursorPosX(contentOffset + buttonRect.X);
                if (prefixColWidth == 0) {
                    // note: a bit of a hack to ensure we get consistent Y padding whether or not the scene is enabled
                    // the assumption is that if there's any prefix UI, it's got at least a button, or is otherwise consistently sized
                    ImGui.SetCursorPosY(ImGui.GetCursorPosY() + framePadding.Y);
                }
            }

            if (filteredChild) {
                ShowNodeDisabled(node, ctx);
            } else {
                ShowNode(node, ctx);
            }
            ImGui.PopID();
            if (!showChildren) continue;

            i = 0;
            // ensure the child contexts always match the nodes, delete anything if there's changes
            foreach (var c in GetChildren(node)) {
                if (ctx.children.Count <= i) {
                    var child = ctx.AddChild($"{c}##{i}", c);
                    SetupNodeItemContext(child, c);
                } else if (ctx.children[i].Get<TBaseNode>() != c) {
                    ctx.children.RemoveAtAfter(i);
                    var child = ctx.AddChild($"{c}##{i}", c);
                    SetupNodeItemContext(child, c);
                }
                i++;
            }
            ctx.children.RemoveAtAfter(i);

            foreach (var child in ctx.children.Reverse<UIContext>()) {
                _stack.Push((child, depth + 1));
            }
        }

        ImGui.Indent(indent * (InheritedDepth));
        DrawEndTree(context);
        ImGui.Unindent(indent * (InheritedDepth));
    }

    protected virtual void DrawEndTree(UIContext context) { }

    protected virtual void SetupNodeItemContext(UIContext context, TBaseNode node) { }
}
