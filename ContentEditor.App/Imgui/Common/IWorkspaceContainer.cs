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

    public static T? FindClassValueInParentValues<T>(this UIContext context) where T : class
    {
        return context.Cast<T>() ?? context.parent?.FindClassValueInParentValues<T>();
    }
    public static T? FindValueInParents<T>(this UIContext context)
    {
        return context.Get<T>() ?? (context.parent == null ? default : context.parent.FindValueInParents<T>());
    }

    public static T? FindInterfaceInParentHandlers<T>(this UIContext context) where T : class
    {
        return context.uiHandler as T ?? context.parent?.FindInterfaceInParentHandlers<T>();
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
