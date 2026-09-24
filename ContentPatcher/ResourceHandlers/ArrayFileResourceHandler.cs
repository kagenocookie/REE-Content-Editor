using System.Text.Json.Nodes;
using ReeLib;

namespace ContentPatcher;

[ResourcePatcher("array-file")]
public class ArrayFileResourceHandler : ResourceHandler, IResourceHandlerStatic
{
    private RszFieldAccessorBase<IList<object>> arrayAccessor = null!;

    public override EntityFieldValueHandler CreateValueHandler(EntityField field) => Config.SubIDGenerator == null ? new ObjectField() : new ObjectArray();

    public static ResourceHandler Deserialize(ResourceConfig resource, ResourceConfigSerialized data, ContentWorkspace workspace)
    {
        return new ArrayFileResourceHandler() {
            Config = resource,
            Files = data.TargetFiles.ToList(),
            arrayAccessor = data.GetDirectFieldAccessor<IList<object>>(static f => f.array && f.type == RszFieldType.Object),
        };
    }

    public override IContentResource ApplyResourceData(ContentWorkspace workspace, IContentResource? resource, JsonNode? data, ResourceEntity? entity)
    {
        if (Config.SubIDGenerator != null) {
            if (resource is not RSZObjectListResource rszl) {
                resource = rszl = new RSZObjectListResource(Config, [], Files[0]);
            }

            workspace.Diff.ApplyDiff(rszl.Instances, data, Config.RszClassRequired.name);
        } else {
            if (resource is not RSZObjectResource rszo) {
                resource = rszo = new RSZObjectResource(Config, workspace.CreateRszInstance(Config.RszClassRequired), Files[0]);
            }

            workspace.Diff.ApplyDiff(rszo.Instance, data);
        }
        if (Config.Filter is ISettable settable) {
            settable.Set(resource);
        }
        return resource;
    }

    public override IContentResource CreateResource(ContentWorkspace workspace, long id, JsonNode? initialData)
    {
        var res = ApplyResourceData(workspace, null, initialData, null);
        var idgen = Config.IDGeneratorRequired;
        if (idgen.Fields != null && idgen.Fields.Length != 1) {
            throw new NotImplementedException("Unsupported rsz object id combination");
        }

        if (idgen.Fields == null) {
            return res;
        }

        var idField = idgen.Fields[0];
        var fieldType = RszInstance.RszFieldTypeToCSharpType(idField.Field.type);
        var castId = Convert.ChangeType(id, fieldType);

        if (res is RSZObjectListResource list) {
            foreach (var item in list.Instances) {
                idField.Set(item, castId);
            }
        } else if (res is RSZObjectResource inst) {
            idField.Set(inst.Instance, castId);
        }
        if (Config.Filter is ISettable settable) {
            settable.Set(res);
        }
        return res;
    }

    public override void ReadResources(ContentWorkspace workspace, Dictionary<long, IContentResource> dict)
    {
        var idGenerator = Config.IDGeneratorRequired;
        var subIdGenerator = Config.SubIDGenerator;

        foreach (var filepath in Files) {
            var userfile = workspace.ResourceManager.GetFileContents<UserFile>(filepath);
            var items = arrayAccessor.Get(userfile.Instance!);
            foreach (var item in items.Cast<RszInstance>()) {
                if (Config.Filter?.IsEnabled(item) == false) {
                    continue;
                }
                var id = idGenerator.GetID(item);
                if (subIdGenerator != null) {
                    if (!dict.TryGetValue(id, out var list) || list is not RSZObjectListResource objlist) {
                        dict[id] = objlist = new RSZObjectListResource(Config, filepath);
                    }
                    objlist.Instances.Add(item);
                } else {
                    dict[id] = new RSZObjectResource(Config, item, filepath);
                }
            }
        }
    }

    private void ClearList(IList<object> list)
    {
        if (Config.Filter == null) {
            list.Clear();
            return;
        }

        for (int i = list.Count - 1; i >= 0; i--) {
            var item = list[i];
            if (Config.Filter.IsEnabled(item)) {
                list.RemoveAt(i);
            }
        }
    }

    public override void ModifyResources(ContentWorkspace workspace, IEnumerable<KeyValuePair<long, IContentResource>> resources)
    {
        var outFiles = new Dictionary<string, IList<object>>();
        if (Config.SubIDGenerator != null) {
            foreach (var (id, resource) in resources) {
                var list = (RSZObjectListResource)resource;
                if (!outFiles.TryGetValue(list.FileResourcePath, out var outList)) {
                    var userfile = workspace.ResourceManager.GetFileContents<UserFile>(list.FileResourcePath, true);
                    outFiles[list.FileResourcePath] = outList = arrayAccessor.Get(userfile.Instance!);
                    ClearList(outList);
                }

                foreach (var item in list.Instances) {
                    outList.Add(item);
                }
            }
        } else {
            foreach (var (_, item) in resources) {
                var citem = (RSZObjectResource)item;
                if (!outFiles.TryGetValue(citem.FileResourcePath, out var outList)) {
                    var userfile = workspace.ResourceManager.GetFileContents<UserFile>(citem.FileResourcePath, true);
                    outFiles[citem.FileResourcePath] = outList = arrayAccessor.Get(userfile.Instance!);
                    ClearList(outList);
                }

                outList.Add(citem.Instance);
            }
        }
    }
}
