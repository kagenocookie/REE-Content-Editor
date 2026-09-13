using System.Diagnostics;
using System.Text.Json.Nodes;
using ContentEditor;
using ReeLib;
using ReeLib.Pfb;

namespace ContentPatcher;

public class ResourcePathResource(ResourceConfig type, string filepath) : IAddressableContentResource, IPropertyContainer
{
    public ResourceConfig ResourceType { get; } = type;
    public string FileResourcePath { get; set; } = filepath;

    public RszInstance? CatalogEntry { get; set; }
    public string ResourcePath { get; set; } = "";

    public long ID => CatalogEntry == null ? -1 : IDGenerator.GenerateID(CatalogEntry);

    public IContentResource Clone() => new ResourcePathResource(ResourceType, FileResourcePath) {
        ResourcePath = ResourcePath, // TODOourning npc?
        CatalogEntry = CatalogEntry?.Clone(),
    };

    public object? Get(string path)
    {
        switch (path) {
            case "path": return ResourcePath;
            default: throw new Exception($"Invalid property {path} for resource prefab proxy {ResourceType}");
        }
    }

    public void Set(string path, object? value)
    {
        switch (path) {
            case "path": ResourcePath = value as string ?? ""; break;
            default: throw new Exception($"Invalid property {path} for resource prefab proxy {ResourceType}");
        }
    }

    public JsonNode ToJson(Workspace env) => JsonValue.Create(ResourcePath);

    public override string ToString() => ResourcePath;
}

public class ResourcePathResourceValueHandler : EntityFieldValueHandler
{
    public override IContentResource? ApplyValue(ContentWorkspace workspace, IContentResource? currentResource, JsonNode? data, ResourceEntity entity, ResourceState state)
    {
        var newPath = data?.GetValueKind() == System.Text.Json.JsonValueKind.String ? data.GetValue<string>() : null;
        if (string.IsNullOrEmpty(newPath)) {
            if (currentResource != null) {
                (currentResource as ResourcePathResource)?.ResourcePath = "";
            }
            return currentResource;
        }

        if (currentResource is not ResourcePathResource res) {
            currentResource = res = new ResourcePathResource(Field.Config, Field.Config.Resource.Files[0]);
        }
        res.ResourcePath = newPath.Replace('\\', '/');
        return res;
    }
}

[ResourcePatcher("resource_proxy_pfb")]
public class ResourceProxyPrefabHandler : ResourceHandler, IResourceHandlerStatic
{
    private RszFieldAccessorBase<List<object>> arrayAccessor = null!;
    public KnownFileFormats ResourceType { get; set; }

    public override EntityFieldValueHandler CreateValueHandler(EntityField field) => new ResourcePathResourceValueHandler();

    private RszClass? catalogEntryClass;
    private RszClass? componentClass;

    private static readonly RszFieldAccessorFirstFallbacks<RszInstance> PrefabLinkField = new RszFieldAccessorFirstFallbacks<RszInstance>([
        f => f.original_type == "via.Prefab",
        f => f.type == RszFieldType.Object
    ]);
    private static readonly RszFieldAccessorFirst<uint> CatalogIdField = new RszFieldAccessorFirst<uint>(f => f.type == RszFieldType.U32);
    private NestableFieldAccessor? PrefabToResourceField { get; set; }

    public static ResourceHandler Deserialize(ResourceConfig resource, ResourceConfigSerialized data, ContentWorkspace workspace)
    {
        return new ResourceProxyPrefabHandler() {
            Config = resource,
            Files = data.TargetFiles.ToList(),
            ResourceType = data.ResourceType,
            arrayAccessor = data.GetDirectFieldAccessor<List<object>>(static f => f.array && f.type == RszFieldType.Object),
        };
    }

