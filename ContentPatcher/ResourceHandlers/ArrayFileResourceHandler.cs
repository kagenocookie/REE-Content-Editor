using System.Text.Json.Nodes;
using ReeLib;

namespace ContentPatcher;

[ResourcePatcher("array-file", nameof(Deserialize))]
public class ArrayFileResourceHandler : ResourceHandler
{
    private string? classname;
    private RszFieldAccessorBase<IList<object>> arrayAccessor = null!;

    public override EntityFieldValueHandler CreateValueHandler(EntityField field) => Config.SubIDGenerator == null ? new ObjectField() : new ObjectArray();

    public static ArrayFileResourceHandler Deserialize(ResourceConfig resource, EntityResourceConfigSerialized data, ContentWorkspace workspace)
    {
        return new ArrayFileResourceHandler() {
            Config = resource,
            Files = [data.SingleFile],
            classname = data.Classname,
            arrayAccessor = data.GetDirectFieldAccessor<IList<object>>(static f => f.array && f.type == RszFieldType.Object),
        };
    }

    public override IContentResource CreateResource(ContentWorkspace workspace, long id, JsonNode? initialData)
    {
        var idgen = Config.IDGeneratorRequired;
        if (Config.SubIDGenerator != null) {
            var list = new RSZObjectListResource(Config.Type, Files[0]);
            workspace.Diff.ApplyDiff(list.Instances, initialData, classname ?? Config.Type);
            foreach (var inst in list.Instances) {
                if (idgen.Fields?.Length == 1) {
                    var idField = idgen.Fields[0].Field;
                    if (idField.type is RszFieldType.String or RszFieldType.Resource) {
                        throw new NotImplementedException("String IDs not yet supported");
                    } else {
                        var fieldType = RszInstance.RszFieldTypeToCSharpType(idField.type);
                        idgen.Fields[0].Set(inst, Convert.ChangeType(id, fieldType));
                    }
                } else {
                    throw new NotImplementedException("Unsupported rsz object id combination");
                }
            }
            return list;
        } else {
            var inst = RszInstance.CreateInstance(workspace.Env.RszParser, workspace.Env.RszParser.GetRSZClass(classname ?? Config.Type)!);
            workspace.Diff.ApplyDiff(inst, initialData);
            if (idgen.Fields?.Length == 1) {
                var idField = idgen.Fields[0].Field;
                var fieldType = RszInstance.RszFieldTypeToCSharpType(idField.type);
                idgen.Fields[0].Set(inst, Convert.ChangeType(id, fieldType));
            } else {
                throw new NotImplementedException("Unsupported rsz object id combination");
            }
            return new RSZObjectResource(inst, Files[0], Config.Type);
        }
    }

    public override void ReadResources(ContentWorkspace workspace, Dictionary<long, IContentResource> dict)
    {
        var items = GetObjectList(workspace, false);
        if (items.Count == 0) return;

        var idGenerator = Config.IDGeneratorRequired;
        var subIdGenerator = Config.SubIDGenerator;
        foreach (var item in items.OfType<RszInstance>()) {
            var id = idGenerator.GetID(item);
            if (subIdGenerator != null) {
                if (dict.TryGetValue(id, out var list) && list is RSZObjectListResource objlist) {
                    objlist.Instances.Add(item);
                } else {
                    dict[id] = new RSZObjectListResource(item, Files[0]);
                }
            } else {
                dict[id] = new RSZObjectResource(item, Files[0]);
            }
        }
    }

    public override void ModifyResources(ContentWorkspace workspace, IEnumerable<KeyValuePair<long, IContentResource>> resources)
    {
        var items = GetObjectList(workspace, true);
        if (items.Count == 0) return;
        // the current expected behavior is that we read _all_ the resources in ReadResources, meaning we can just clear and re-add everything here

        var dict = new Dictionary<long, RszInstance>();
        if (Config.SubIDGenerator != null) {
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
        UserFile userfile = workspace.ResourceManager.ReadFileResource<UserFile>(Files[0], modify);
        var instance = userfile.Instance!;
        var items = arrayAccessor.Get(instance);
        return items;
    }
}
