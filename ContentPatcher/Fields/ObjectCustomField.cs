using System.Text.Json.Nodes;

namespace ContentPatcher;

[ResourceField("object")]
public class ObjectCustomField : CustomField, IMainField, IDiffableField
{
    public string classname = null!;
    public override string ResourceIdentifier => classname;
    public bool? forceNested;

    public override void LoadParams(string fieldName, Dictionary<string, object>? param)
    {
        ArgumentNullException.ThrowIfNull(param, nameof(param));
        classname = (string)param["classname"];
        if (param.TryGetValue("nested", out var nested)) forceNested = (bool)nested;
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
            var resourceKey = data["$type"]?.GetValue<string>() ?? classname;
            var inst = workspace.ResourceManager.CreateEntityResource<RSZObjectResource>(entity, this, state, resourceKey);
            workspace.Diff.ApplyDiff(inst.Instance, data);
            return inst;
        }
        if (currentResource is RSZObjectResource instance) {
            workspace.Diff.ApplyDiff(instance.Instance, data);
            return instance;
        }
        return null;
    }
}
