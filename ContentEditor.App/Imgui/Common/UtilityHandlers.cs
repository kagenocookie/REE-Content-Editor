using ContentEditor.Core;
using ImGuiNET;

namespace ContentEditor.App.ImguiHandling;

public class NestedUIHandler(IObjectUIHandler inner) : IObjectUIHandler
{
    public void OnIMGUI(UIContext context)
    {
        if (ImGui.TreeNode(context.label)) {
            inner.OnIMGUI(context);
            ImGui.TreePop();
        }
    }
}

public class NullUIHandler : IObjectUIHandler
{
    public static readonly NullUIHandler Instance = new();
    public void OnIMGUI(UIContext context)
    {
    }
}

public class FullWindowWidthUIHandler(int offset, IObjectUIHandler inner) : IObjectUIHandler
{
    public void OnIMGUI(UIContext context)
    {
        ImGui.PushItemWidth(ImGui.GetWindowWidth() + offset);
        inner.OnIMGUI(context);
        ImGui.PopItemWidth();
    }
}

public class TextHeaderUIHandler(string text, IObjectUIHandler inner) : IObjectUIHandler
{
    public void OnIMGUI(UIContext context)
    {
        ImGui.Text(text);
        inner.OnIMGUI(context);
    }
}

public class BoxedUIHandler(IObjectUIHandler inner) : IObjectUIHandler
{
    public void OnIMGUI(UIContext context)
    {
        ImguiHelpers.BeginRect();
        inner.OnIMGUI(context);
        ImguiHelpers.EndRect(2);
    }
}

public class ValueChangeCallbackUIHandler(IObjectUIHandler inner, Action<UIContext, object?, object?> callback) : IObjectUIHandler
{
    public void OnIMGUI(UIContext context)
    {
        var prev = context.GetRaw();
        inner.OnIMGUI(context);
        var next = context.GetRaw();
        if ((next == null) != (prev == null) || (next != null && !next.Equals(prev)) || (prev != null && !prev.Equals(next))) {
            callback.Invoke(context, prev, next);
        }
    }
}

public class ReadOnlyWrapperHandler : IObjectUIHandler
{
    public IObjectUIHandler next;

    public ReadOnlyWrapperHandler(IObjectUIHandler next)
    {
        this.next = next;
    }

    public void OnIMGUI(UIContext container)
    {
        ImGui.BeginDisabled();
        next.OnIMGUI(container);
        ImGui.EndDisabled();
    }
}
public class NestedUIHandlerStringSuffixed(IObjectUIHandler nested) : IObjectUIHandler
{
    public void OnIMGUI(UIContext context)
    {
        if (ImguiHelpers.TreeNodeSuffix(context.label, context.GetRaw()?.ToString() ?? string.Empty)) {
            nested.OnIMGUI(context);
            ImGui.TreePop();
        }
    }
}
public class SameLineHandler : IObjectUIHandler
{
    public static readonly SameLineHandler Instance = new();
    public void OnIMGUI(UIContext context)
    {
        ImGui.SameLine();
    }
}

public abstract class TreeContextUIHandler(IObjectUIHandler nested) : IObjectUIHandler
{
    public void OnIMGUI(UIContext context)
    {
        var show = ImguiHelpers.TreeNodeSuffix(context.label, context.GetRaw()?.ToString() ?? string.Empty);
        HandleContextMenu(context);
        if (show) {
            nested.OnIMGUI(context);
            ImGui.TreePop();
        }
    }

    protected abstract void HandleContextMenu(UIContext context);
}
