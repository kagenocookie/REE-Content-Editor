using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;
using ContentEditor;
using ContentEditor.Core;
using ReeLib;

namespace ContentPatcher;

public class ResourceManager(PatchDataContainer config)
{
    private readonly Dictionary<string, ResourceData> resources = new();
    private readonly Dictionary<string, EntityData> entities = new();
    private readonly Dictionary<string, FileHandle> openFiles = new();
    private BundleManager? bundles;
    private ContentWorkspace workspace = null!;
    private Bundle? activeBundle;

    private readonly List<IFileLoader> FileLoaders = new();

    private sealed class ResourceData
    {
        public ClassConfig config;
        public Dictionary<long, IContentResource>? baseInstances;
        public Dictionary<long, IContentResource>? activeInstances;
        public readonly List<string> baseTypes = new(0);

        public ResourceData(ClassConfig config)
        {
            this.config = config;
        }
    }

    private sealed class EntityData
    {
        public EntityConfig config;
        public Dictionary<long, ResourceEntity>? instances;
        public readonly HashSet<long> activatedInstances = new();

        public EntityData(EntityConfig config)
        {
            this.config = config;
        }
    }

    public void Setup(ContentWorkspace workspace)
    {
        this.workspace = workspace;
        SetupFileLoaders();
        foreach (var (classname, patch) in config.Classes) {
            var data = new ResourceData(patch);
            resources[classname] = data;

            data.baseTypes.AddRange(resources.Where(kv => kv.Value.config.Subclasses?.ContainsKey(classname) == true).Select(kv => kv.Key));
        }

        foreach (var (type, patch) in config.Entities) {
            entities[type] = new EntityData(patch);

            foreach (var field in patch.Fields) {
                if (field is ICustomResourceField custom) {
                    var cfg = custom.CreateConfig();
                    resources[custom.ResourceIdentifier] = new ResourceData(cfg);
                }
            }
        }

        // foreach (var (type, cusType) in config.CustomTypes) {
        // }
    }

    private void SetupFileLoaders()
    {
        foreach (var type in typeof(UserFileLoader).Assembly.GetTypes()) {
            if (!type.IsAbstract && type.IsAssignableTo(typeof(IFileLoader)) && type.GetConstructor(Array.Empty<Type>()) != null) {
                var loader = (IFileLoader)Activator.CreateInstance(type)!;
                FileLoaders.Add(loader);
            }
        }
    }

    public void SetBundle(BundleManager bundleManager, Bundle? active)
    {
        ClearInstances();
        bundles = bundleManager;
        activeBundle = active;
    }

    private bool LoadAndApplyBundle(Bundle bundle, ResourceState state)
    {
        var changed = false;
        if (bundle.ResourceListing != null) {
            var bundleBasepath = workspace.BundleManager.GetBundleFolder(bundle);
            foreach (var (localFile, resInfo) in bundle.ResourceListing) {
                if (localFile.EndsWith(".pak")) {
                    Logger.Error($"PAK bundles are not yet supported for the patcher! (bundle: {bundle.Name})");
                    continue;
                }

                var file = ReadFileResource(resInfo.Target, null);
                if (file?.DiffHandler == null) {
                    // not diffable, give it an empty object just to mark it as not null
                    resInfo.Diff = new JsonObject();
                    resInfo.DiffTime = DateTime.UtcNow;
                    var fullLocalFilepath = Path.Combine(bundleBasepath, localFile);
                    if (File.Exists(fullLocalFilepath)) {
                        if (openFiles.Remove(resInfo.Target, out var previousFile)) {
                            previousFile.Dispose();
                        }
                        var local = ReadFileResource(fullLocalFilepath, resInfo.Target);
                        if (local != null) local.Modified = true;
                    }
                    continue;
                }

                if (resInfo.Diff == null) {
                    var fullLocalFilepath = Path.Combine(bundleBasepath, localFile);
                    var tempFile = FileHandle.FromDiskFilePath(fullLocalFilepath, file.Loader);
                    file.Loader.Load(workspace, tempFile);

                    // recalculate diff
                    changed = true;
                    resInfo.Diff = file.DiffHandler.FindDiff(tempFile);
                    resInfo.DiffTime = DateTime.UtcNow;
                    if (resInfo.Diff == null) {
                        // no changes
                        continue;
                    }
                }

                // apply diff
                file.DiffHandler.ApplyDiff(resInfo.Diff);
                file.Modified = true;
            }
        }

        var entityTypes = bundle.Entities.Select(e => e.Type).Distinct().ToList();
        foreach (var type in entityTypes) {
            var data = entities[type];
            LoadBundleEntitiesAsBase(bundle, type, data);
        }

        var modifiedResources = new HashSet<string>();
        foreach (var e in bundle.Entities) {
            if (e.Data == null) continue;
            var config = entities[e.Type];
            var resourceEntity = (config.instances!)[e.Id];

            foreach (var (f, data) in e.Data) {
                var realData = resourceEntity.Get(f);
                var field = config.config.GetField(f);
                if (field != null && realData != null) {
                    modifiedResources.Add(realData.ResourceIdentifier);
                }
            }
        }

        foreach (var type in modifiedResources) {
            if (!this.resources.TryGetValue(type, out var resourceData)) {
                // non-patchable resources (e.g. custom fields like new item icons)
                // should get transferred via the bundle's file copy mechanism
                continue;
            }
            var patcher = resourceData.config.Patcher;
            if (patcher != null && resourceData.baseInstances != null) {
                patcher.ModifyResources(workspace, resourceData.config, resourceData.baseInstances);
            }
        }

        return changed;
    }

