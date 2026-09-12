using System.Text.Json.Nodes;
using ReeLib;

namespace ContentPatcher;

[ResourcePatcher("multi-array")]
public class MultiFileArrayResourceHandler : ResourceHandler, IResourceHandlerStatic
{
    private RszFieldAccessorBase<IList<object>> arrayAccessor = null!;

    public override EntityFieldValueHandler CreateValueHandler(EntityField field) => new ObjectArray();

    public static ResourceHandler Deserialize(ResourceConfig resource, ResourceConfigSerialized data, ContentWorkspace workspace)
    {
        return new MultiFileArrayResourceHandler() {
            Config = resource,
            // path = data.Field ?? throw new Exception("Field is required for multi-array patcher!"),
            Files = data.TargetFiles.ToList(),
            arrayAccessor = data.GetDirectFieldAccessor<IList<object>>(static f => f.array && f.type == RszFieldType.Object),
        };
    }

    public override IContentResource CreateResource(ContentWorkspace workspace, long id, JsonNode? initialData)
    {
        // always store new resources on the first path, the idea is that it probably doesn't matter which because the catalogs are usually just merged for runtime anyway
        var file = Files[0];
        var inst = RszInstance.CreateInstance(workspace.Env.RszParser, workspace.Env.RszParser.GetRSZClass(Config.Type)!);
        workspace.Diff.ApplyDiff(inst, initialData);
        var idgen = Config.IDGeneratorRequired;
        if (idgen.Fields.Length == 1) {
            var idField = idgen.Fields[0].Field;
            var fieldType = RszInstance.RszFieldTypeToCSharpType(idField.type);
            idgen.Fields[0].Set(inst, Convert.ChangeType(id, fieldType));
        } else {
            throw new NotImplementedException("Unsupported rsz object id combination");
        }
        return new RSZObjectListResource(Config, file);
    }

    public override void ReadResources(ContentWorkspace workspace, Dictionary<long, IContentResource> dict)
    {
        var idGenerator = Config.IDGeneratorRequired;
        foreach (var filepath in Files) {
            var userfile = workspace.ResourceManager.GetFileContents<UserFile>(filepath);
            var items = arrayAccessor.Get(userfile.Instance!);
            if (items == null) continue;

            foreach (var item in items.Cast<RszInstance>()) {
                if (Config.Filter?.IsEnabled(item) == false) {
                    continue;
                }
                var id = idGenerator.GetID(item);
                if (!dict.TryGetValue(id, out var list) || list is not RSZObjectListResource objlist) {
                    dict[id] = objlist = new RSZObjectListResource(Config, filepath);
                }
                objlist.Instances.Add(item);
            }
        }
    }

    public override void ModifyResources(ContentWorkspace workspace, IEnumerable<KeyValuePair<long, IContentResource>> resources)
    {
        var outFiles = new Dictionary<string, IList<object>>();
        foreach (var (id, resource) in resources) {
            var list = (RSZObjectListResource)resource;
            if (!outFiles.TryGetValue(list.FileResourcePath, out var outList)) {
                var userfile = workspace.ResourceManager.GetFileContents<UserFile>(list.FileResourcePath, true);
                // outFiles[list.FileResourcePath] = outList = (List<object>)userfile.Instance!.GetNestedFieldValue(path)!;
                outFiles[list.FileResourcePath] = outList = (List<object>)arrayAccessor.Get(userfile.Instance!);
                outList.Clear();
            }

            foreach (var item in list.Instances) {
                outList.Add(item);
            }
        }
    }
}
