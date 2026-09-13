using System.Text.Json.Nodes;

namespace ContentPatcher;

[ResourceField("object")]
public class ObjectField : EntityFieldValueHandler, IMainField, IDiffableField
{
    public bool? forceNested;
    bool IDiffableField.EnableDiff => true;

    public override void LoadParams(EntityFieldConfig data)
    {
        if (data.TryGetParam<bool>("nested", out bool nested)) {
            forceNested = nested;
        }
    }

    public override IContentResource? ApplyValue(ContentWorkspace workspace, IContentResource? currentResource, JsonNode? data, ResourceEntity entity, ResourceState state)
    {
        if (data == null) {
            // TODO delete object (how?)
            return null;
        }
        if (currentResource == null) {
            var inst = workspace.ResourceManager.CreateEntityResource<RSZObjectResource>(entity, Field, state, initialData: data);
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
