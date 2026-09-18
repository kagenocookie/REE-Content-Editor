using System.Diagnostics;
using System.Text.Json.Nodes;
using ContentEditor;
using ReeLib;
using ReeLib.Pfb;

namespace ContentPatcher;

public class CatalogPrefabResource(ResourceConfig type, RszInstance instance, string filepath)
    : RSZObjectResource(type, instance, filepath),
    IAddressableContentResource
{
    public RszInstance? CatalogEntry { get; set; }

    public long ID => CatalogEntry == null ? -1 : IDGenerator.GenerateID(CatalogEntry);

    public override IContentResource Clone() => new CatalogPrefabResource(ResourceType, Instance.Clone(), FileResourcePath) {
        CatalogEntry = CatalogEntry?.Clone(),
    };
}

[ResourcePatcher("resource_proxy_pfb")]
public class ResourceProxyPrefabHandler : ResourceHandler, IResourceHandlerStatic
{
    private RszFieldAccessorBase<List<object>> arrayAccessor = null!;

    private RszClass? catalogEntryClass;
    private RszClass? componentClass;
    public int SkipFieldCount { get; set; }

    private static readonly RszFieldAccessorFirstFallbacks<RszInstance> PrefabLinkField = new RszFieldAccessorFirstFallbacks<RszInstance>([
        f => f.original_type == "via.Prefab",
        f => f.type == RszFieldType.Object
    ]);
    private static readonly RszFieldAccessorFirst<uint> CatalogIdField = new RszFieldAccessorFirst<uint>(f => f.type == RszFieldType.U32);

    public static ResourceHandler Deserialize(ResourceConfig resource, ResourceConfigSerialized data, ContentWorkspace workspace)
    {
        return new ResourceProxyPrefabHandler() {
            Config = resource,
            Files = data.TargetFiles.ToList(),
            SkipFieldCount = data.GetParam<int>("skipFields", 0),
            arrayAccessor = data.GetDirectFieldAccessor<List<object>>(static f => f.array && f.type == RszFieldType.Object),
        };
    }

    public void UpdateCatalogEntry(CatalogPrefabResource resource, long id, ContentWorkspace workspace)
    {
        componentClass ??= Config.RszClass;
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
            if (catalogEntryClass == null) {
                Logger.Error("Unknown catalog class for resource " + Config);
                return;
            }
            resource.CatalogEntry = workspace.CreateRszInstance(catalogEntryClass);
            catFile.Modified = true;
        }

        if (PrefabLinkField.Get(resource.CatalogEntry) is not RszInstance viaPrefab) {
            PrefabLinkField.Set(resource.CatalogEntry, viaPrefab = workspace.CreateRszInstance(workspace.Env.Classes.Prefab));
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
            go = new ReeLib.Pfb.PfbGameObject() { Instance = workspace.CreateRszInstance(workspace.Env.Classes.GameObject) };
            go.Components.Add(workspace.CreateRszInstance(workspace.Env.Classes.Transform));
            pfb.GameObjects.Add(go);
            catFile.Modified = true;
        }

        var comp = go.Components.FirstOrDefault(c => c.RszClass == componentClass);
        if (comp == null) {
            go.Components.Add(comp = workspace.CreateRszInstance(componentClass));
            catFile.Modified = true;
        }
        go.Components[go.Components.IndexOf(comp)] = resource.Instance;
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

                dict[id] = new CatalogPrefabResource(Config, component, filepath) { CatalogEntry = item };
            }
        }
    }

    public override CatalogPrefabResource ApplyResourceData(ContentWorkspace workspace, IContentResource? resource, JsonNode? data)
    {
        componentClass ??= Config.RszClass;
        Debug.Assert(componentClass != null);
        var path = data?.GetValueKind() == System.Text.Json.JsonValueKind.String ? data.GetValue<string>() : null;
        if (resource is not CatalogPrefabResource pfbRes) {
            pfbRes = new CatalogPrefabResource(Config, workspace.CreateRszInstance(componentClass), Files[0]);
        }

        if (string.IsNullOrEmpty(path) && data?.GetValueKind() != System.Text.Json.JsonValueKind.String) {
            workspace.Diff.ApplyDiff(pfbRes.Instance, data);
        } else {
            Logger.Warn($"Straight strings are not currently accepted here: {path}");
        }
        return pfbRes;
    }

    public override IContentResource CreateResource(ContentWorkspace workspace, long id, JsonNode? initialData)
    {
        var res = ApplyResourceData(workspace, null, initialData);
        if (catalogEntryClass == null) throw new Exception();

        var idgen = Config.IDGeneratorRequired;
        var inst = workspace.CreateRszInstance(catalogEntryClass);
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
        res.CatalogEntry = inst;
        return res;
    }

    public override void ModifyResources(ContentWorkspace workspace, IEnumerable<KeyValuePair<long, IContentResource>> resources)
    {
        Debug.Assert(componentClass != null);
        var idgen = Config.IDGeneratorRequired;
        foreach (var (id, rawRes) in resources) {
            if (rawRes is not CatalogPrefabResource res) {
                continue;
            }

            UpdateCatalogEntry(res, id, workspace);
        }
    }
}