    /// <summary>
    /// Loads all active bundle data into memory.
    /// </summary>
    public void LoadActiveBundle()
    {
        if (activeBundle == null) throw new NullReferenceException("activeBundle is null");

        if (activeBundle.DependsOn?.Count > 0) {
            // TODO load dependencies
            throw new NotImplementedException("Bundle dependencies not yet supported");
        }

        LoadAndApplyBundle(activeBundle, ResourceState.Active);
    }

    public void LoadBaseBundleData()
    {
        if (bundles == null) throw new NullReferenceException("no bundle manager was given");

        // TODO ideally we only load individual modified base resources
        // but this is good enough for now
        foreach (var bundle in bundles.ActiveBundles) {
            if (bundle == activeBundle) continue;

            LoadAndApplyBundle(bundle, ResourceState.Base);
        }
    }

    public void ClearInstances()
    {
        foreach (var res in resources.Values) {
            res.activeInstances?.Clear();
            res.activeInstances = null;
            res.baseInstances?.Clear();
            res.baseInstances = null;
        }

        foreach (var ee in entities.Values) {
            ee.instances?.Clear();
            ee.instances = null;
            ee.activatedInstances.Clear();
        }
    }

    public IEnumerable<KeyValuePair<long, IContentResource>> GetResourceInstances(string type)
    {
        if (resources.TryGetValue(type, out var data)) {
            if (data.baseInstances == null) {
                data.baseInstances = new();
                ReadObjectSourceData(data.config, data);
            }
            return data.baseInstances;
        }
        throw new NotImplementedException("Requested unknown resource " + type);
    }

    private IContentResource? CreateEntityResourceInternal(ResourceEntity entity, CustomField field, ResourceState state, ResourceData data, JsonNode? initialData)
    {
        var resourceConfig = data.config;
        var resourceId = 0L;
        IContentResource? fieldResource = null;

        if (field is ICustomResourceField customField) {
            (resourceId, fieldResource) = customField.CreateResource(workspace, resourceConfig, entity, initialData);
            entity.Set(field.name, fieldResource);
        } else if (resourceConfig.Patcher != null) {
            (resourceId, fieldResource) = resourceConfig.Patcher.CreateResource(workspace, resourceConfig, entity, initialData);
            entity.Set(field.name, fieldResource);
        }
        if (fieldResource == null) return null;

        if (openFiles.TryGetValue(fieldResource.FilePath, out var file)) {
            file.Modified = true;
        } else {
            throw new Exception("New resource file should've been opened, wtf?");
        }
        AddResource(fieldResource.ResourceIdentifier, resourceId, fieldResource, state);
        return fieldResource;
    }

    public TResourceType CreateEntityResource<TResourceType>(ResourceEntity entity, CustomField field, ResourceState state, string? resourceKeyOverride = null) where TResourceType : IContentResource
    {
        if (resources.TryGetValue(resourceKeyOverride ?? field.ResourceIdentifier, out var data)) {
            if (data.baseInstances == null) {
                data.baseInstances = new();
                ReadObjectSourceData(data.config, data);
            }

            if (data.config.Patcher != null) {
                return (TResourceType?)CreateEntityResourceInternal(entity, field, state, data, null)
                    ?? throw new Exception($"Failed to create entity {entity} field {field} resource");
            }

            throw new NotImplementedException($"Unable to create new entity {entity} field {field} resource");
        }
        throw new NotImplementedException();
    }

