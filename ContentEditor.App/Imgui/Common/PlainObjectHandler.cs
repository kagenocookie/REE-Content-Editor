using ContentEditor.Core;

namespace ContentEditor.App.ImguiHandling;

public class PlainObjectHandler : IObjectUIHandler
{
    public void OnIMGUI(UIContext context)
    {
        ImGui.Text(context.label);

        foreach (var child in context.children) {
            child.ShowUI();
        }
    }
}

public class LazyPlainObjectHandler<T>() : LazyPlainObjectHandler(typeof(T)) where T : class
{
    public bool AllowReorder { get; set; }

    protected override bool DoTreeNode(UIContext context, object instance)
    {
        var show = base.DoTreeNode(context, instance);
        if (AllowReorder) {
            if (AppImguiHelpers.DragDropReorder<T>(context, false, out var dropped)) {
                context.ClearChildren();
            }
        }
        return show;
    }
}

public class LazyPlainObjectHandler(Type type) : IObjectUIHandler
{
    public ImGuiCond AutoOpen { get; set; }
    public Type Type { get; } = type;

    protected virtual bool DoTreeNode(UIContext context, object instance)
    {
        return ImguiHelpers.TreeNodeSuffix(context.label, instance.ToString() ?? string.Empty);
    }

    public virtual void OnIMGUI(UIContext context)
    {
        var instance = context.GetRaw();
        if (instance == null) {
            ImGui.Text($"{context.label}: NULL");
            ImGui.PushID(context.label);
            if (ImguiHelpers.SameLine() && ImGui.Button("Create")) {
                context.Set(WindowHandlerFactory.Instantiate(context, Type));
                context.AddDefaultHandler();
            }
            ImGui.PopID();
            return;
        }

        if (AutoOpen != ImGuiCond.None) {
            ImGui.SetNextItemOpen(true, AutoOpen);
        }

        if (DoTreeNode(context, instance)) {
            if (context.children.Count == 0) {
                WindowHandlerFactory.SetupObjectUIContext(context, Type);
            }
            ImGui.PushID(context.label);
            foreach (var child in context.children) {
                child.ShowUI();
            }
            ImGui.PopID();
            ImGui.TreePop();
        }
    }
}

public class CopyableTreeUIHandler<T>() : LazyPlainObjectHandler(typeof(T)) where T : class
{
    protected override bool DoTreeNode(UIContext context, object instance)
    {
        return AppImguiHelpers.CopyableTreeNode<T>(context);
    }
}

public class JsonCopyableTreeUIHandler<T>() : LazyPlainObjectHandler(typeof(T)) where T : struct
{
    protected override bool DoTreeNode(UIContext context, object instance)
    {
        return AppImguiHelpers.JsonCopyableTreeNode<T>(context);
    }
}
