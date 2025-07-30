using System.Collections;
using ContentEditor.Core;
using ImGuiNET;

namespace ContentEditor.App.ImguiHandling;

public class BaseListHandler : IObjectUIHandler
{
    public bool CanCreateNewElements { get; set; }

    public void OnIMGUI(UIContext context)
    {
        var list = context.Get<IList>();
        if (list == null) {
            ImGui.Text(context.label + ": NULL");
            return;
        }
        var count = list.Count;
        if (count == 0 && !CanCreateNewElements) {
            ImGui.Text(context.label + ": <empty>");
            return;
        }
        if (ImguiHelpers.TreeNodeSuffix(context.label, $"({count})")) {
            for (int i = 0; i < list.Count; ++i) {
                ImGui.PushID(i);
                if (i >= context.children.Count) {
                    context.children.Add(CreateElementContext(context, list, i));
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
                    UndoRedo.RecordListRemove(context, list, i);
                    i--;
                }
                ImGui.PopID();
            }
            if (CanCreateNewElements && ImGui.Button("Add")) {
                var newInstance = CreateNewElement(context);
                if (newInstance == null) {
                    Logger.Error("Failed to create new array element");
                } else {
                    UndoRedo.RecordListAdd(context, list, newInstance);
                }
            }
            ImGui.TreePop();
        }
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

    public ListHandler(Type elementType)
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
