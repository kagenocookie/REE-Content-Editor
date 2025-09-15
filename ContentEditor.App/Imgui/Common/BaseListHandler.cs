using System.Collections;
using ContentEditor.Core;
using ImGuiNET;

namespace ContentEditor.App.ImguiHandling;

public class BaseListHandler : IObjectUIHandler
{
    public bool CanCreateNewElements { get; set; }
    private readonly Type? containerType;

    public BaseListHandler() {}
    public BaseListHandler(Type? containerType) { this.containerType = containerType; }

    public void OnIMGUI(UIContext context)
    {
        var list = context.Get<IList>();
        if (list == null) {
            ImGui.Text(context.label + ": NULL");
            if (containerType != null && ImguiHelpers.SameLine() && ImGui.Button("Create")) {
                context.Set(Activator.CreateInstance(containerType));
            }
            return;
        }
        var count = list.Count;
        if (count == 0 && !CanCreateNewElements) {
            ImGui.Text(context.label + ": <empty>");
            return;
        }

        var show = ImguiHelpers.TreeNodeSuffix(context.label, $"({count})");
        if (true == (this as ITooltipHandler)?.HandleTooltip(context)) {
            // re-fetch the list in case the instance got changed
            list = context.Get<IList>();
        }
        if (show) {
            for (int i = 0; i < list.Count; ++i) {
                ImGui.PushID(i);
                while (i >= context.children.Count) {
                    var nextIndex = context.children.Count;
                    context.children.Add(CreateElementContext(context, list, nextIndex));
                }
                var child = context.children[i];
                var remove = false;
                if (CanCreateNewElements) {
                    remove = ImGui.Button("X");
                    ImGui.SameLine();
                }
                child.ShowUI();
                if (remove) {
                    int fixed_i = i;
                    var item = list[i];
                    RemoveFromList(context, list, i);
                    list = context.Get<IList>();
                    i--;
                }
                ImGui.PopID();
            }
            if (CanCreateNewElements && ImGui.Button("Add")) {
                var newInstance = CreateNewElement(context);
                if (newInstance == null) {
                    Logger.Error("Failed to create new array element");
                } else {
                    ExpandList(context, list, newInstance);
                    list = context.Get<IList>();
                }
            }
            ImGui.TreePop();
        }
    }

    protected virtual void RemoveFromList(UIContext context, IList list, int i)
    {
        UndoRedo.RecordListRemove(context, list, i);
    }

    protected virtual void ExpandList(UIContext context, IList list, object newInstance)
    {
        UndoRedo.RecordListAdd(context, list, newInstance);
    }

    protected virtual UIContext CreateElementContext(UIContext context, IList list, int elementIndex)
    {
        var ctx = WindowHandlerFactory.CreateListElementContext(context, elementIndex);
        ctx.uiHandler = new UnsupportedHandler();
        return ctx;
    }

    protected virtual object? CreateNewElement(UIContext context)
    {
        var list = context.Get<IList>();
        if (list.Count == 0 || list[0] == null) return null;
        return Activator.CreateInstance(list[0]!.GetType());
    }
}

public class ListHandler : BaseListHandler
{
    private readonly Type elementType;

    public ListHandler(Type elementType, Type? containerType = null) : base(containerType)
    {
        this.elementType = elementType;
    }

    protected override UIContext CreateElementContext(UIContext context, IList list, int elementIndex)
    {
        var ctx = WindowHandlerFactory.CreateListElementContext(context, elementIndex);
        WindowHandlerFactory.SetupArrayElementHandler(ctx, elementType);
        return ctx;
    }

    protected override object CreateNewElement(UIContext context)
    {
        return elementType == typeof(string) ? string.Empty : Activator.CreateInstance(elementType)!;
    }
}

public class ListHandler<T> : ListHandler where T : class
{
    public Func<T>? Instantiator;
    public ListHandler(Func<T>? instantiator) : base(typeof(T))
    {
        Instantiator = instantiator;
        base.CanCreateNewElements = instantiator != null;
    }

    protected override object CreateNewElement(UIContext context) => Instantiator?.Invoke() ?? base.CreateNewElement(context);
}

public class ArrayHandler : BaseListHandler
{
    private readonly Type elementType;

    public ArrayHandler(Type elementType)
    {
        this.elementType = elementType;
        CanCreateNewElements = false;
    }

    protected override UIContext CreateElementContext(UIContext context, IList list, int elementIndex)
    {
        var ctx = WindowHandlerFactory.CreateListElementContext(context, elementIndex);
        WindowHandlerFactory.SetupArrayElementHandler(ctx, elementType);
        return ctx;
    }
}

public class ResizableArrayHandler : BaseListHandler
{
    private readonly Type elementType;

    public ResizableArrayHandler(Type elementType)
    {
        this.elementType = elementType;
        CanCreateNewElements = true;
    }

    protected override UIContext CreateElementContext(UIContext context, IList list, int elementIndex)
    {
        var ctx = WindowHandlerFactory.CreateListElementContext(context, elementIndex);
        WindowHandlerFactory.SetupArrayElementHandler(ctx, elementType);
        return ctx;
    }

    protected override void ExpandList(UIContext context, IList list, object newInstance)
    {
        var newArray = Array.CreateInstance(elementType, list.Count + 1);
        list.CopyTo(newArray, 0);
        newArray.SetValue(newInstance, list.Count);
        UndoRedo.RecordSet(context, newArray, mergeMode: UndoRedoMergeMode.NeverMerge);
        context.ClearChildren();
    }

    protected override void RemoveFromList(UIContext context, IList list, int i)
    {
        var newArray = Array.CreateInstance(elementType, list.Count - 1);
        var prevArray = (Array)list;
        if (i > 0) {
            Array.Copy(prevArray, 0, newArray, 0, i);
        }
        if (i < prevArray.Length - 1) {
            Array.Copy(prevArray, i + 1, newArray, i, prevArray.Length - 1 - i);
        }

        UndoRedo.RecordSet(context, newArray, mergeMode: UndoRedoMergeMode.NeverMerge);
        context.ClearChildren();
    }
}
