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

    public static FileEditor? GetEditor(this UIContext context) => GetEditor<FileEditor>(context);

    public static FileEditor? GetEditor<T>(this UIContext context) where T : FileEditor
    {
        return context.FindHandlerInParents<T>();
    }

    public static IRectWindow? GetWindow(this UIContext context)
    {
        return context.Cast<IRectWindow>() ?? context.parent?.GetWindow() ?? EditorWindow.CurrentWindow;
    }

    public static EditorWindow? GetNativeWindow(this UIContext context)
    {
        return context.root.Cast<EditorWindow>();
    }

    public static T? FindObjectInspectorParent<T>(this UIContext context) where T : class, IWindowHandler
    {
        var inspector = context.FindHandlerInParents<ObjectInspector>();
        return inspector?.ParentWindow as T;
    }

    public static ResourceEntity? GetOwnerEntity(this UIContext context)
    {
        var parent = context.parent;
        while (parent != null && parent.target is not ResourceEntity entity) {
            parent = parent.parent;
        }
        return parent?.target as ResourceEntity;
    }

    public static bool CreateEntityResource<TResourceType>(this UIContext context, ContentWorkspace workspace, EntityField field) where TResourceType : IContentResource
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
