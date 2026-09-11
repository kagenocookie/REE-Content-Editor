using System.Text.Json.Nodes;

namespace ContentPatcher;

[ResourceField("object")]
public class ObjectField : EntityFieldValueHandler, IMainField, IDiffableField, IResourceValueContainer
{
    public bool? forceNested;
    bool IDiffableField.EnableDiff => true;

    public override void LoadParams(EntityFieldConfig data)
    {
        if (data.TryGetParam<bool>("nested", out bool nested)) {
            forceNested = nested;
        }
    }

    public NestableFieldAccessor? GetAccessor(ContentWorkspace workspace, string path)
        => NestableFieldAccessor.CreateForClass(workspace.Env.RszParser, Field.Config.RszClass, path);

    public override IContentResource? ApplyValue(ContentWorkspace workspace, IContentResource? currentResource, JsonNode? data, ResourceEntity entity, ResourceState state)
    {
        if (data == null) {
            // TODO delete object (how?)
            return null;
        }
        if (currentResource == null) {
            var resourceKey = data["$type"]?.GetValue<string>() ?? Field.Config.RszClassRequired.name;
            var inst = workspace.ResourceManager.CreateEntityResource<RSZObjectResource>(entity, Field, state);
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
