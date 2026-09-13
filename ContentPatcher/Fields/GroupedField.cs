using System.Text.Json.Nodes;

namespace ContentPatcher;

[ResourceField("group")]
public class GroupedField : EntityFieldValueHandler, IMainField, IDiffableField
{
    bool IDiffableField.EnableDiff => true;
    private Dictionary<string, EntityFieldValueHandler>? subhandlers;

    public override IContentResource? ApplyValue(ContentWorkspace workspace, IContentResource? currentResource, JsonNode? data, ResourceEntity entity, ResourceState state)
    {
        if (data == null) {
            // TODO delete object (how?)
            return null;
        }
        if (currentResource == null) {
            currentResource = workspace.ResourceManager.CreateEntityResource<GroupedResource>(entity, Field, state, initialData: data);
        }

        if (subhandlers == null) {
            subhandlers = new Dictionary<string, EntityFieldValueHandler>();
            foreach (var (key, sub) in Field.Config.Subtypes!) {
                subhandlers[key] = sub.Resource.CreateValueHandler(Field);
            }
        }

        if (currentResource is GroupedResource instance) {
            foreach (var (key, sub) in Field.Config.Subtypes!) {
                var subresource = instance.Get(key);
                var handler = subhandlers[key];
                instance.Set(key, handler.ApplyValue(workspace, subresource, data?[key], entity, state));
            }
            return instance;
        }

        return null;
    }
}
