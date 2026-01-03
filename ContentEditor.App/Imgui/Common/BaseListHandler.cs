using System.Collections;
using ContentEditor.Core;

namespace ContentEditor.App.ImguiHandling;

public class BaseListHandler : IObjectUIHandler
{
    public bool CanCreateRemoveElements { get; set; }
    public bool Filterable { get; set; }
    public bool AllowContextMenu { get; set; } = true;

    private readonly Type? containerType;

    public BaseListHandler() {}
    public BaseListHandler(Type? containerType) { this.containerType = containerType; }

    protected virtual object? CreateNewListInstance() => containerType == null ? null : Activator.CreateInstance(containerType);

    protected virtual bool MatchesFilter(object? obj, string filter)
    {
        if (filter[0] == '!') {
            return obj?.ToString()?.Contains(filter.Substring(1), StringComparison.InvariantCultureIgnoreCase) != true;
        }
        return obj?.ToString()?.Contains(filter, StringComparison.InvariantCultureIgnoreCase) == true;
    }

    protected virtual bool ShowContextMenuItems(UIContext context)
    {
        if (ImGui.Selectable("Clear")) {
            UndoRedo.RecordListClear(context, context.Get<IList>());
            return true;
        }
        return false;
    }

    public void OnIMGUI(UIContext context)
    {
        var list = context.Get<IList>();
        if (list == null) {
            ImGui.Text(context.label + ": NULL");
            if (containerType != null && ImguiHelpers.SameLine() && ImGui.Button("Create")) {
                context.Set(CreateNewListInstance());
            }
            return;
        }
        var count = list.Count;
        if (count == 0 && !CanCreateRemoveElements) {
            ImGui.Text(context.label + ": <empty>");
            return;
        }

        var show = ImguiHelpers.TreeNodeSuffix(context.label, $"({count})");
        if (AllowContextMenu) {
            if (ImGui.BeginPopupContextItem(context.label)) {
                if (ShowContextMenuItems(context)) {
                    ImGui.CloseCurrentPopup();
                }
                ImGui.EndPopup();
            }
        }
        if (true == (this as ITooltipHandler)?.HandleTooltip(context)) {
            // re-fetch the list in case the instance got changed
            list = context.Get<IList>();
        }
        if (show) {
            string? filter = null;
            if (Filterable) {
                ImGui.InputText("Filter", ref context.Filter, 200);
                ImGui.Spacing();
                filter = context.Filter;
            }
            for (int i = 0; i < list.Count; ++i) {
                if (Filterable && !string.IsNullOrEmpty(filter) && !MatchesFilter(list[i], filter)) {
                    continue;
                }

                ImGui.PushID(i);
                while (i >= context.children.Count) {
                    var nextIndex = context.children.Count;
                    context.children.Add(CreateElementContext(context, list, nextIndex));
                }
                var child = context.children[i];
                var remove = false;
                if (CanCreateRemoveElements) {
                    remove = ImGui.Button($"{AppIcons.SI_GenericClose}");
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
            if (CanCreateRemoveElements && ImGui.Button("Add")) {
                try {
                    var newInstance = CreateNewElement(context);
                    if (newInstance != null) {
                        ExpandList(context, list, newInstance);
                        list = context.Get<IList>();
                    }
                } catch (Exception e) {
                    Logger.Error("Failed to create new array element: " + e.Message);
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
        Type elementType;
        if (list.GetType().IsArray) {
            elementType = list.GetType().GetElementType()!;
        } else if (list.Count == 0 || list[0] == null) {
            throw new Exception("Unknown list element type");
        } else {
            elementType = list[0]!.GetType();
        }

        return WindowHandlerFactory.Instantiate(context, elementType);
    }
}

public class ListHandler : BaseListHandler
{
    private readonly Type elementType;

    public ListHandler(Type elementType, Type? containerType = null) : base(containerType)
    {
        this.elementType = elementType;
        CanCreateRemoveElements = !elementType.IsAbstract;
    }

    protected override UIContext CreateElementContext(UIContext context, IList list, int elementIndex)
    {
        var ctx = WindowHandlerFactory.CreateListElementContext(context, elementIndex);
        WindowHandlerFactory.SetupArrayElementHandler(ctx, elementType);
        return ctx;
    }

    protected override object? CreateNewElement(UIContext context)
    {
        if (elementType == typeof(string)) return string.Empty;
        if (elementType.IsAbstract) throw new Exception($"Type {elementType.Name} is abstract");
        return WindowHandlerFactory.Instantiate(context, elementType)!;
    }
}

public class ListHandlerTyped<T> : ListHandler where T : class
{
    public ListHandlerTyped() : base(typeof(T), typeof(List<T>)) { }
}

public class ListHandler<T> : ListHandler where T : class
{
    public Func<T>? Instantiator;
    public ListHandler(Func<T>? instantiator) : base(typeof(T))
    {
        Instantiator = instantiator;
        CanCreateRemoveElements = instantiator != null;
    }

    protected override object? CreateNewElement(UIContext context) => Instantiator?.Invoke() ?? base.CreateNewElement(context);
}

public class ArrayHandler : BaseListHandler
{
    private readonly Type elementType;

    public ArrayHandler(Type elementType)
    {
        this.elementType = elementType;
        CanCreateRemoveElements = false;
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

    public ResizableArrayHandler(Type elementType, Type? arrayType = null) : base(arrayType ?? elementType.MakeArrayType())
    {
        this.elementType = elementType;
        CanCreateRemoveElements = true;
    }

    protected override object? CreateNewListInstance()
    {
        return Array.CreateInstance(elementType, 0);
    }

    protected override bool ShowContextMenuItems(UIContext context)
    {
        if (ImGui.Selectable("Clear")) {
            UndoRedo.RecordSet(context, Array.CreateInstance(elementType, 0));
            return true;
        }
        return false;
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
