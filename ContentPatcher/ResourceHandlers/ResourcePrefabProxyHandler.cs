using System.Diagnostics;
using System.Text.Json.Nodes;
using ContentEditor;
using ReeLib;
using ReeLib.Pfb;

namespace ContentPatcher;

public class ResourcePathResource(string type, string path) : IContentResource
{
    public string ResourceTypeID { get; } = type;
    public string FilePath { get; set; } = path;

    public RszInstance? CatalogEntry { get; set; }
    public string ResourcePath { get; set; } = "";

    public IContentResource Clone() => new ResourcePathResource(ResourceTypeID, FilePath) { ResourcePath = ResourcePath };

    public JsonNode ToJson(Workspace env) => JsonValue.Create(ResourcePath);

    public override string ToString() => ResourcePath;
}

public class ResourcePathResourceValueHandler : EntityFieldValueHandler
{
    public override string? ResourceTypeId => throw new NotImplementedException();

    public override IContentResource? ApplyValue(ContentWorkspace workspace, IContentResource? currentResource, JsonNode? data, ResourceEntity entity, ResourceState state)
    {
        throw new NotImplementedException();
    }

    public override IContentResource? FetchResource(ContentWorkspace workspace, ResourceEntity entity, long resourceId, ResourceState state)
    {
        throw new NotImplementedException();
    }
}

[ResourcePatcher("resource_proxy_pfb", nameof(Deserialize))]
public class ResourceProxyPrefabHandler : ResourceHandler
{
    private RszFieldAccessorBase<List<object>> arrayAccessor = null!;
    private KnownFileFormats ResourceType { get; set; }

    public override EntityFieldValueHandler CreateValueHandler(EntityField field) => new ResourcePathResourceValueHandler();

    private RszClass? catalogEntryClass;
    private RszClass? componentClass;

    private static readonly RszFieldAccessorFirstFallbacks<RszInstance> PrefabLinkField = new RszFieldAccessorFirstFallbacks<RszInstance>([
        f => f.original_type == "via.Prefab",
        f => f.type == RszFieldType.Object
    ]);
    private static readonly RszFieldAccessorFirst<uint> CatalogIdField = new RszFieldAccessorFirst<uint>(f => f.type == RszFieldType.U32);
    private NestableFieldAccessor? PrefabToResourceField { get; set; }

    public static ResourceProxyPrefabHandler Deserialize(ResourceConfig resource, EntityResourceConfigSerialized data, ContentWorkspace workspace)
    {
        return new ResourceProxyPrefabHandler() {
            Config = resource,
            Files = data.TargetFiles.ToList(),
            ResourceType = data.ResourceType,
            arrayAccessor = data.GetDirectFieldAccessor<List<object>>(static f => f.array && f.type == RszFieldType.Object),
        };
    }

    public override void ReadResources(ContentWorkspace workspace, Dictionary<long, IContentResource> dict)
    {
        var idGenerator = Config.IDGenerator;
        List<(RszInstance, string)> instances = new();
        foreach (var filepath in Files) {
            var instance = workspace.ResourceManager.ReadFileResource<UserFile>(filepath, false).Instance!;

            var list = arrayAccessor.Get(instance);
            foreach (var item in list.Cast<RszInstance>()) {
                idGenerator ??= (Config.IDGenerator ??= IDGenerator.GetGenerator(item.RszClass));
                var id = idGenerator.GetID(item);
                catalogEntryClass ??= item.RszClass;

                var prefab = PrefabLinkField.Get(item);
                var prefabPath = prefab?.Get(RszFieldCache.Prefab.Path);
                if (string.IsNullOrEmpty(prefabPath)) {
                    dict[id] = new ResourcePathResource(Config.Type, "");
                    continue;
                }

                if (!workspace.ResourceManager.TryResolveGameFile(prefabPath, out var pfbHandle)) {
                    Logger.Warn("Failed to load prefab file " + prefabPath);
                    continue;
                }

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

                dict[id] = new ResourcePathResource(Config.Type, filepath) { ResourcePath = resourcePath, CatalogEntry = item };
            }
        }
    }

    public override IContentResource CreateResource(ContentWorkspace workspace, long id, JsonNode? initialData)
    {
        var path = (initialData?.GetValueKind() == System.Text.Json.JsonValueKind.String ? initialData.GetValue<string>() : null) ?? "";
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
        return new ResourcePathResource(Config.Type, Files[0]) { CatalogEntry = inst };
    }

    public override void ModifyResources(ContentWorkspace workspace, IEnumerable<KeyValuePair<long, IContentResource>> resources)
    {
        Debug.Assert(componentClass != null);
        Debug.Assert(PrefabToResourceField != null);
        foreach (var (id, rawRes) in resources) {
            if (rawRes is not ResourcePathResource res) {
                continue;
            }

            if (res.CatalogEntry == null) {
                if (catalogEntryClass == null) {
                    Logger.Error("Unknown catalog class for resource " + Config);
                    return;
                }
                res.CatalogEntry = workspace.Env.CreateRszInstance(catalogEntryClass);
            }

            if (workspace.ResourceManager.TryResolveGameFile(rawRes.FilePath ?? "", out var catFile)) {
                var catalog = catFile.GetFile<UserFile>().Instance!;
                var list = arrayAccessor.Get(catalog);
                if (list.Contains(res.CatalogEntry)) {
                    list.Add(res.CatalogEntry);
                    catFile.Modified = true;
                }
            }

            if (PrefabLinkField.Get(res.CatalogEntry) is not RszInstance viaPrefab) {
                PrefabLinkField.Set(res.CatalogEntry, viaPrefab = workspace.Env.CreateRszInstance(workspace.Env.Classes.Prefab));
                catFile?.Modified = true;
            }
            var prefabPath = viaPrefab.Get(RszFieldCache.Prefab.Path);
            if (string.IsNullOrEmpty(prefabPath)) {
                RszFieldCache.Prefab.Path.Set(viaPrefab, prefabPath = string.Concat(PathUtils.GetFilepathWithoutExtensionOrVersion(rawRes.FilePath), ".pfb"));
                catFile?.Modified = true;
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
            }

            var comp = go.Components.FirstOrDefault(c => c.RszClass == componentClass);
            if (comp == null) {
                go.Components.Add(comp = workspace.Env.CreateRszInstance(componentClass));
            }
            PrefabToResourceField.Set(componentClass, res.ResourcePath);
        }
    }

    private List<(RszInstance entry, string sourceFile)> GetCatalogEntries(ContentWorkspace workspace, bool modify)
    {
        List<(RszInstance, string)> instances = new();
        foreach (var filepath in Files) {
            var instance = workspace.ResourceManager.ReadFileResource<UserFile>(filepath, modify).Instance!;

            var list = arrayAccessor.Get(instance);
            foreach (var item in list.Cast<RszInstance>()) {
                instances.Add((item, filepath));
            }
        }

        return instances;
    }
}
