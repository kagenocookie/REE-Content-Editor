using ContentEditor.App.Windowing;
using ContentEditor.Editor;
using ContentPatcher;

namespace ContentEditor.App.ImguiHandling;

public interface IWorkspaceContainer
{
    ContentWorkspace Workspace { get; }
}

public static class UIContextExtensions
{
    public static ContentWorkspace? GetWorkspace(this UIContext context)
    {
        return context.root.Cast<IWorkspaceContainer>()?.Workspace ?? context.root.Cast<ContentWorkspace>();
    }

    public static IRectWindow? GetWindow(this UIContext context)
    {
        return context.Cast<IRectWindow>() ?? context.parent?.GetWindow() ?? EditorWindow.CurrentWindow;
    }

    public static EditorWindow? GetNativeWindow(this UIContext context)
    {
        return context.root.Cast<EditorWindow>();
    }

    public static IWindowHandler? GetWindowHandler(this UIContext context)
    {
        return context.Cast<IWindowHandler>() ?? context.parent?.GetWindowHandler();
    }

    public static T? FindValueInParentValues<T>(this UIContext context) where T : class
    {
        return context.Cast<T>() ?? context.parent?.FindValueInParentValues<T>();
    }

    public static T? FindHandlerInParents<T>(this UIContext context, bool ignoreSelf = false) where T : class
    {
        if (ignoreSelf) {
            return context.parent?.FindHandlerInParents<T>();
        }

        return context.uiHandler as T ?? context.parent?.FindHandlerInParents<T>();
    }

    public static UIContext? FindParentContextByHandler<T>(this UIContext context, bool ignoreSelf = false) where T : class
    {
        if (ignoreSelf) {
            return context.parent?.FindParentContextByHandler<T>();
        }
        if (context.uiHandler is T) return context;

        return context.parent?.FindParentContextByHandler<T>();
    }

    public static T? FindObjectInspectorParent<T>(this UIContext context) where T : class, IWindowHandler
    {
        var inspector = context.FindHandlerInParents<ObjectInspector>();
        return inspector?.ParentWindow as T;
    }

    public static UIContext? FindInHierarchy(this UIContext context, UIContext endParent, Func<UIContext, bool> condition, bool includeSelf = false)
    {
        var p = includeSelf ? context : context.parent;
        while (p != null && p != endParent) {
            if (condition.Invoke(p)) {
                return p;
            }
            p = p.parent;
        }

        return null;
    }

    public static ResourceEntity? GetOwnerEntity(this UIContext context)
    {
        var parent = context.parent;
        while (parent != null && parent.target is not ResourceEntity entity) {
            parent = parent.parent;
        }
        return parent?.target as ResourceEntity;
    }

    public static bool CreateEntityResource<TResourceType>(this UIContext context, ContentWorkspace workspace, CustomField field) where TResourceType : IContentResource
    {
        var entity = context.GetOwnerEntity();
        if (entity == null) {
            Logger.Error("Could not find parent entity");
            return false;
        }
        var newInstance = workspace.ResourceManager.CreateEntityResource<TResourceType>(entity, field, ResourceState.Active);
        context.Set(newInstance);
        context.children.Clear();
        return true;
    }
}
