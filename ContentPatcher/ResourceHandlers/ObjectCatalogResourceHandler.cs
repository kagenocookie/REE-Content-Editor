using System.Diagnostics;
using System.Text.Json.Nodes;
using ContentEditor;
using ReeLib;
using ReeLib.Common;

namespace ContentPatcher;

[ResourcePatcher("object-catalog")]
public class ObjectCatalogResourceHandler : ResourceHandler, IResourceHandlerStatic
{
    private RszFieldAccessorBase<IList<object>> arrayAccessor = null!;

    public override EntityFieldValueHandler CreateValueHandler(EntityField field) => Config.SubIDGenerator == null ? new ObjectField() : new ObjectArray();

    public static ResourceHandler Deserialize(ResourceConfig resource, ResourceConfigSerialized data, ContentWorkspace workspace)
    {
        return new ObjectCatalogResourceHandler() {
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

    public override IContentResource CreateResource(ContentWorkspace workspace, long id, JsonNode? initialData, ResourceEntity? entity)
    {
        var res = ApplyResourceData(workspace, null, initialData, entity);
        var idgen = Config.IDGeneratorRequired;
        if (idgen.Fields != null && idgen.Fields.Length != 1) {
            throw new NotImplementedException("Unsupported rsz object id combination");
        }

        if (idgen.Fields == null) {
            return res;
        }

        var idField = idgen.Fields[0];
        if (idField.Field.type != RszFieldType.String) {
            // for string-based IDs, the entity config should've required a string input which would've written the ID string there already
            var castId = id.SafeBoxedID(idField.Field.type);

            if (res is RSZObjectListResource list) {
                foreach (var item in list.Instances) {
                    idField.Set(item, castId);
                }
            } else if (res is RSZObjectResource inst) {
                idField.Set(inst.Instance, castId);
            }
        }
        if (Config.Filter is ISettable settable) {
            settable.Set(res);
        }

        if (Config.OriginalConfig?.GetParam<bool>("duplicate_assets_on_create") == true) {
            var instances = (res as RSZObjectListResource)?.Instances ?? [((RSZObjectResource)res).Instance];
            DuplicateAssets(workspace, instances, id);
        }

        return res;
    }

    private void DuplicateAssets(ContentWorkspace workspace, List<RszInstance> instances, long id)
    {
        var clonedAssets = new Dictionary<string, string>(PakHashedPathComparer.Instance);
        foreach (var inst in instances) {
            foreach (var obj in inst.GetChildren()) {
                for (int i = 0; i < obj.Fields.Length; i++) {
                    var f = obj.Fields[i];
                    if (f.type is not RszFieldType.Resource and not RszFieldType.String) continue;
                    if (f.type == RszFieldType.String) {
                        if (obj.RszClass.name != "via.Prefab" && obj.RszClass.name != "via.Folder") {
                            continue;
                        }
                    }

                    var path = obj.Values[i] as string;
                    if (string.IsNullOrEmpty(path)) continue;

                    if (clonedAssets.TryGetValue(path, out var newFilePath)) {
                        obj.Values[i] = newFilePath;
                        continue;
                    }

                    if (!workspace.ResourceManager.TryResolveGameFile(path, out var handle)) {
                        Logger.Warn($"Failed to open original file for duplication: {path}");
                        continue;
                    }

                    newFilePath = $"CustomFile/{Config.Type}/{handle.Format.format}_{id}_{clonedAssets.Count.ToString("D02")}{Path.GetExtension(path)}";
                    obj.Values[i] = clonedAssets[path] = newFilePath;

                    newFilePath = workspace.Env.AppendFileVersion(newFilePath);
                    if (workspace.CurrentBundle != null) {
                        var bundleFilepath = Path.Combine(workspace.CurrentBundle.StoragePath, newFilePath);
                        handle.Save(workspace, bundleFilepath);
                        workspace.CurrentBundle.AddResource(newFilePath, newFilePath, true);
                        if (workspace.ResourceManager.TryResolveGameFile(bundleFilepath, out var newFile)) {
                            newFile.Modified = true;
                            if (newFile.Loader is IFilePropertyContainer propfile && Config.OriginalConfig?.TryGetParam<List<object>>("id_props", out var idprops) == true) {
                                foreach (var idPropPath in idprops) {
                                    propfile.Set(newFile, idPropPath.ToString()!, id);
                                }
                            }
                        }
                    } else {
                        var newFile = workspace.ResourceManager.CreateNewFile(handle.Format.format, newFilePath);
                        if (newFile != null) {
                            handle.CopyContentsTo(newFile, workspace);
                        }
                    }
                }
            }
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
                    if (dict.ContainsKey(id)) {
                        Logger.Warn($"Found duplicate {Config} resource {id}");
                    }
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
        foreach (var (_, item) in resources) {
            var file = item.FileResourcePath;
            Debug.Assert(!string.IsNullOrEmpty(file));

            if (!outFiles.TryGetValue(file, out var outList)) {
                var userfile = workspace.ResourceManager.GetFileContents<UserFile>(file, true);
                outFiles[file] = outList = arrayAccessor.Get(userfile.Instance!);
                ClearList(outList);
            }

            if (item is NulledResource) {
                // nothing to do - ClearList would've removed it already
                continue;
            }

            if (Config.SubIDGenerator != null) {
                foreach (var resource in ((RSZObjectListResource)item).Instances) {
                    outList.Add(resource);
                }
            } else {
                var citem = (RSZObjectResource)item;
                outList.Add(citem.Instance);
            }
        }
    }
}
