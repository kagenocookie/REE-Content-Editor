using System.Numerics;
using ContentEditor.Core;

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
        ImGui.SeparatorText(text);
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

public class TooltipUIHandler(IObjectUIHandler inner, string tooltip) : IObjectUIHandler
{
    public void OnIMGUI(UIContext context)
    {
        inner.OnIMGUI(context);
        if (ImGui.IsItemHovered()) {
            ImGui.SetItemTooltip(tooltip);
        }
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

    public static readonly ReadOnlyWrapperHandler Bool = new ReadOnlyWrapperHandler(BoolFieldHandler.Instance);
    public static readonly ReadOnlyWrapperHandler Integer = new ReadOnlyWrapperHandler(new NumericFieldHandler<int>(ImGuiDataType.S32));
    public static readonly ReadOnlyWrapperHandler Short = new ReadOnlyWrapperHandler(new NumericFieldHandler<short>(ImGuiDataType.S16));
    public static readonly ReadOnlyWrapperHandler UShort = new ReadOnlyWrapperHandler(new NumericFieldHandler<ushort>(ImGuiDataType.U16));

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
public class NestedRszUIHandlerStringSuffixed(IObjectUIHandler nested) : IObjectUIHandler
{
    public void OnIMGUI(UIContext context)
    {
        var show = ImguiHelpers.TreeNodeSuffix(context.label, context.GetRaw()?.ToString() ?? string.Empty);
        RszInstanceHandler.ShowDefaultContextMenu(context);
        if (show) {
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

public class ChildrenOnlyHandler : IObjectUIHandler
{
    public static readonly ChildrenOnlyHandler Instance = new();
    public void OnIMGUI(UIContext context)
    {
        context.ShowChildrenUI();
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

public class ContextMenuUIHandler(IObjectUIHandler inner, Action<UIContext> contextCallback) : IObjectUIHandler
{
    public void OnIMGUI(UIContext context)
    {
        inner.OnIMGUI(context);
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled) && ImGui.IsKeyPressed(ImGuiKey.MouseRight)) {
            // appending extra _ctx to id so we avoid triggering any internal / disabled context menus
            ImGui.OpenPopup(context.label + "_ctx");
        }
        if (ImGui.BeginPopup(context.label + "_ctx")) {
            contextCallback.Invoke(context);
            ImGui.EndPopup();
        }
    }
}

public class ConditionalUIHandler(IObjectUIHandler inner, Func<UIContext, bool> condition) : IObjectUIHandler
{
    public void OnIMGUI(UIContext context)
    {
        if (condition.Invoke(context)) {
            inner.OnIMGUI(context);
        }
    }
}

public class InstanceTypePickerHandler<T>(Type?[] classOptions, Func<UIContext, Type, T>? factory = null, bool filterable = true) : IObjectUIHandler
{
    private Type? chosenType;
    private bool wasInit;

    public void OnIMGUI(UIContext context)
    {
        var instance = context.Get<T>();
        var labels = classOptions.Select(inst => inst?.Name ?? "<none>").ToArray();
        var curType = instance?.GetType();
        if (!wasInit) {
            chosenType = curType;
            wasInit = true;
        }

        if (filterable) {
            ImguiHelpers.FilterableCombo(context.label, labels, classOptions!, ref chosenType, ref context.Filter);
        } else {
            ImguiHelpers.ValueCombo(context.label, labels, classOptions!, ref chosenType);
        }
        if (chosenType != curType) {
            if (ImGui.Button("Change")) {
                T? newInstance;
                if (chosenType == null) {
                    newInstance = default;
                } else if (factory == null) {
                    newInstance = (T)Activator.CreateInstance(chosenType)!;
                } else {
                    newInstance = factory.Invoke(context, chosenType);
                }
                UndoRedo.RecordSet(context, newInstance, mergeMode: UndoRedoMergeMode.NeverMerge);
            }

            ImGui.SameLine();
            if (ImGui.Button("Cancel")) {
                chosenType = null;
            }
        }
    }
}
