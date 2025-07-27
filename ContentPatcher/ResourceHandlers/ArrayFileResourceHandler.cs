using System.Text.Json.Nodes;
using ReeLib;

namespace ContentPatcher;

[ResourcePatcher("array-file", nameof(Deserialize))]
public class ArrayFileResourceHandler : ResourceHandler
{
    public static ArrayFileResourceHandler Deserialize(string resourceKey, Dictionary<string, object> data)
    {
        return new ArrayFileResourceHandler() { file = (string)data["file"], ResourceKey = resourceKey };
    }

    public override (long id, IContentResource resource) CreateResource(ContentWorkspace workspace, ClassConfig config, ResourceEntity entity, JsonNode? initialData)
    {
        if (config.SubIDFields?.Length > 0) {
            var list = new RSZObjectListResource(ResourceKey, file!);
            workspace.Diff.ApplyDiff(list.Instances, initialData, ResourceKey);
            return (entity.Id, list);
        } else {
            var inst = RszInstance.CreateInstance(workspace.Env.RszParser, workspace.Env.RszParser.GetRSZClass(ResourceKey)!);
            workspace.Diff.ApplyDiff(inst, initialData);
            if (config.IDFields?.Length == 1) {
                var idField = inst.RszClass.fields[config.IDFields[0]];
                var fieldType = RszInstance.RszFieldTypeToCSharpType(idField.type);
                inst.SetFieldValue(config.IDFields[0], Convert.ChangeType(entity.Id, fieldType));
            } else {
                throw new NotImplementedException("Unsupported rsz object id combination");
            }
            return (entity.Id, new RSZObjectResource(inst, file!));
        }
    }

    public override void ReadResources(ContentWorkspace workspace, ClassConfig config, Dictionary<long, IContentResource> dict)
    {
        var items = GetObjectList(workspace, false);
        if (items.Count == 0) return;

        var idGenerator = IDGenerator.GetGenerator((RszInstance)items[0], config.IDFields!);
        var subIdGenerator = config.SubIDFields?.Length > 0 ? IDGenerator.GetGenerator((RszInstance)items[0], config.SubIDFields) : null;
        foreach (var item in items.OfType<RszInstance>()) {
            var id = idGenerator.GetID(item, config.IDFields!);
            if (subIdGenerator != null) {
                if (dict.TryGetValue(id, out var list) && list is RSZObjectListResource objlist) {
                    objlist.Instances.Add(item);
                } else {
                    dict[id] = new RSZObjectListResource(item, file!);
                }
            } else {
                dict[id] = new RSZObjectResource(item, file!);
            }
        }
    }

    public override void ModifyResources(ContentWorkspace workspace, ClassConfig config, IEnumerable<KeyValuePair<long, IContentResource>> resources)
    {
        var items = GetObjectList(workspace, true);
        if (items.Count == 0) return;
        // the current expected behavior is that we read _all_ the resources in ReadResources, meaning we can just clear and re-add everything here

        var dict = new Dictionary<long, RszInstance>();
        var isarray = config.SubIDFields?.Length > 0;
        if (isarray) {
            items.Clear();
            foreach (var (id, resource) in resources) {
                var list = (RSZObjectListResource)resource;
                foreach (var item in list.Instances) {
                    items.Add(item);
                }
            }
        } else {
            items.Clear();
            foreach (var (_, item) in resources) {
                items.Add(((RSZObjectResource)item).Instance);
            }
        }
    }

    private IList<object> GetObjectList(ContentWorkspace workspace, bool modify)
    {
        UserFile userfile = workspace.ResourceManager.ReadFileResource<UserFile>(file!, modify);

        var instance = userfile.RSZ.ObjectList[0];
        var arrayField = instance.RszClass.fields.FirstOrDefault(f => f.array);
        if (arrayField == null) throw new Exception($"Invalid array-field patcher - root instance {instance} has no array fields");

        return (IList<object>)instance.GetFieldValue(arrayField.name)!;
    }
}