    public void AddResource(string resourceKey, long id, IContentResource resource, ResourceState state)
    {
        if (resources.TryGetValue(resourceKey, out var data)) {
            if (data.baseInstances == null) {
                data.baseInstances = new();
                ReadObjectSourceData(data.config, data);
            }

            var instances = (state == ResourceState.Base ? data.baseInstances : data.activeInstances ??= new());
            instances.Add(id, resource);
            var resid = resource.ResourceIdentifier;
            if (!string.IsNullOrEmpty(resid) && resid != resourceKey && resources.TryGetValue(resid, out var sub)) {
                instances = (state == ResourceState.Base ? sub.baseInstances ??= new() : sub.activeInstances ??= new());
                instances.Add(id, resource);
            }

            foreach (var baseclass in data.baseTypes) {
                var bt = resources[baseclass];
                if (bt.baseInstances == null) ReadObjectSourceData(data.config, bt);
                instances = (state == ResourceState.Base ? bt.baseInstances! : bt.activeInstances ??= new());
                instances.Add(id, resource);
            }
        }
    }

    /// <summary>
    /// Gets the base resource of the given type and id. This instance will include original game data with all dependency bundles applied on top, but not the actively edited bundle.
    /// </summary>
    public IContentResource? GetBaseResourceInstance(string type, long id)
    {
        if (resources.TryGetValue(type, out var data)) {
            if (data.baseInstances == null) {
                GetResourceInstances(type);
            }
            return data.baseInstances?.GetValueOrDefault(id);
        }
        return null;
    }

    /// <summary>
    /// Gets the active resource of the given type and id. This instance is a copy of the base data with the active edited bundle applied on top.
    /// </summary>
    public IContentResource? GetActiveResourceInstance(string type, long id)
    {
        if (resources.TryGetValue(type, out var data)) {
            if (data.baseInstances == null) {
                GetResourceInstances(type);
            }
            data.activeInstances ??= new();
            if (!data.activeInstances.TryGetValue(id, out var active)) {
                if (data.baseInstances!.TryGetValue(id, out active)) {
                    data.activeInstances[id] = active = active.Clone();
                }
            }

            return active;
        }
        return null;
    }

    /// <summary>
    /// Calls <see cref="GetBaseResourceInstance(string, long)"/> or <see cref="GetActiveEntityInstance(string, long)"/> depending on the requested resource state.
    /// </summary>
    public IContentResource? GetResourceInstance(string type, long id, ResourceState state)
    {
        return state == ResourceState.Base ? GetBaseResourceInstance(type, id) : GetActiveResourceInstance(type, id);
    }

    private Dictionary<long, ResourceEntity> LoadBaseEntityInstances(string type, EntityData data)
    {
        var dict = new Dictionary<long, ResourceEntity>();
        List<ResourceEntity>? newEntities = null;
        foreach (var field in data.config.Fields) {
            if (field is not IMainField mainField) continue;

            foreach (var (id, instance) in mainField.FetchInstances(this)) {
                if (!dict.TryGetValue(id, out var entity)) {
                    dict[id] = entity = new ResourceEntity(id, type, data.config);
                    newEntities ??= new();
                    newEntities.Add(entity);
                }

                entity.Set(field.name, instance);
            }
        }

        foreach (var (id, entity) in dict) {
            foreach (var field in data.config.Fields) {
                if (field is IMainField) continue;

                var fieldValue = field.FetchResource(this, entity, ResourceState.Base);
                entity.Set(field.name, fieldValue);
            }
        }

        if (newEntities != null) {
            // generate new entity strings in a separate step so we have all initial fields ready
            foreach (var entity in newEntities) {
                entity.Label = data.config.StringFormatter?.GetString(entity) ?? $"{type} {entity.Id}";
            }
        }

        return dict;
    }

    private void LoadBundleEntitiesAsBase(Bundle bundle, string type, EntityData data)
    {
        data.instances ??= LoadBaseEntityInstances(type, data);

        foreach (var sourceEntity in bundle.GetEntities(type)) {
            if (!data.instances.TryGetValue(sourceEntity.Id, out var entity)) {
                data.instances[sourceEntity.Id] = entity = new ResourceEntity(sourceEntity, data.config);
                entity.LoadResources(this, ResourceState.Base);
            } else {
                entity.Data = sourceEntity.Data;
            }

            entity.ApplyDataValues(workspace, ResourceState.Base);
        }
    }

