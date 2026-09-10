using System.Text.Json.Nodes;
using ReeLib;

namespace ContentPatcher;

[ResourceField("objectArray")]
public class ObjectArray : EntityFieldValueHandler, IMainField, IDiffableField, IResourceValueContainer
{
    private string? elementClassname;
    public override string ResourceTypeId => Field.Config.Type;
    bool IDiffableField.EnableDiff => true;

    public override void LoadParams(EntityFieldConfig param)
    {
        elementClassname = param.resource?.Classname;
    }

    public override IContentResource? FetchResource(ContentWorkspace workspace, ResourceEntity entity, long resourceId, ResourceState state)
    {
        var instance = workspace.ResourceManager.GetResourceInstance(Field.Config.Type, resourceId, state);
        elementClassname ??= (instance as RSZObjectListResource)?.Instances.FirstOrDefault()?.RszClass.name;
        return instance;
    }

    public override IContentResource? ApplyValue(ContentWorkspace workspace, IContentResource? currentResource, JsonNode? data, ResourceEntity entity, ResourceState state)
    {
        if (data == null) {
            // TODO delete object (how?)
            return null;
        }
        if (currentResource == null) {
            var resourceKey = (data as JsonArray)?.Count > 0 ? data[0]?["$type"]?.GetValue<string>() ?? elementClassname : elementClassname;
            var inst = workspace.ResourceManager.CreateEntityResource<RSZObjectListResource>(entity, Field, state, resourceKey);
            workspace.Diff.ApplyDiff(inst.Instances, data, resourceKey);
            return inst;
        }
        if (currentResource is RSZObjectListResource list) {
            workspace.Diff.ApplyDiff(list.Instances, data, elementClassname);
            return list;
        }
        return null;
    }

    public NestableFieldAccessor? GetAccessor(ContentWorkspace workspace, string path)
    {
        var clsAcc = NestableFieldAccessor.CreateForClass(workspace.Env.RszParser, Field.Config.RszClass, path);
        return new NestableFieldAccessor.Custom<RSZObjectListResource>(clsAcc.Field, l => clsAcc.Get(l.Instances[0])!, (l, v) => {
            foreach (var inst in l.Instances) {
                clsAcc.Set(inst, v!);
            }
        });
    }
}
