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

    public override IContentResource ApplyResourceData(ContentWorkspace workspace, IContentResource? resource, JsonNode? data)
    {
        if (resource is not RSZObjectListResource rszl) {
            // always store new resources on the first path, the idea is that it probably doesn't matter which because the catalogs are usually just merged for runtime anyway
            resource = rszl = new RSZObjectListResource(Config, [], Files[0]);
        }

        workspace.Diff.ApplyDiff(rszl.Instances, data, Config.RszClassRequired.name);
        return resource;
    }

    public override IContentResource CreateResource(ContentWorkspace workspace, long id, JsonNode? initialData)
    {
        var list = (RSZObjectListResource)ApplyResourceData(workspace, null, initialData);

        var idgen = Config.IDGeneratorRequired;
        if (idgen.Fields != null && idgen.Fields.Length != 1) {
            throw new NotImplementedException("Unsupported rsz object id combination");
        }

        if (idgen.Fields == null) {
            return list;
        }

        var idField = idgen.Fields[0].Field;
        var fieldType = RszInstance.RszFieldTypeToCSharpType(idField.type);
        var castId = Convert.ChangeType(id, fieldType);

        foreach (var item in list.Instances) {
            idgen.Fields[0].Set(item, castId);
        }

        return list;
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
