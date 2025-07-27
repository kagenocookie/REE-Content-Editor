using System.Text.Json.Nodes;

namespace ContentPatcher;

[ResourceField("objectArray")]
public class RszArrayCustomField : CustomField, IMainField, IDiffableField
{
    public string classname = null!;
    public override string ResourceIdentifier => classname;

    public override void LoadParams(string fieldName, Dictionary<string, object>? param)
    {
        ArgumentNullException.ThrowIfNull(param);
        classname = (string)param["classname"];
    }

    public IEnumerable<KeyValuePair<long, IContentResource>> FetchInstances(ResourceManager resources)
    {
        return resources.GetResourceInstances(classname);
    }

    public override IContentResource? FetchResource(ResourceManager resources, ResourceEntity entity, ResourceState state)
    {
        return resources.GetResourceInstance(classname, entity.Id, state);
    }

    public override IContentResource? ApplyValue(ContentWorkspace workspace, IContentResource? currentResource, JsonNode? data, ResourceEntity entity, ResourceState state)
    {
        if (data == null) {
            // TODO delete object (how?)
            return null;
        }
        if (currentResource == null) {
            var resourceKey = (data as JsonArray)?.Count > 0 ? data[0]?["$type"]?.GetValue<string>() ?? classname : classname;
            var inst = workspace.ResourceManager.CreateEntityResource<RSZObjectListResource>(entity, this, state, resourceKey);
            workspace.Diff.ApplyDiff(inst.Instances, data, resourceKey);
            return inst;
        }
        if (currentResource is RSZObjectListResource list) {
            workspace.Diff.ApplyDiff(list.Instances, data, classname);
            return list;
        }
        return null;
    }
}
