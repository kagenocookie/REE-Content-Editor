using System.Text.Json.Nodes;
using ContentEditor;
using ReeLib;
using ReeLib.Common;

namespace ContentPatcher;

[ResourcePatcher("multi-array", nameof(Deserialize))]
public class MultiFileArrayResourceHandler : ResourceHandler
{
    private string path = "";
    private List<string> files = new();

    private class FileObjectContainer
    {
        public string file = "";
        public string path = "";
        public IList<object> list = null!;

        public override string ToString() => $"{path} [{list.Count}] ({file})";
    }

    public static MultiFileArrayResourceHandler Deserialize(string resourceKey, Dictionary<string, object> data)
    {
        return new MultiFileArrayResourceHandler() {
            ResourceKey = resourceKey,
            path = (string)data["path"],
            files = ((IEnumerable<object>)data["files"]).Cast<string>().ToList(),
        };
    }

    public override (long id, IContentResource resource) CreateResource(ContentWorkspace workspace, ClassConfig config, ResourceEntity entity, JsonNode? initialData)
    {
        // always store new resources on the first path, the idea is that it probably doesn't matter which because the catalogs are usually just merged for runtime anyway
        var file = files[0];
        var inst = RszInstance.CreateInstance(workspace.Env.RszParser, workspace.Env.RszParser.GetRSZClass(ResourceKey)!);
        workspace.Diff.ApplyDiff(inst, initialData);
        if (config.IDFields?.Length == 1) {
            var idField = config.IDFields[0].Field;
            var fieldType = RszInstance.RszFieldTypeToCSharpType(idField.type);
            config.IDFields[0].Set(inst, Convert.ChangeType(entity.Id, fieldType));
        } else {
            throw new NotImplementedException("Unsupported rsz object id combination");
        }
        return (entity.Id, new RSZObjectResource(inst, file));
    }

    public override void ReadResources(ContentWorkspace workspace, ClassConfig config, Dictionary<long, IContentResource> dict)
    {
        var items = GetObjectList(workspace, false);
        if (items.Count == 0) return;

        var firstInstance = items.SelectMany(i => i.list).First();
        var idGenerator = IDGenerator.GetGenerator(firstInstance, config.IDFields!);
        if (config.SubIDFields?.Length > 0) {
            throw new NotImplementedException("Sub ID not yet supported for multi file resources");
        }

        foreach (var item in items) {
            var file = item.file;
            foreach (var elem in item.list.OfType<RszInstance>()) {
                var id = idGenerator.GetID(elem, config.IDFields!);
                var hashedId = AppUtils.StableHashCombine((uint)id, MurMur3HashUtils.GetHash(file));
                dict[hashedId] = new RSZObjectResource(elem, file);
            }
        }
    }

    public override void ModifyResources(ContentWorkspace workspace, ClassConfig config, IEnumerable<KeyValuePair<long, IContentResource>> resources)
    {
        var items = GetObjectList(workspace, true);
        if (items.Count == 0) return;
        // the current expected behavior is that we read _all_ the resources in ReadResources, meaning we can just clear and re-add everything here

        foreach (var obj in items) {
            obj.list.Clear();
        }

        foreach (var (_, item) in resources) {
            var file = item.FilePath;
            var container = items.FirstOrDefault(it => it.file == file);
            if (container == null) {
                Logger.Error("Multi-file resource pointing to unknown file " + file);
                continue;
            }

            container.list.Add(((RSZObjectResource)item).Instance);
        }
    }

    private List<FileObjectContainer> GetObjectList(ContentWorkspace workspace, bool modify)
    {
        var list = new List<FileObjectContainer>();
        foreach (var file in files) {
            UserFile userfile = workspace.ResourceManager.ReadFileResource<UserFile>(file!, modify);

            var instance = userfile.Instance!;
            if (path.Contains('.')) {
                var parts = path.Split('.');
                static void HandleRecursiveSubfields(Span<string> parts, object instance, List<FileObjectContainer> list, string file, string path)
                {
                    var part = parts[0];
                    if (part == "*") {
                        var arr = (IEnumerable<object>)instance;
                        var i = 0;
                        foreach (var item in arr.Cast<RszInstance>()) {
                            var subpath = path == "" ? i.ToString() : $"{path}.{i}";
                            HandleRecursiveSubfields(parts.Slice(1), item, list, file, subpath);
                            i++;
                        }
                    } else if (int.TryParse(part, out var index)) {
                        var arr = (IEnumerable<object>)instance;
                        var item = arr.ElementAt(index);
                        var subpath = path == "" ? index.ToString() : $"{path}.{index}";
                        HandleRecursiveSubfields(parts.Slice(1), item!, list, file, subpath);
                    } else {
                        var item = (RszInstance)instance;
                        var fieldIndex = item.RszClass.IndexOfField(part);
                        if (fieldIndex == -1) {
                            throw new Exception($"Invalid array-field patcher - root instance {instance} does not have field {path}");
                        }
                        var fieldValue = item.Values[fieldIndex];
                        var subpath = path == "" ? part : $"{path}.{part}";
                        if (parts.Length == 1) {
                            list.Add(new FileObjectContainer() {
                                file = file,
                                path = subpath,
                                list = (IList<object>)fieldValue,
                            });
                        } else {
                            HandleRecursiveSubfields(parts.Slice(1), fieldValue, list, file, subpath);
                        }
                    }
                }
                HandleRecursiveSubfields(parts.AsSpan(), instance, list, file, "");
            } else {
                var arrayField = instance.RszClass.IndexOfField(path);
                if (arrayField == -1) throw new Exception($"Invalid array-field patcher - root instance {instance} does not have field {path}");

                var item = new FileObjectContainer() { file = file, path = path };
                item.list = (IList<object>)instance.Values[arrayField];
                list.Add(item);
            }
        }
        return list;
    }
}
