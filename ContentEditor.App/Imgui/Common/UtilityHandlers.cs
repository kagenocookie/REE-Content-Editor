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
