using System.Collections;
using ContentEditor.Core;
using ImGuiNET;

namespace ContentEditor.App.ImguiHandling;

public class BaseListHandler : IObjectUIHandler
{
    public bool CanCreateNewElements { get; set; }
    public bool Filterable { get; set; }

    private readonly Type? containerType;

    public BaseListHandler() {}
    public BaseListHandler(Type? containerType) { this.containerType = containerType; }

    protected virtual object? CreateNewListInstance() => containerType == null ? null : Activator.CreateInstance(containerType);

    protected virtual bool MatchesFilter(object? obj, string filter)
    {
        Logger.Debug("Missing list filter implementation");
        return true;
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
        if (count == 0 && !CanCreateNewElements) {
            ImGui.Text(context.label + ": <empty>");
            return;
        }

        var show = ImguiHelpers.TreeNodeSuffix(context.label, $"({count})");
        if (this is IContextMenuHandler ctxm && ctxm.AllowContextMenu) {
            if (ImGui.BeginPopupContextItem(context.label)) {
                if (ctxm.ShowContextMenuItems(context)) {
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
            if (Filterable) {
                context.state ??= "";
                ImGui.InputText("Filter", ref context.state, 200);
            }
            for (int i = 0; i < list.Count; ++i) {
                if (Filterable && !string.IsNullOrEmpty(context.state) && !MatchesFilter(list[i], context.state)) {
                    continue;
                }

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

        return Activator.CreateInstance(elementType);
    }
}

public class ListHandler : BaseListHandler
{
    private readonly Type elementType;

    public ListHandler(Type elementType, Type? containerType = null) : base(containerType)
    {
        this.elementType = elementType;
        CanCreateNewElements = !elementType.IsAbstract;
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
        return Activator.CreateInstance(elementType)!;
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

    protected override object? CreateNewElement(UIContext context) => Instantiator?.Invoke() ?? base.CreateNewElement(context);
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

    public ResizableArrayHandler(Type elementType) : base(elementType.MakeArrayType())
    {
        this.elementType = elementType;
        CanCreateNewElements = true;
    }

    protected override object? CreateNewListInstance()
    {
        return Array.CreateInstance(elementType, 0);
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