    public void UpdateCatalogEntry(ResourcePathResource resource, long id, ContentWorkspace workspace)
    {
        Debug.Assert(componentClass != null);
        if (!workspace.ResourceManager.TryResolveGameFile(resource.FileResourcePath, out var catFile)) {
            Logger.Error("Failed to resolve catalog file " + (resource.FileResourcePath));
            return;
        }
        var idgen = resource.ResourceType.IDGeneratorRequired;

        var catalog = catFile.GetFile<UserFile>().Instance!;
        var list = arrayAccessor.Get(catalog);
        if (resource.CatalogEntry == null) {
            resource.CatalogEntry = list.FirstOrDefault(item => idgen.GetID((RszInstance)item) == id) as RszInstance;
        } else if (list.Contains(resource.CatalogEntry)) {
            catFile.Modified = true;
        }

        if (resource.CatalogEntry == null) {
            // TODO load or create entry
            if (catalogEntryClass == null) {
                Logger.Error("Unknown catalog class for resource " + Config);
                return;
            }

            resource.CatalogEntry = workspace.Env.CreateRszInstance(catalogEntryClass);
        }

        if (resource.CatalogEntry == null) {
            if (catalogEntryClass == null) {
                Logger.Error("Unknown catalog class for resource " + Config);
                return;
            }
            resource.CatalogEntry = workspace.Env.CreateRszInstance(catalogEntryClass);
            catFile.Modified = true;
        }

        if (PrefabLinkField.Get(resource.CatalogEntry) is not RszInstance viaPrefab) {
            PrefabLinkField.Set(resource.CatalogEntry, viaPrefab = workspace.Env.CreateRszInstance(workspace.Env.Classes.Prefab));
            catFile.Modified = true;
        }
        var prefabPath = viaPrefab.Get(RszFieldCache.Prefab.Path);
        if (string.IsNullOrEmpty(prefabPath)) {
            RszFieldCache.Prefab.Path.Set(viaPrefab, prefabPath = string.Concat(
                PathUtils.GetFilepathWithoutExtensionOrVersion(resource.FileResourcePath),
                "_",
                PathUtils.GetExtensionWithoutPeriod(resource.FileResourcePath),
                ".pfb"));
            catFile.Modified = true;
        }

        if (!workspace.ResourceManager.TryResolveGameFile(prefabPath, out var pfbHandle)) {
            pfbHandle = workspace.ResourceManager.CreateNewFile(KnownFileFormats.Prefab, prefabPath)!;
        }

        var pfb = pfbHandle.GetFile<PfbFile>();
        var go = pfb.GameObjects.FirstOrDefault();
        if (go == null) {
            go = new ReeLib.Pfb.PfbGameObject() { Instance = workspace.Env.CreateRszInstance(workspace.Env.Classes.GameObject) };
            go.Components.Add(workspace.Env.CreateRszInstance(workspace.Env.Classes.Transform));
            pfb.GameObjects.Add(go);
            catFile.Modified = true;
        }

        var comp = go.Components.FirstOrDefault(c => c.RszClass == componentClass);
        if (comp == null) {
            go.Components.Add(comp = workspace.Env.CreateRszInstance(componentClass));
            catFile.Modified = true;
        }
        PrefabToResourceField!.Set(comp, resource.ResourcePath);
    }

    public override void ReadResources(ContentWorkspace workspace, Dictionary<long, IContentResource> dict)
    {
        var idGenerator = Config.IDGenerator;
        List<(RszInstance, string)> instances = new();
        foreach (var filepath in Files) {
            var instance = workspace.ResourceManager.GetFileContents<UserFile>(filepath).Instance!;

            var list = arrayAccessor.Get(instance);
            foreach (var item in list.Cast<RszInstance>()) {
                idGenerator ??= (Config.IDGenerator ??= IDGenerator.GetGenerator(item.RszClass));
                var id = idGenerator.GetID(item);
                catalogEntryClass ??= item.RszClass;

                var prefab = PrefabLinkField.Get(item);
                var prefabPath = prefab?.Get(RszFieldCache.Prefab.Path);
                if (string.IsNullOrEmpty(prefabPath)) {
                    dict[id] = new ResourcePathResource(Config, "");
                    continue;
                }

                if (!workspace.ResourceManager.TryResolveGameFile(prefabPath, out var pfbHandle)) {
                    Logger.Warn("Failed to load prefab file " + prefabPath);
                    continue;
                }
                workspace.ResourceManager.CloseFile(pfbHandle, true);

                var pfb = pfbHandle.GetFile<PfbFile>();
                var component = pfb.GameObjects.First().Components.First(c => c.RszClass.name != "via.Transform");
                if (component == null) {
                    Logger.Warn("No valid resource path component in prefab " + prefabPath);
                    continue;
                }
                componentClass ??= component.RszClass;

                if (PrefabToResourceField == null) {
                    var ff = component.Fields.FirstOrDefault(f => f.type is RszFieldType.String or RszFieldType.Resource);
                    if (ff == null) {
                        throw new Exception("Failed to determine resource path in prefab " + prefabPath);
                    }
                    PrefabToResourceField = new NestableFieldAccessor.SimpleField(component.RszClass, component.Fields.IndexOf(ff));
                }

                var resourcePathVal = PrefabToResourceField.Get(component);
                if (resourcePathVal is not string resourcePath) {
                    continue;
                }

                dict[id] = new ResourcePathResource(Config, filepath) { ResourcePath = resourcePath, CatalogEntry = item };
            }
        }
    }