    private Dictionary<long, ResourceEntity> LoadBaseBundleEntities(string type, EntityData data)
    {
        data.instances = LoadBaseEntityInstances(type, data);

        if (bundles != null) {
            foreach (var bundle in bundles.ActiveBundles) {
                if (bundle == activeBundle) continue;

                LoadBundleEntitiesAsBase(bundle, type, data);

                // foreach (var sourceEntity in bundle.GetEntities(type)) {
                //     if (!data.instances.TryGetValue(sourceEntity.Id, out var entity)) {
                //         data.instances[sourceEntity.Id] = entity = new ResourceEntity(sourceEntity, data.config);
                //         entity.LoadResources(this, ResourceState.Base);
                //     } else {
                //         entity.Data = sourceEntity.Data;
                //     }

                //     entity.ApplyValues(workspace, entity, ResourceState.Base);
                // }
            }
        }

        return data.instances;
    }

    private void LoadEntities(string type, EntityData data)
    {
        data.instances ??= LoadBaseBundleEntities(type, data);

        if (bundles == null || activeBundle == null) {
            return;
        }

        foreach (var sourceEntity in activeBundle.GetEntities(type)) {
            if (!data.instances.TryGetValue(sourceEntity.Id, out var entity)) {
                entity = new ResourceEntity(sourceEntity, data.config);
                data.instances[sourceEntity.Id] = entity = new ResourceEntity(sourceEntity, data.config);
            } else {
                entity.Data = sourceEntity.Data;
            }
            entity.LoadResources(this, ResourceState.Active);
            entity.ApplyDataValues(workspace, ResourceState.Active);
        }
    }

    public ResourceEntity CreateEntity(string type, long? sourceEntityId)
    {
        if (!entities.TryGetValue(type, out var data)) {
            throw new ArgumentException("Unknown entity type " + type, nameof(type));
        }

        if (data.config.CustomIDRange == null) {
            throw new Exception($"Entity type {type} does not have a custom ID range defined");
        }

        var sourceEntity = sourceEntityId == null ? null : data.instances![sourceEntityId.Value];

        int attempts = 100;
        long id;
        do {
            id = Random.Shared.NextInt64(data.config.CustomIDRange[0], data.config.CustomIDRange[1]);
            // TODO verify uniqueness with inactive bundles as well
            // TODO use bundle-defined initial IDs
            if (attempts-- <= 0) {
                throw new Exception($"Could not generate a new ID for entity type {type}");
            }
        } while (data.instances!.ContainsKey(id) == true);

        var entity = new ResourceEntity(id, type, data.config);
        foreach (var field in data.config.Fields) {
            if (field.Condition?.IsEnabled(entity) == false) {
                continue;
            }

            var resourceData = resources[field.ResourceIdentifier];
            IContentResource? fieldResource = null;
            if (sourceEntity != null && sourceEntity.Get(field.name) is IContentResource src) {
                resourceData = resources[src.ResourceIdentifier];
                fieldResource = CreateEntityResourceInternal(entity, field, ResourceState.Active, resourceData, src.ToJson(workspace.Env));
            } else if (field.IsRequired) {
                var resource = CreateEntityResourceInternal(entity, field, ResourceState.Active, resourceData, null);
                if (resource == null) {
                    throw new Exception($"Could not create field value for entity {entity} field {field}");
                }
            }
        }
        data.instances[id] = entity;
        data.activatedInstances.Add(id);
        if (activeBundle != null) {
            activeBundle.Entities.Add(entity);
        }
        return entity;
    }

    public IEnumerable<KeyValuePair<long, ResourceEntity>> GetEntityInstances(string type)
    {
        if (entities.TryGetValue(type, out var data)) {
            if (data.instances == null) {
                LoadEntities(type, data);
            }

            return data.instances!;
        }
        return [];
    }

    public ResourceEntity? GetActiveEntityInstance(string type, long entityId)
    {
        if (entities.TryGetValue(type, out var data)) {
            if (data.instances == null) {
                LoadEntities(type, data);
            }

            if (data.instances!.TryGetValue(entityId, out var entity)) {
                if (data.activatedInstances.Add(entityId)) {
                    entity.LoadResources(this, ResourceState.Active);
                }
                return entity;
            }

            Logger.Info($"Entity not found: {type} {entityId}");
        }

        return null;
    }

