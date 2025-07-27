using System.Text.Json.Nodes;

namespace ContentPatcher;

[ResourceField("resource")]
public class ResourceCustomField : CustomField, IMainField
{
    public string resourceType = null!;
    public override string ResourceIdentifier => resourceType;

    public override void LoadParams(string fieldName, Dictionary<string, object>? param)
    {
        ArgumentNullException.ThrowIfNull(param);
        resourceType = (string)param["type"];
    }

    public override IContentResource? FetchResource(ResourceManager resources, ResourceEntity entity, ResourceState state)
    {
        return resources.GetResourceInstance(resourceType, entity.Id, state);
    }

    public IEnumerable<KeyValuePair<long, IContentResource>> FetchInstances(ResourceManager resources)
    {
        throw new NotImplementedException();
    }

    public override IContentResource? ApplyValue(ContentWorkspace workspace, IContentResource? currentResource, JsonNode? data, ResourceEntity entity, ResourceState state)
    {
        throw new NotImplementedException();
    }
}
