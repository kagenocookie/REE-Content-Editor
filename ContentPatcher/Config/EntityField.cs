using System.Text.Json.Nodes;

namespace ContentPatcher;

/// <summary>
/// Describes a single custom entity field. One instance is created per entity and field, but shared between each entity instance.
/// </summary>
public sealed class EntityField
{
    public required string name = string.Empty;
    public required EntityFieldConfig config;
    public string label = string.Empty;

    public ResourceConfig Config { get; set; } = null!;

    public EntityFieldValueHandler ValueHandler { get; set; } = null!;

    /// <summary>
    /// An identifier of the resource type for grouping the resources. Can be null in case the field does not have any actual instance data (only serves as a reference to a file or custom UI display). If null, object will not be diffable.
    /// </summary>
    public string? ResourceType => ValueHandler.ResourceType;

    /// <summary>
    /// Condition for when the field is valid and displayed.
    /// </summary>
    public EntityFieldCondition? Condition { get; set; }

    /// <summary>
    /// Denotes that the field must have a value for a valid entity. The resource will be automatically created during new entity creation.
    /// </summary>
    public bool IsRequired { get; set; }

    /// <summary>
    /// Whether the field can, by its own, be considered enough to create an entity.
    /// If true, the value will be ignored if we haven't already found another important main field for this ID.
    /// </summary>
    public bool IsNotStandaloneValue { get; set; }

    public NestableFieldAccessor? IdField { get; set; }

    public override string ToString() => $"{name} [{ResourceType}]";
}

public abstract class EntityFieldValueHandler
{
    public EntityField Field { get; internal set; } = null!;

    /// <summary>
    /// An identifier of the resource type for grouping the resources. Can be null in case the field does not have any actual instance data (only serves as a reference to a file or custom UI display). If null, object will not be diffable.
    /// </summary>
    public virtual string? ResourceType => Field.Config.Type;

    /// <summary>
    /// Try and fetch a resource instance for the entity's field value from the resource manager.
    /// </summary>
    /// <param name="resources"></param>
    /// <param name="entity"></param>
    /// <param name="resourceId">The resource ID assumed by the entity. May by wrong or -1 for entity-specific fields. Will be -1 for "ID field" on entity load.</param>
    /// <param name="state"></param>
    /// <returns></returns>
    public virtual IContentResource? FetchResource(ContentWorkspace workspace, ResourceEntity entity, long resourceId, ResourceState state)
    {
        return workspace.ResourceManager.GetResourceInstance(Field.Config.Type, resourceId, state);
    }

    /// <summary>
    /// Apply a data JSON on top of an existing resource object or create a new resource.
    /// </summary>
    /// <returns>A resource representing the applied data. Can be the same instance that was given.</returns>
    public abstract IContentResource? ApplyValue(ContentWorkspace workspace, IContentResource? currentResource, JsonNode? data, ResourceEntity entity, ResourceState state);

    public virtual void LoadParams(EntityFieldConfig param)
    {
    }

    public virtual void EntitySetup(EntityConfig entityConfig, ContentWorkspace workspace)
    {
    }
}

/// <summary>
/// <inheritdoc/>
/// </summary>
public abstract class EntityFieldValueHandler<TContentType> : EntityFieldValueHandler where TContentType : IContentResource
{
    public sealed override IContentResource? ApplyValue(ContentWorkspace workspace, IContentResource? currentResource, JsonNode? data, ResourceEntity entity, ResourceState state)
    {
        return ApplyValue(workspace, (TContentType?)currentResource, data, entity, state);
    }

    public abstract TContentType? ApplyValue(ContentWorkspace workspace, TContentType? currentResource, JsonNode? data, ResourceEntity entity, ResourceState state);
}

public interface IMainField
{
}

public interface IDiffableField
{
    bool EnableDiff => false;
    JsonNode? GetDiff(ContentWorkspace workspace, IContentResource value, IContentResource baseValue)
    {
        // default diff implementation - converts both values to json and naively diffs that
        // this should probably be able to handle most cases
        var baseJson = baseValue.ToJson(workspace.Env);
        var newJson = value.ToJson(workspace.Env);
        var fieldDiff = workspace.Diff.GetHierarchicalDataDiff(baseJson, newJson);
        return fieldDiff;
    }
}

/// <summary>
/// An entity-specific resource field that's relies on an entity's data to work and can't be a standalone resource.
/// </summary>
public abstract class CustomEntityFieldHandler : EntityFieldValueHandler
{
    // /// <summary>
    // /// An identifier of the field's resource type for grouping the resources. Can be null in case the field does not have any actual instance data (only serves as a reference to a file or custom UI display). If null, object will not be diffable.
    // /// </summary>
    // string? ResourceTypeId { get; }

    public virtual ResourceHandler? CreateResourceHandler(ResourceConfig config) => null;

    /// <summary>
    /// Create an entity-specific value for this field and its ID. If the object doesn't have a custom id, it can return -1 which will then auto determine the ID based on the entity.
    /// </summary>
    public abstract (long id, IContentResource resource) CreateValue(ContentWorkspace workspace, ResourceEntity entity, JsonNode? initialData);
}

/// <summary>
/// <inheritdoc/>
/// </summary>
public abstract class CustomEntityFieldHandler<TContentType> : CustomEntityFieldHandler where TContentType : IContentResource
{
    public sealed override IContentResource? ApplyValue(ContentWorkspace workspace, IContentResource? currentResource, JsonNode? data, ResourceEntity entity, ResourceState state)
    {
        return ApplyValue(workspace, (TContentType?)currentResource, data, entity, state);
    }

    public abstract TContentType? ApplyValue(ContentWorkspace workspace, TContentType? currentResource, JsonNode? data, ResourceEntity entity, ResourceState state);
}
