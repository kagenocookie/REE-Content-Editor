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

    public static EntityField? GetEntityField(this UIContext context)
    {
        var entity = context.EntityParams?.Entity ?? context.GetOwnerEntity();
        var field = context.EntityParams?.EntityField;
        if (entity == null || field == null) {
            return null;
        }
        return (entity as ResourceEntity)?.Config.GetField(field);
    }

    public static T? GetEntityField<T>(this UIContext context) where T : EntityFieldValueHandler
    {
        var entity = context.EntityParams?.Entity ?? context.GetOwnerEntity();
        var field = context.EntityParams?.EntityField;
        if (entity == null || field == null) {
            return null;
        }
        return (entity as ResourceEntity)?.Config.GetField(field)?.ValueHandler as T;
    }

    public static IContentResource? CreateEntityResource(this UIContext context, ContentWorkspace workspace, EntityField? field, string? resourceType)
    {
        // TODO: this one probably shouldn't be needed anymore
        // we have a dedicated NullResourceHandler that should be able to handle any resource
        if (field == null) {
            Logger.Error("Could not determine entity field");
            return null;
        }
        var entity = context.GetOwnerEntity();
        if (entity == null) {
            Logger.Error("Could not find parent entity");
            return null;
        }
        var newInstance = workspace.ResourceManager.CreateEntityResource(entity, field, ResourceState.Active, resourceType);
        context.Set(newInstance);
        context.children.Clear();
        return newInstance;
    }
}
