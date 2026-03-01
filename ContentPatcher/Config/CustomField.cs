using System.Text.Json.Nodes;
using VYaml.Annotations;

namespace ContentPatcher;

/// <summary>
/// Describes a single custom field. One instance is created per entity and field, but shared between each entity instance.
/// </summary>
public abstract class CustomField
{
    public required string name = string.Empty;
    public string label = string.Empty;

    /// <summary>
    /// An identifier of the resource type for grouping the resources. Can be null in case the field does not have any actual instance data (only serves as a reference to a file or custom UI display). If null, object will not be diffable.
    /// </summary>
    public abstract string? ResourceIdentifier { get; }

    /// <summary>
    /// Condition for when the field is valid and displayed.
    /// </summary>
    public CustomFieldCondition? Condition { get; set; }

    /// <summary>
    /// Denotes that the field must have a value for a valid entity. The resource will be automatically created during new entity creation.
    /// </summary>
    public bool IsRequired { get; set; }

    /// <summary>
    /// Whether the field can, by its own, be considered enough to create an entity.
    /// If true, the value will be ignored if we haven't already found another important main field for this ID.
    /// </summary>
    public bool IsNotStandaloneValue { get; set; }

    /// <summary>
    /// Try and fetch a resource instance for the entity's field value from the resource manager.
    /// </summary>
    /// <param name="resources"></param>
    /// <param name="entity"></param>
    /// <param name="state"></param>
    /// <returns></returns>
    public abstract IContentResource? FetchResource(ResourceManager resources, ResourceEntity entity, ResourceState state);

    /// <summary>
    /// Apply a data JSON on top of an existing resource object or create a new resource.
    /// </summary>
    /// <returns>A resource representing the applied data. Can be the same instance that was given.</returns>
    public abstract IContentResource? ApplyValue(ContentWorkspace workspace, IContentResource? currentResource, JsonNode? data, ResourceEntity entity, ResourceState state);

    public virtual void LoadParams(string fieldName, Dictionary<string, object>? param)
    {
    }

    public virtual void EntitySetup(EntityConfig entityConfig, ContentWorkspace workspace)
    {
    }

    public override string ToString() => $"{name} [{ResourceIdentifier}]";
}

/// <summary>
/// <inheritdoc/>
/// </summary>
public abstract class CustomField<TContentType> : CustomField where TContentType : IContentResource
{
    public sealed override IContentResource? ApplyValue(ContentWorkspace workspace, IContentResource? currentResource, JsonNode? data, ResourceEntity entity, ResourceState state)
    {
        return ApplyValue(workspace, (TContentType?)currentResource, data, entity, state);
    }

    public abstract TContentType? ApplyValue(ContentWorkspace workspace, TContentType? currentResource, JsonNode? data, ResourceEntity entity, ResourceState state);
}

public interface IMainField
{
    IEnumerable<KeyValuePair<long, IContentResource>> FetchInstances(ResourceManager workspace);
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

public interface ICustomResourceField
{
    /// <summary>
    /// An identifier of the field's resource type for grouping the resources. Can be null in case the field does not have any actual instance data (only serves as a reference to a file or custom UI display). If null, object will not be diffable.
    /// </summary>
    string? ResourceIdentifier { get; }
    IEnumerable<KeyValuePair<long, IContentResource>> FetchInstances(ResourceManager workspace);
    (long id, IContentResource resource) CreateResource(ContentWorkspace workspace, ClassConfig config, ResourceEntity entity, JsonNode? initialData);
    ClassConfig CreateConfig();
}

public interface ISecondaryField
{
}

[YamlObject]
public partial class CustomFieldSerialized
{
    public string type = string.Empty;
    public string? label;
    [YamlMember("when_classname")]
    public CustomFieldClassnameCondition? whenClassname;
    [YamlMember("required")]
    public bool isRequired;
    [YamlMember("display_after")]
    public string? displayAfter;
    [YamlMember("not_standalone")]
    public bool IsNotStandalone;
    public Dictionary<string, object>? param;
}

[YamlObject]
public partial class CustomFieldClassnameCondition
{
    public string field = string.Empty;
    public string classname = string.Empty;
}