    public override IContentResource CreateResource(ContentWorkspace workspace, long id, JsonNode? initialData)
    {
        var path = (initialData?.GetValueKind() == System.Text.Json.JsonValueKind.String ? initialData.GetValue<string>() : null) ?? "";
        if (path == "null") {
            return new ResourcePathResource(Config, Files[0]);
        }

        if (catalogEntryClass == null) throw new Exception();

        var idgen = Config.IDGeneratorRequired;
        var inst = workspace.Env.CreateRszInstance(catalogEntryClass);
        workspace.Diff.ApplyDiff(inst, initialData);
        if (idgen.Fields.Length == 1) {
            var idField = idgen.Fields[0].Field;
            var fieldType = RszInstance.RszFieldTypeToCSharpType(idField.type);
            idgen.Fields[0].Set(inst, Convert.ChangeType(id, fieldType));
        } else {
            throw new NotImplementedException("Unsupported rsz object id combination");
        }
        if (workspace.ResourceManager.TryResolveGameFile(Files[0], out var file)) {
            var user = file.GetFile<UserFile>().Instance!;
            arrayAccessor.Get(user).Add(inst);
        }
        return new ResourcePathResource(Config, Files[0]) { CatalogEntry = inst };
    }

    public override void ModifyResources(ContentWorkspace workspace, IEnumerable<KeyValuePair<long, IContentResource>> resources)
    {
        Debug.Assert(componentClass != null);
        Debug.Assert(PrefabToResourceField != null);
        var idgen = Config.IDGeneratorRequired;
        foreach (var (id, rawRes) in resources) {
            if (rawRes is not ResourcePathResource res) {
                continue;
            }

            UpdateCatalogEntry(res, id, workspace);
            // if (UpdateCatalogEntry(res, id, workspace)) {
            //     catFile.Modified = true;
            // }

            // if (!workspace.ResourceManager.TryResolveGameFile(res.FileResourcePath, out var catFile)) {
            //     Logger.Error("Failed to resolve catalog file " + (res.FileResourcePath));
            //     continue;
            // }

            // var catalog = catFile.GetFile<UserFile>().Instance!;
            // var list = arrayAccessor.Get(catalog);
            // if (res.CatalogEntry == null) {
            //     res.CatalogEntry = list.FirstOrDefault(item => idgen.GetID((RszInstance)item) == id) as RszInstance;
            // } else if (list.Contains(res.CatalogEntry)) {
            //     catFile.Modified = true;
            // }

            // if (res.CatalogEntry == null) {
            //     if (catalogEntryClass == null) {
            //         Logger.Error("Unknown catalog class for resource " + Config);
            //         return;
            //     }
            //     res.CatalogEntry = workspace.Env.CreateRszInstance(catalogEntryClass);
            //     list.Add(res.CatalogEntry);
            // }

            // if (PrefabLinkField.Get(res.CatalogEntry) is not RszInstance viaPrefab) {
            //     PrefabLinkField.Set(res.CatalogEntry, viaPrefab = workspace.Env.CreateRszInstance(workspace.Env.Classes.Prefab));
            //     catFile?.Modified = true;
            // }
            // var prefabPath = viaPrefab.Get(RszFieldCache.Prefab.Path);
            // if (string.IsNullOrEmpty(prefabPath)) {
            //     RszFieldCache.Prefab.Path.Set(viaPrefab, prefabPath = string.Concat(PathUtils.GetFilepathWithoutExtensionOrVersion(rawRes.FileResourcePath), ".pfb"));
            //     catFile?.Modified = true;
            // }

            // if (!workspace.ResourceManager.TryResolveGameFile(prefabPath, out var pfbHandle)) {
            //     pfbHandle = workspace.ResourceManager.CreateNewFile(KnownFileFormats.Prefab, prefabPath)!;
            // }

            // var pfb = pfbHandle.GetFile<PfbFile>();
            // var go = pfb.GameObjects.FirstOrDefault();
            // if (go == null) {
            //     go = new ReeLib.Pfb.PfbGameObject() { Instance = workspace.Env.CreateRszInstance(workspace.Env.Classes.GameObject) };
            //     go.Components.Add(workspace.Env.CreateRszInstance(workspace.Env.Classes.Transform));
            //     pfb.GameObjects.Add(go);
            // }

            // var comp = go.Components.FirstOrDefault(c => c.RszClass == componentClass);
            // if (comp == null) {
            //     go.Components.Add(comp = workspace.Env.CreateRszInstance(componentClass));
            // }
            // PrefabToResourceField.Set(componentClass, res.ResourcePath);
        }
    }
}