    private void ReadObjectSourceData(ClassConfig config, ResourceData data)
    {
        data.baseInstances ??= new();
        config.Patcher?.ReadResources(workspace, config, data.baseInstances);

        if (config.Subclasses != null) {
            foreach (var (subkey, sub) in config.Subclasses) {
                if (sub.Patcher != null) {
                    var subdata = resources[sub.Patcher.ResourceKey];
                    sub.Patcher.ReadResources(workspace, config, subdata.baseInstances ??= new());
                    foreach (var (id, ss) in subdata.baseInstances) {
                        data.baseInstances[id] = ss;
                    }
                }
            }
        }
    }

    public bool TryGetOrLoadFile(string filename, [MaybeNullWhen(false)] out FileHandle fileHandle)
    {
        fileHandle = ReadFileResource(filename, null);
        return fileHandle != null;
    }

    private FileHandle? ReadFileResource(string filepath, string? nativePath)
    {
        filepath = workspace.Env.PrependBasePath(filepath);
        if (openFiles.TryGetValue(nativePath ?? filepath, out var handle)) {
            return handle;
        }

        var stream = workspace.Env.FindSingleFile(filepath, out var resolvedFilename);
        if (stream == null) {
            return null;
        }

        handle = CreateFileHandleInternal(filepath, nativePath, stream);
        return handle;
    }

    private FileHandle? CreateFileHandleInternal(string filepath, string? nativePath, Stream stream)
    {
        var format = PathUtils.ParseFileFormat(filepath);
        var handleType = stream is MemoryStream ? FileHandleType.InMemoryFile : FileHandleType.DiskFile;

        IFileLoader? loader = null;
        foreach (var candidate in FileLoaders) {
            if (candidate.CanHandleFile(filepath, format)) {
                loader = candidate;
                break;
            }
        }
        loader ??= UnknownStreamFileLoader.Instance;

        var handle = new FileHandle(filepath, stream.ToMemoryStream(forceCopy: true), handleType, loader) {
            NativePath = nativePath ?? (filepath.IsNativePath() ? filepath : null),
        };

        if (!loader.Load(workspace, handle)) {
            Logger.Error($"Failed to load file {filepath}");
            return null;
        }
        handle.DiffHandler = handle.Loader.CreateDiffHandler();
        handle.DiffHandler?.LoadBase(workspace, handle);
        // var file = handler.LoadBase(workspace, filepath);
        openFiles.Add(nativePath ?? filepath, handle);
        return handle;
    }

    public FileHandle CreateFileHandle(string filepath, string? nativePath, Stream stream)
    {
        var handle = this.CreateFileHandleInternal(filepath, nativePath, stream);
        if (handle == null) {
            throw new NotSupportedException();
        }
        return handle;
    }

    public TFileType ReadFileResource<TFileType>(string filepath, bool markModified = false) where TFileType : BaseFile
    {
        var resource = ReadFileResource(filepath, null);
        if (resource == null) {
            throw new NotSupportedException();
        }
        var file = resource.GetContent<TFileType>();

        if (markModified) {
            resource.Modified = true;
        }

        return file;
    }

    public void MarkFileResourceModified(string filepath, bool markModified)
    {
        filepath = workspace.Env.PrependBasePath(filepath);
        if (openFiles.TryGetValue(filepath, out var fileResource)) {
            fileResource.Modified = markModified;
        }
    }

    public FileHandle GetFileHandle(string filepath)
    {
        var resource = ReadFileResource(filepath, null);
        if (resource == null) {
            throw new NotSupportedException();
        }
        return resource;
    }

    public TFileType? GetOpenFile<TFileType>(string filepath, bool markModified = false) where TFileType : BaseFile
    {
        if (openFiles?.TryGetValue(workspace.Env.PrependBasePath(filepath), out var handle) == true) {
            var file = handle.GetContent<TFileType>();
            if (markModified) {
                handle.Modified = true;
            }

            return file;
        }

        return null;
    }

    public IEnumerable<FileHandle> GetOpenFiles() => openFiles.Values;

    public IEnumerable<FileHandle> GetModifiedResourceFiles()
    {
        foreach (var file in openFiles.Values) {
            if (file.Modified) {
                yield return file;
            }
        }
    }

    public void CloseFile(FileHandle file)
    {
        openFiles.Remove(file.Filepath);
        foreach (var rf in file.References) {
            rf.Close();
        }
        file.Dispose();
    }
}

public enum ResourceState
{
    Base,
    Active,
}
