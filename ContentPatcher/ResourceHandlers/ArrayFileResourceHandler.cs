using System.Text.Json.Nodes;
using ReeLib;

namespace ContentPatcher;

[ResourcePatcher("array-file")]
public class ArrayFileResourceHandler : ResourceHandler, IResourceHandlerStatic
{
    private string? classname;
    private RszFieldAccessorBase<IList<object>> arrayAccessor = null!;

    public override EntityFieldValueHandler CreateValueHandler(EntityField field) => Config.SubIDGenerator == null ? new ObjectField() : new ObjectArray();

    public static ResourceHandler Deserialize(ResourceConfig resource, ResourceConfigSerialized data, ContentWorkspace workspace)
    {
        return new ArrayFileResourceHandler() {
            Config = resource,
            Files = data.TargetFiles.ToList(),
            classname = data.Classname,
            arrayAccessor = data.GetDirectFieldAccessor<IList<object>>(static f => f.array && f.type == RszFieldType.Object),
        };
    }

    public override IContentResource CreateResource(ContentWorkspace workspace, long id, JsonNode? initialData)
    {
        var idgen = Config.IDGeneratorRequired;
        if (Config.SubIDGenerator != null) {
            var list = new RSZObjectListResource(Config, Files[0]);
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
                if (Config.Filter is ISettable settable) {
                    settable.Set(inst);
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
            if (Config.Filter is ISettable settable) {
                settable.Set(inst);
            }
            return new RSZObjectResource(Config, inst, Files[0]);
        }
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
