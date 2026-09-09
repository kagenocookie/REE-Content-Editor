using System.Text.Json.Nodes;

namespace ContentPatcher;

[ResourceField("object")]
public class ObjectField : EntityFieldValueHandler, IMainField, IDiffableField, IResourceValueContainer
{
    public string classname = null!;
    public override string ResourceTypeId => classname;
    public bool? forceNested;
    bool IDiffableField.EnableDiff => true;

    public override void LoadParams(EntityFieldConfig data)
    {
        classname = data.RequireResourceSettings.Classname!;
        if (data.TryGetParam<bool>("nested", out bool nested)) {
            forceNested = nested;
        }
    }

    public NestableFieldAccessor? GetAccessor(ContentWorkspace workspace, string path)
        => NestableFieldAccessor.CreateForClass(workspace.Env.RszParser, Field.Resource.RszClass, path);

    public override IContentResource? FetchResource(ContentWorkspace workspace, ResourceEntity entity, long resourceId, ResourceState state)
    {
        return workspace.ResourceManager.GetResourceInstance(Field.Resource.Type, resourceId, state);
    }

    public override IContentResource? ApplyValue(ContentWorkspace workspace, IContentResource? currentResource, JsonNode? data, ResourceEntity entity, ResourceState state)
    {
        if (data == null) {
            // TODO delete object (how?)
            return null;
        }
        if (currentResource == null) {
            var resourceKey = data["$type"]?.GetValue<string>() ?? classname;
            var inst = workspace.ResourceManager.CreateEntityResource<RSZObjectResource>(entity, Field, state, resourceKey);
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
