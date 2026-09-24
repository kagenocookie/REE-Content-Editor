using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.Json.Nodes;
using ContentEditor;
using ContentEditor.App.FileLoaders;
using ContentEditor.Core;
using ReeLib;
using ReeLib.Common;

namespace ContentPatcher;

public sealed class ResourceManager(PatchConfig config) : IDisposable
{
    private readonly Dictionary<string, ResourceData> resources = new();
    private readonly Dictionary<string, EntityData> entities = new();
    private readonly ConcurrentDictionary<string, FileHandle> openFiles = new(HybridPakPathEqualityComparer.Instance);
    private readonly Dictionary<string, string> targetPathRemaps = new(HybridPakPathEqualityComparer.Instance);
    private BundleManager? bundles;
    private ContentWorkspace workspace = null!;
    private Bundle? activeBundle;

    private readonly List<IFileLoader> FileLoaders = new();

    private readonly ConcurrentQueue<(string filename, IAsyncResourceReceiver receiver, Action<FileHandle> callback)> backgroundQueue = new();

    private static readonly HashSet<KnownFileFormats> PreloadFormats = [
        KnownFileFormats.Scene,
        KnownFileFormats.Prefab,
        KnownFileFormats.Mesh,
        KnownFileFormats.CollisionMesh,
        KnownFileFormats.RequestSetCollider,
        KnownFileFormats.MeshMaterial,
    ];

    public bool HasAnyActivatedEntities => entities.Values.Any(entityData => entityData.activatedInstances.Count != 0);

    private sealed class ResourceData
    {
        public ResourceConfig config;
        public Dictionary<long, IContentResource>? baseInstances;
        public Dictionary<long, IContentResource>? activeInstances;

        public ResourceData(ResourceConfig config)
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

        foreach (var (type, res) in config.Resources) {
            var data = new ResourceData(res);
            resources[type] = data;
        }

        foreach (var (type, patch) in config.Entities) {
            entities[type] = new EntityData(patch);

            // foreach (var field in patch.Fields) {
            //     if (field.ValueHandler is ICustomEntityField custom && custom.ResourceTypeId != null) {
            //         var cfg = custom.CreateConfig();
            //         resources[custom.ResourceTypeId] = new ResourceData(cfg);
            //     }
            // }
        }
    }

    private void SetupFileLoaders()
    {
        SetupFileLoaders(typeof(UserFileLoader).Assembly);
    }

    public void SetupFileLoaders(Assembly assembly)
    {
        foreach (var type in assembly.GetTypes()) {
            if (!type.IsAbstract && type.IsAssignableTo(typeof(IFileLoader)) && type.GetConstructor(Array.Empty<Type>()) != null) {
                var loader = (IFileLoader)Activator.CreateInstance(type)!;
                FileLoaders.Add(loader);
            }
        }
        FileLoaders.Sort(FileLoaderPriorityComparer.Instance);
    }

    public void RemoveFileLoader<ILoader>() where ILoader : IFileLoader
    {
        foreach (var loader in FileLoaders) {
            if (loader.GetType() == typeof(ILoader)) {
                FileLoaders.Remove(loader);
                return;
            }
        }
    }

    private class FileLoaderPriorityComparer : IComparer<IFileLoader>
    {
        public static readonly FileLoaderPriorityComparer Instance = new();
        public int Compare(IFileLoader? x, IFileLoader? y)
        {
            if (x == null || y == null) return 0;
            return x.Priority.CompareTo(y.Priority);
        }
    }

    public void SetBundle(BundleManager bundleManager, Bundle? active)
    {
        ClearInstances();
        bundles = bundleManager;
        activeBundle = active;
    }

    private void LoadAndApplyBundle(Bundle bundle, ResourceState state)
    {
        var success = true;
        if (bundle.HasFiles) {
            var bundleBasepath = workspace.BundleManager.GetBundleFolder(bundle);
            foreach (var (localFile, resInfo) in bundle.ResourcesEntries) {
                if (localFile.EndsWith(".pak")) {
                    Logger.Error($"PAK bundles are not yet supported for the patcher! (bundle: {bundle.Name})");
                    continue;
                }

                var fileSuccess = ApplyBundleFile(bundleBasepath, localFile, resInfo, true);
                success = fileSuccess && success;
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
                    modifiedResources.Add(realData.ResourceType.Type);
                }
            }
        }

        foreach (var type in modifiedResources) {
            if (!this.resources.TryGetValue(type, out var resourceData)) {
                // non-patchable resources (e.g. custom fields like new item icons)
                // should get transferred via the bundle's file copy mechanism
                continue;
            }
            var patcher = resourceData.config.Resource;
            if (patcher != null && resourceData.baseInstances != null) {
                patcher.ModifyResources(workspace, resourceData.baseInstances);
            }
        }

        if (!success) {
            Logger.Error($"Loading of bundle {bundle.Name} was not fully successful.");
        }
    }

    private bool ApplyBundleFile(string bundleBasepath, string localFile, ResourceListItem resourceEntry, bool markModifiedOnChange)
    {
        var fullLocalFilepath = Path.Combine(bundleBasepath, localFile);
        if (resourceEntry.Replace) {
            if (File.Exists(fullLocalFilepath)) {
                if (TryForceLoadFile(fullLocalFilepath, out var local, resourceEntry.Target)) {
                    openFiles[resourceEntry.Target] = local;
                    return true;
                }

                // pray that it's a partially patchable file and we have a file to base changes on
                Logger.Error($"Could not load source file marked as Replace: {fullLocalFilepath}. Attempting partial load...");
            }
        }

        var file = ReadOrGetFileResource(resourceEntry.Target, null);
        if (file?.DiffHandler == null) {
            // not diffable, give it an empty diff object just to mark it as not null
            resourceEntry.Diff = new JsonObject();
            resourceEntry.DiffTime = DateTime.UtcNow;
            if (File.Exists(fullLocalFilepath)) {
                if (openFiles.Remove(resourceEntry.Target, out var previousFile)) {
                    previousFile.Dispose();
                }
                var local = ReadOrGetFileResource(fullLocalFilepath, resourceEntry.Target);
                if (local != null && markModifiedOnChange) local.Modified = true;
            }
            return true;
        }

        if (resourceEntry.Diff == null) {
            if (!File.Exists(fullLocalFilepath)) {
                Logger.Error("File not found: " + fullLocalFilepath);
                return false;
            }
            var tempFile = FileHandle.FromDiskFilePath(fullLocalFilepath, file.Loader, true);
            if (tempFile == null) return false;
            var resource = file.Loader.Load(workspace, tempFile);
            if (resource == null) {
                Logger.Error("File could not be loaded: " + fullLocalFilepath);
                return false;
            }
            tempFile.Resource = resource;

            // recalculate diff
            resourceEntry.Diff = file.DiffHandler.FindDiff(tempFile);
            resourceEntry.DiffTime = DateTime.UtcNow;
            if (resourceEntry.Diff == null) {
                // no changes
                return true;
            }
        }

        // apply diff
        try {
            file.DiffHandler.ApplyDiff(resourceEntry.Diff);
            if (markModifiedOnChange) file.Modified = true;
            return true;
        } catch (Exception e) {
            Logger.Error(e, $"Failed to apply partial patch for file {localFile}. This could indicate issues with the patch generation or unsupported file edits. Attempting simple replacement instead ...");

            if (File.Exists(fullLocalFilepath)) {
                var local = ReadOrGetFileResource(fullLocalFilepath, resourceEntry.Target);
                if (local != null) {
                    Logger.Info($"Will attempt simple file replacement instead, previous error can be ignored");
                    openFiles[resourceEntry.Target] = local;
                    return true;
                }

                Logger.Error($"Could not load source file {fullLocalFilepath}.");
            } else {
                Logger.Error($"File not found for full replacement: {fullLocalFilepath}.");
            }

            return false;
        }
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

    public IContentResource CreateEntityField(ResourceEntity entity, EntityField field, ResourceState state)
    {
        return CreateEntityFieldInternal(entity, field, state, field.Config, null)
            ?? throw new NotImplementedException($"Unable to create new entity {entity} field {field} resource");
    }

    private IContentResource? CreateEntityFieldInternal(ResourceEntity entity, EntityField field, ResourceState state, ResourceConfig resourceConfig, JsonNode? initialData)
    {
        IContentResource? fieldResource;
        if (field.ValueHandler is CustomEntityFieldHandler customField) {
            var resourceId = field.GetIDForEntity(entity);
            if (resourceId == -1) resourceId = GetRandomUniqueResourceID(resources[field.Config.Type], state);
            fieldResource = customField.ApplyValue(workspace, null, initialData, entity, state);
            entity.Set(field.name, fieldResource);
            if (fieldResource == null) return null;

            if (fieldResource.FileResourcePath == null) {
                // ignore - there's no file here
            } else {
                var filepath = PreprocessTargetFilepath(fieldResource.FileResourcePath);
                if (filepath != null && openFiles.TryGetValue(filepath, out var file)) {
                    file.Modified = true;
                } else {
                    throw new Exception("New resource's file should've been opened, wtf?");
                }
            }
            AddResource(resourceConfig.Type, resourceId, fieldResource, state);
        } else {
            var resourceId = field.GetIDForEntity(entity);
            fieldResource = CreateResourceInternal(resourceId, resourceConfig, state, initialData);
            entity.Set(field.name, fieldResource);
        }
        return fieldResource;
    }

    public IContentResource CreateSubResource(ResourceEntity entity, EntityField field, ResourceState state, ResourceConfig subresourceType, JsonNode? initialData)
    {
        Debug.Assert(field.Config.Subtypes?.Any(kv => kv.Value.resource == subresourceType) == true);
        var baseResource = entity.Get(field.name);
        Debug.Assert(baseResource != null);

        var id = entity.GetFieldId(field.name);
        var sub = subresourceType.Resource.CreateResource(workspace, id, initialData);
        return sub;
    }

    private long GetRandomUniqueResourceID(ResourceData data, ResourceState state)
    {
        if (data.baseInstances == null) {
            data.baseInstances = new();
            ReadObjectSourceData(data.config, data);
        }

        long id;
        var idRange = data.config.CustomIDRange;
        if (idRange == null) {
            throw new Exception($"Resource type {data.config} does not have a custom ID range defined");
        }

        var instanceList = state == ResourceState.Active ? data.activeInstances ?? data.baseInstances! : data.baseInstances!;
        int attempts = 100;
        do {
            id = Random.Shared.NextInt64(idRange[0], idRange[1]);
            // TODO verify uniqueness with inactive bundles as well
            // TODO use bundle-defined initial IDs
            if (attempts-- <= 0) {
                throw new Exception($"Could not generate a new ID for resource type {data.config}");
            }
        } while (instanceList.ContainsKey(id) == true);
        return id;
    }

    private IContentResource CreateResourceInternal(long resourceId, ResourceConfig resource, ResourceState state, JsonNode? initialData)
    {
        var fieldResource = resource.Resource.CreateResource(workspace, resourceId, initialData);
        if (fieldResource.FileResourcePath == null) {
            // ignore - there's no file here
        } else if (TryResolveGameFile(fieldResource.FileResourcePath, out var file)) {
            file.Modified = true;
        } else {
            throw new Exception("New resource file should've been opened, wtf?");
        }
        AddResource(resource.Type, resourceId, fieldResource, state);
        return fieldResource;
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

    public IContentResource? GetActiveResourceInstance(ResourceEntity entity, IContentResource resource)
    {
        if (resources.TryGetValue(resource.ResourceType.Type, out var data)) {
            if (data.baseInstances == null) {
                GetResourceInstances(resource.ResourceType.Type);
            }
            data.activeInstances ??= new();
            // if (!data.activeInstances.TryGetValue(resource, out var active)) {
            //     if (data.baseInstances!.TryGetValue(id, out active)) {
            //         data.activeInstances[id] = active = active.Clone();
            //     }
            // }

            // return active;
        }
        // resource.ResourceTypeID
        return resource.Clone();
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
        var entityDict = new Dictionary<long, ResourceEntity>();
        List<ResourceEntity>? newEntities = null;
        if (data.config.ZeroEntity != null) {
            var zero = new ResourceEntity(data.config.ZeroEntity.id, type, data.config) {
                Label = data.config.ZeroEntity.label ?? "None"
            };
            entityDict[data.config.ZeroEntity.id] = zero;
            newEntities = new();
        }
        foreach (var (primaryResourceId, primaryResource) in GetResourceInstances(data.config.PrimaryField.Config.Type)) {
            var entity = new ResourceEntity(primaryResourceId, type, data.config);
            entity.Set(data.config.PrimaryField.name, primaryResource);
            if (data.config.IDField != data.config.PrimaryField) {
                if (data.config.IDField.ValueHandler is CustomEntityFieldHandler custom) {
                    var idres = custom.FetchResource(workspace, entity, -1, ResourceState.Base);
                    // note: 0 entries are sometimes expected (e.g. DD2 app.TopsStyle.None), using -1 as invalid instead
                    if (idres is IAddressableContentResource addrId && addrId.ID != -1) {
                        entity.Id = addrId.ID;
                    } else {
                        Logger.Warn("Failed to determine ID for entity " + entity);
                    }
                    entity.Set(data.config.IDField.name, idres);
                }
            }

            if (entityDict.TryGetValue(entity.Id, out var previousEntity)) {
                if (entity.Id == data.config.ZeroEntity?.id) {
                    entityDict[entity.Id] = entity;
                    newEntities ??= new();
                    newEntities.Add(entity);
                } else {
                    Logger.Warn($"Detected potentially duplicate {type} entity: ID {entity.Id}");
                    entity = previousEntity;
                    if (data.config.IDField != data.config.PrimaryField) {
                        previousEntity.Set(data.config.IDField.name, entity.Get(data.config.IDField.name));
                    }
                }
            } else {
                newEntities ??= new();
                newEntities.Add(entity);
            }

            // setup the rest of the fields in definition order
            foreach (var field in data.config.Fields) {
                if (field == data.config.PrimaryField || field == data.config.IDField) {
                    continue;
                }

                var fieldId = field.GetIDForEntity(entity);
                var fieldValue = field.ValueHandler.FetchResource(workspace, entity, fieldId, ResourceState.Base);
                entity.Set(field.name, fieldValue);
            }
        }

        if (newEntities != null) {
            // generate new entity strings in a separate step so we have all initial fields ready
            foreach (var entity in newEntities) {
                entityDict[entity.Id] = entity;
                entity.Label = data.config.StringFormatter?.GetString(entity) ?? $"{type} {entity.Id}";
            }
        }

        return entityDict;
    }

    private void LoadBundleEntitiesAsBase(Bundle bundle, string type, EntityData data)
    {
        data.instances ??= LoadBaseEntityInstances(type, data);

        foreach (var sourceEntity in bundle.GetEntities(type)) {
            if (!data.instances.TryGetValue(sourceEntity.Id, out var entity)) {
                data.instances[sourceEntity.Id] = entity = new ResourceEntity(sourceEntity, data.config);
                LoadSingleEntityResources(entity, ResourceState.Base);
            } else {
                entity.Data = sourceEntity.Data;
            }

            entity.ApplyDataValues(workspace, ResourceState.Base);
            data.config.PrimaryEnum?.UpdateEnum(workspace, entity);
        }
    }

    /// <summary>
    /// Fetches all referenced resources with the given state. If Active state, resources will be copied from the base state data if found.
    /// </summary>
    private void LoadSingleEntityResources(ResourceEntity entity, ResourceState state)
    {
        var idField = entity.Config.IDField;
        if (entity.Get(idField.name) == null) {
            entity.FieldValues[idField.name] = idField.ValueHandler.FetchResource(workspace, entity, -1, state);
        }
        foreach (var field in entity.Config.Fields) {
            if (field == idField) {
                continue;
            }
            if (field.Condition?.IsEnabled(entity) == false) {
                continue;
            }

            var fieldId = field.IdField == null ? entity.Id : Convert.ToInt64(field.IdField.Get(entity));

            var value = entity.Get(field.name);
            if (value != null) {
                // TODO: bypass the value handler and go directly for the resource?
                // how do we determine the resource ID (for cases where it's different from the entity id)

                entity.FieldValues[field.name] = field.ValueHandler.FetchResource(workspace, entity, fieldId, state);
            }

            // note: I'm not sure if the custom field case needs to be handled here
            // if (field.ValueHandler is CustomEntityFieldHandler custom) {
            //     var (resid, res) = custom.LoadValue(workspace, entity, state);
            //     if (resid == -1 || res == null) {
            //         // fall back to default resource fetch
            //         entity.FieldValues[field.name] = field.ValueHandler.FetchResource(workspace, entity, fieldId, state);
            //         continue;
            //     }
            //     if (field == entity.Config.IDField && entity.Id != resid) {
            //         entity.Id = resid;
            //     }
            //     entity.FieldValues[field.name] = res;
            // }
        }
    }

    private Dictionary<long, ResourceEntity> LoadBaseBundleEntities(string type, EntityData data)
    {
        data.instances = LoadBaseEntityInstances(type, data);

        if (bundles != null) {
            foreach (var bundle in bundles.ActiveBundles) {
                if (bundle == activeBundle) continue;

                LoadBundleEntitiesAsBase(bundle, type, data);
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
                data.instances[sourceEntity.Id] = entity;
            } else {
                entity.Data = sourceEntity.Data;
            }
            LoadSingleEntityResources(entity, ResourceState.Active);
            entity.ApplyDataValues(workspace, ResourceState.Active);
            data.config.PrimaryEnum?.UpdateEnum(workspace, entity);
        }
    }

    public ResourceEntity CreateEntity(string type, long sourceEntityId)
    {
        if (!entities.TryGetValue(type, out var data)) {
            throw new ArgumentException("Unknown entity type " + type, nameof(type));
        }

        if (data.config.PrimaryField == null) {
            throw new Exception($"Entity type {type} does not have a primary field");
        }

        var resourceKey = data.config.PrimaryField.ResourceType;
        if (resourceKey == null) {
            throw new Exception($"Entity type {type} primary field {data.config.PrimaryField} is not instantiable");
        }

        var sourceEntity = data.instances![sourceEntityId];
        var jsonEntity = sourceEntity.ToJson(workspace.Env).Data!;
        return CreateEntity(type, jsonEntity);
    }

    public ResourceEntity CreateEntity(string type, Entity sourceEntity)
    {
        if (sourceEntity.Type != type) {
            throw new Exception($"Mismatched source entity {sourceEntity} for new entity type {type}");
        }

        return CreateEntity(type, sourceEntity.Data);
    }

    public ResourceEntity CreateEntity(string type, JsonObject initialData)
    {
        var dict = new Dictionary<string, JsonNode?>(initialData);
        return CreateEntity(type, dict);
    }

    public ResourceEntity CreateEntity(string type, Dictionary<string, JsonNode?>? initialData = null)
    {
        if (!entities.TryGetValue(type, out var data)) {
            throw new ArgumentException("Unknown entity type " + type, nameof(type));
        }

        if (data.config.PrimaryField == null) {
            throw new Exception($"Entity type {type} does not have a primary field");
        }

        var primaryField = data.config.PrimaryField;
        var idField = data.config.IDField;

        var primaryId = GetRandomUniqueResourceID(resources[primaryField.Config.Type], ResourceState.Active);
        var primaryResource = CreateResourceInternal(primaryId, primaryField.Config, ResourceState.Active, initialData?.GetValueOrDefault(primaryField.name));
        ResourceEntity entity;
        if (idField != null && idField != primaryField) {
            if (idField.ResourceType == null) throw new Exception($"ID field must have a resource type ID {idField}");
            var id = GetRandomUniqueResourceID(resources[idField.Config.Type], ResourceState.Active);

            entity = new ResourceEntity(id, type, data.config);
            entity.Set(primaryField.name, primaryResource);

            var idResource = CreateEntityFieldInternal(entity, idField, ResourceState.Active, idField.Config, initialData?.GetValueOrDefault(idField.name));
            entity.Set(idField.name, idResource);
        } else {
            entity = new ResourceEntity(primaryId, type, data.config);
            entity.Set(primaryField.name, primaryResource);
        }

        foreach (var field in data.config.Fields) {
            if (field == primaryField || field == idField) {
                // we already instantiated this one, skip it
                continue;
            }
            if (field.Condition?.IsEnabled(entity) == false) {
                continue;
            }

            IContentResource? fieldResource = null;
            if (initialData != null && initialData.TryGetValue(field.name, out var src)) {
                fieldResource = CreateEntityFieldInternal(entity, field, ResourceState.Active, field.Config, src);
            } else if (field.IsRequired) {
                var resource = CreateEntityFieldInternal(entity, field, ResourceState.Active, field.Config, null);
                if (resource == null) {
                    throw new Exception($"Could not create field value for entity {entity} field {field}");
                }
            }
        }

        entity.Label = data.config.StringFormatter?.GetString(entity) ?? $"{type} {entity.Id}";
        data.instances![entity.Id] = entity;
        data.activatedInstances.Add(entity.Id);
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
                    LoadSingleEntityResources(entity, ResourceState.Active);
                }
                return entity;
            }

            Logger.Info($"Entity not found: {type} {entityId}");
        }

        return null;
    }

    private void ReadObjectSourceData(ResourceConfig config, ResourceData data)
    {
        data.baseInstances ??= new();
        config.Resource?.ReadResources(workspace, data.baseInstances);
    }

    /// <summary>
    /// Attempt to load a file from the given file path. Can be a disk path or a native or internal path. Does not handle custom bundle files unless a full native path is given.
    /// </summary>
    /// <param name="filepath"></param>
    /// <param name="fileHandle"></param>
    /// <returns></returns>
    public bool TryGetOrLoadFile(string filepath, [MaybeNullWhen(false)] out FileHandle fileHandle)
    {
        fileHandle = ReadOrGetFileResource(filepath, null);
        return fileHandle != null;
    }

    /// <summary>
    /// Loads a disk file fully ignoring any in-memory, PAK or bundle changes, and does not store the file in the resource manager.
    /// The caller must take care of disposing the file handle.
    /// </summary>
    public bool TryForceLoadFile(string filepath, [MaybeNullWhen(false)] out FileHandle fileHandle, string? nativePathOverride = null)
    {
        try {
            using var fs = File.OpenRead(filepath);
            fileHandle = CreateFileHandleForStream(filepath.NormalizeFilepath(), nativePathOverride, fs, null, true);
            return fileHandle != null;
        } catch (Exception e) {
            Logger.Error("Failed to load file: " + e.Message);
            fileHandle = null;
            return false;
        }
    }

    /// <summary>
    /// Attempt to find and load a file based on an internal or native filepath. Checks the active bundle and base game files and returns the first result.
    /// </summary>
    public bool TryResolveGameFile(string filename, [MaybeNullWhen(false)] out FileHandle file)
    {
        var nativePath = workspace.Env.ResolveFilepath(filename);
        if (nativePath != null) {
            return TryGetOrLoadFile(nativePath, out file);
        }

        foreach (var candidate in workspace.Env.FindPossibleFilepaths(filename)) {
            file = ReadOrGetFileResource(candidate, null);
            if (file != null) {
                return true;
            }
        }
        file = null;
        return false;
    }

    private void RunBackgroundLoadQueue()
    {
        var linkedResourceQueue = new Queue<string>();
        while (backgroundQueue.TryDequeue(out var item)) {
            if (!TryResolveGameFile(item.filename, out var mainRes)) continue;
            linkedResourceQueue.Enqueue(item.filename);

            var preloadedResources = 0;
            while (linkedResourceQueue.Count != 0) {
                var next = linkedResourceQueue.Dequeue();
                var fmt = PathUtils.ParseFileFormat(next);
                if (PreloadFormats.Contains(fmt.format)) {
                    if (!TryResolveGameFile(next, out var linked)) continue;
                    preloadedResources++;

                    if (fmt.format == KnownFileFormats.Scene) {
                        var linkscn = linked.GetFile<ScnFile>();
                        foreach (var resource in linkscn.ResourceInfoList) {
                            linkedResourceQueue.Enqueue(resource.Path);
                        }
                    }
                }
            }

            item.receiver.ReceiveResource(mainRes, item.callback);
            // Logger.Debug($"Preloaded {preloadedResources} resources for {item.filename}");
        }
    }

    /// <summary>
    /// Attempt to find and load a file based on an internal or native filepath. Checks the active bundle and base game files and returns the first result.
    /// </summary>
    public void TryResolveFileInBackground(string filename, IAsyncResourceReceiver receiver, Action<FileHandle> callback)
    {
        if (backgroundQueue.IsEmpty) {
            backgroundQueue.Enqueue((filename, receiver, callback));
            Task.Run(RunBackgroundLoadQueue);
        } else {
            backgroundQueue.Enqueue((filename, receiver, callback));
        }
    }

    /// <summary>
    /// Attemps to resolve and load a streaming buffer file for the given file. Automatically attempts to prepend the streaming/ prefix at the correct position of the file path.
    /// </summary>
    public bool TryResolveStreamingBufferFile(FileHandle mainFile, [MaybeNullWhen(false)] out FileHandle file)
    {
        var sourceTargetPath = mainFile.TargetPath ?? PathUtils.GetTargetFromFullFilepath(mainFile.Filepath);
        if (string.IsNullOrEmpty(sourceTargetPath)) {
            file = null;
            return false;
        }
        sourceTargetPath = PathUtils.GetStreamingPath(sourceTargetPath);

        // try to resolve an on-disk loose streaming file relative to the main file
        var filepathStreaming = mainFile.Filepath;
        var filepathNativesIndex = mainFile.Filepath.IndexOf(workspace.Platform.basePath, StringComparison.OrdinalIgnoreCase);
        if (filepathNativesIndex != -1) {
            var internalStartIndex = filepathNativesIndex + workspace.Platform.basePath.Length;
            filepathStreaming = string.Concat(filepathStreaming.AsSpan(0, internalStartIndex), "streaming/", filepathStreaming.AsSpan(internalStartIndex));
        }

        return TryResolveStreamingBufferFile(sourceTargetPath, filepathStreaming, out file);
    }

    /// <summary>
    /// Attemps to resolve and load the file as a raw data buffer stream.
    /// </summary>
    public bool TryResolveStreamingBufferFile(string streamingTargetPath, string filepath, [MaybeNullWhen(false)] out FileHandle file)
    {
        streamingTargetPath = streamingTargetPath.NormalizeFilepath();
        var rawStream = workspace.Env.FindSingleFile(streamingTargetPath, out var resolvedPath);
        if (rawStream != null && resolvedPath != null) {
            resolvedPath = workspace.Env.RemoveBasePath(resolvedPath).ToString();
            if (openFiles.TryGetValue(resolvedPath, out file)) {
                rawStream.Dispose();
                return true;
            }
            openFiles[resolvedPath] = file = CreateRawStreamFileHandle(resolvedPath, streamingTargetPath, rawStream, true);
            var bundleFile = AttemptResolveBundleFile(streamingTargetPath, streamingTargetPath, true);
            file = bundleFile ?? file;
            return true;
        }

        if (Path.IsPathFullyQualified(filepath) && File.Exists(filepath)) {
            filepath = filepath.NormalizeFilepath();
            file = CreateRawStreamFileHandle(filepath, streamingTargetPath, File.OpenRead(filepath), true);
            if (file != null) {
                openFiles[filepath] = file;
                return true;
            }
        }

        file = null;
        return false;
    }

    /// <summary>
    /// Attempt to load a file from the given file path and assume the set target path for its base file. File path can be a disk path, a native, target or resource path.
    /// </summary>
    public FileHandle? ReadOrGetFileResource(string filepath, string? targetPath)
    {
        filepath = workspace.Env.RemoveBasePath(filepath).ToString().NormalizeFilepath();
        targetPath ??= PreprocessTargetFilepath(filepath);
        if (!string.IsNullOrEmpty(targetPath) && openFiles.TryGetValue(targetPath, out var handle) ||
            openFiles.TryGetValue(filepath, out handle)) {
            return handle;
        }

        return ReadFileResource(filepath, targetPath, true);
    }

    private string? PreprocessTargetFilepath(string filepath)
    {
        if (!Path.IsPathRooted(filepath)) {
            filepath = workspace.Env.RemoveBasePath(filepath).ToString().NormalizeFilepath();
            var fmt = PathUtils.ParseFileFormatFull(filepath);
            if (fmt.version != -1) {
                // usecase: file taken direct from PAK file
                return filepath;
            }

            // usecase: resource path without extension
            // attempt to optimize and prevent searching/loading it anew on each load request
            // lang variants (.en, .ja) might not differentiate like this, but this is a request without an explicit lang anyway so it's OK

            // we've already opened this specific one before and therefore know which path it should have
            if (targetPathRemaps.TryGetValue(filepath, out var targetPath)) return targetPath;

            // not opened before but we can auto append the version that we know it should have
            if (fmt.format != KnownFileFormats.Unknown && workspace.Env.TryGetFileExtensionVersion(fmt.extension, out var expectedVersion)) {
                return targetPathRemaps[filepath] = filepath + "." + expectedVersion;
            }

            return null;
        }

        if (activeBundle?.HasFiles == true) {
            // usecase: opening a file in the active bundle (opened as disk file)
            filepath = workspace.Env.RemoveBasePath(filepath).ToString().NormalizeFilepath();
            if (filepath.StartsWith(workspace.BundleManager.GetBundleFolder(activeBundle))) {
                var localPath = Path.GetRelativePath(workspace.BundleManager.GetBundleFolder(activeBundle), filepath).NormalizeFilepath();
                if (activeBundle.TryFindResourceByLocalPath(localPath, out var resourceList)) {
                    return resourceList.Target;
                }
            }
        }

        // intentionally unhandled usecases:
        // - when opening a random disk file, even if it's in a natives/stm/ path, don't mark the target path
        // - if the file is inside a non-active bundle, also don't mark the path - they mustn't replace actually active files
        return null;
    }

    private FileHandle? ReadFileResource(string filepath, string? targetPath, bool includeActiveBundle)
    {
        FileHandle? handle = null;
        if (Path.IsPathFullyQualified(filepath)) {
            if (!File.Exists(filepath)) return null;

            if (includeActiveBundle && activeBundle?.HasFiles == true && filepath.NormalizeFilepath().StartsWith(activeBundle.StoragePath)) {
                handle = AttemptResolveBundleFile(filepath, targetPath, false);
            }

            if (handle == null) {
                var file = File.OpenRead(filepath);
                handle = CreateFileHandleForStream(filepath, targetPath, file, targetPath);
            }

            if (handle == null) return null;

            openFiles.TryAdd(handle.TargetPath ?? handle.Filepath, handle);
            return handle;
        }

        if (includeActiveBundle) {
            handle = AttemptResolveBundleFile(filepath, targetPath, false);
        }

        if (handle == null) {
            // either no bundle, file is not in bundle, or bundle load failed
            // this does mean we might end up loading the basegame file when a bundle file fails to load
            // reasonable for some cases (dependency files) and confusing in some (opening bundle file directly),
            // but surely the user noticed the error message
            var stream = workspace.Env.FindSingleFile(filepath, out var resolvedFilename);
            if (resolvedFilename != null) resolvedFilename = workspace.Env.RemoveBasePath(resolvedFilename).ToString();
            if (stream != null) {
                filepath = resolvedFilename ?? filepath;
                var srcFile = targetPath ?? (resolvedFilename != null && !Path.IsPathFullyQualified(resolvedFilename) ? resolvedFilename : null);
                handle = CreateFileHandleForStream(filepath, srcFile, stream, srcFile);
            }
        }

        if (workspace.Env.AllowUseLooseFiles && handle?.HandleType != FileHandleType.Bundle) {
            var looseStream = workspace.Env.FindSingleFile(filepath, out var loosePath, Workspace.FileSourceType.Loose);
            if (looseStream != null) {
                var useRawHandler = handle?.Loader is UnknownStreamFileLoader;
                var looseHandle = useRawHandler ? CreateRawStreamFileHandle(loosePath ?? filepath, filepath, looseStream) : CreateFileHandleForStream(loosePath ?? filepath, filepath, looseStream, filepath)!;
                if (looseHandle != null) {
                    if (handle == null) {
                        handle = looseHandle;
                    } else {
                        looseHandle.DiffHandler = handle.DiffHandler ?? looseHandle.DiffHandler;
                        handle = looseHandle;
                    }
                }
            }
        }

        if (handle == null) return null;
        openFiles.TryAdd(handle.TargetPath ?? handle.Filepath, handle);
        return handle;
    }

    private FileHandle? AttemptResolveBundleFile(string filepath, string? targetPath, bool rawFile)
    {
        // TODO should include dependency bundles as well here
        if (activeBundle?.HasFiles == true && activeBundle.TryFindResource(targetPath ?? filepath, out var resourceListing, out var localPath)) {
            // we can treat the given handle as a "temporary" file and now load the active file
            var fullBundleFilePath = workspace.BundleManager.ResolvePathToBundleFile(activeBundle, localPath);
            if (File.Exists(fullBundleFilePath)) {
                using var bundleStream = File.OpenRead(fullBundleFilePath);
                if (rawFile) {
                    var rawHandle = CreateRawStreamFileHandle(fullBundleFilePath, resourceListing.Target, bundleStream);
                    rawHandle.FileSource = activeBundle.Name;
                    return rawHandle;
                }

                var handle = CreateFileHandleForStream(fullBundleFilePath, resourceListing.Target, bundleStream, resourceListing.BaseFile ?? resourceListing.Target);
                if (handle != null) {
                    handle.FileSource = activeBundle.Name;
                    return handle;
                }

                // file failed to load, what now? we can try fetching the base file and do a partial patch
                var baseFilePath = resourceListing.BaseFile ?? resourceListing.Target;
                var baseFile = rawFile || baseFilePath == null ? null : workspace.Env.GetFile(baseFilePath, Workspace.FileSourceType.Original);
                var newBaseHandle = baseFile == null ? null : CreateFileHandleForStream(filepath, baseFilePath, baseFile, null);
                if (string.IsNullOrEmpty(baseFilePath) || baseFile == null || newBaseHandle?.DiffHandler == null || resourceListing.Diff == null) {
                    Logger.Error("Failed to load bundle file - it may be outdated and no partial patch is available:\n" + fullBundleFilePath);
                    return null;
                }

                newBaseHandle.DiffHandler.ApplyDiff(resourceListing.Diff);
                handle = new FileHandle(fullBundleFilePath, new MemoryStream(), FileHandleType.Bundle, newBaseHandle.Loader) {
                    TargetPath = resourceListing.Target,
                    ResourcePath = workspace.Env.GetResourcePath(resourceListing.Target).ToString(),
                    DiffHandler = newBaseHandle.DiffHandler,
                    Resource = newBaseHandle.Resource,
                    FileSource = activeBundle.Name,
                };
                handle.Modified = true;
                // reopen the base file to make sure it's a clean separate file
                newBaseHandle.DiffHandler.LoadBase(
                    workspace,
                    CreateFileHandleForStream(filepath, baseFilePath, workspace.Env.GetRequiredFile(baseFilePath, Workspace.FileSourceType.Original), null)!);

                Logger.Warn("Bundle file could not be loaded directly, but succeeded recovery using the last partial patch instead:\n" + fullBundleFilePath);
                return handle;
            }
        }
        return null;
    }

    public bool CanLoadFile(string filepath)
    {
        return GetLoaderForFile(filepath, PathUtils.ParseFileFormat(filepath)) != null;
    }

    private IFileLoader? GetLoaderForFile(string filepath, REFileFormat format, FileHandle? file = null)
    {
        foreach (var candidate in FileLoaders) {
            if (candidate.CanHandleFile(filepath, format, file)) {
                return candidate;
            }
        }
        return null;
    }

    public FileHandle? CreateNewFile(KnownFileFormats format, string? filepath = null)
    {
        var ext = workspace.Env.GetFileExtensionsForFormat(format).FirstOrDefault();
        if (ext == null) {
            Logger.Error($"Unable to create new {format} file");
            return null;
        }

        string filename = $"{format}_{Random.Shared.Next().ToString("X")}.{ext}";
        if (workspace.Env.TryGetFileExtensionVersion(ext, out var version)) {
            ext += "." + version;
        }
        var fmt = new REFileFormat(format, version);

        var loader = GetLoaderForFile(filename, fmt);
        if (loader == null) {
            Logger.Error($"No loader available for {format} file .{ext}");
            return null;
        }

        return CreateNewFile(loader, filepath ?? format.ToString(), ext);
    }

    public FileHandle? CreateNewFile(IFileLoader loader, string baseName, string extension)
    {
        string filename;
        if (baseName.EndsWith(extension)) {
            filename = baseName;
        } else {
            filename = $"{baseName}_{Random.Shared.Next().ToString("X")}.{extension}";
        }
        var handle = CreateRawStreamFileHandle(filename, null, new MemoryStream(), true, FileHandleType.New);
        handle.Loader = loader;
        handle.Modified = true;
        var newFileResource = loader.CreateNewFile(workspace, handle);
        if (newFileResource == null) {
            return null;
        }

        openFiles[filename] = handle;
        handle.Resource = newFileResource;
        handle.DiffHandler = loader.CreateDiffHandler();
        return handle;
    }

    private readonly Dictionary<string, string> _importExceptions = new();
    public string? GetLastFileImportError(string filepath) => _importExceptions.GetValueOrDefault(filepath);

    private FileHandle? CreateFileHandleForStream(string filepath, string? targetPath, Stream stream, string? baseFilePath, bool allowDisposeStream = true)
    {
        var format = PathUtils.ParseFileFormat(filepath);

        if (format.version != -1) {
            var ext = PathUtils.GetFilenameExtensionWithoutSuffixes(filepath).ToString();
            if (workspace.Env.TryGetFileExtensionVersion(ext, out var expectedVersion) && expectedVersion != format.version) {
                Logger.Warn($"Unexpected file version .{ext}.{format.version} for type {format.format}. Game {workspace.Game} expects version {expectedVersion}. File may or may not work correctly.");
            }
        }
        var handle = CreateRawStreamFileHandle(filepath, targetPath, stream, allowDisposeStream);
        IFileLoader? loader = GetLoaderForFile(filepath, format, handle);
        handle.Loader = loader ?? UnknownStreamFileLoader.Instance;

        try {
            if (!handle.Load(workspace)) {
                Logger.Error($"Failed to load file {filepath}");
                return null;
            }
        } catch (NotSupportedException e) {
            Logger.Error($"Unsupported file {filepath}: {e.Message}");
            return null;
        } catch (FileImportException e) {
            Logger.Error($"Failed to load file {filepath}: {e.Message}");
            _importExceptions[filepath] = e.Message;
            return null;
        } catch (Exception e) {
            Logger.Error(e, $"Failed to load file {filepath}");
            return null;
        }
        handle.DiffHandler = handle.Loader.CreateDiffHandler();
        if (handle.DiffHandler == null) return handle;

        if (baseFilePath != null && handle.HandleType is FileHandleType.Disk or FileHandleType.Bundle) {
            var baseFile = workspace.Env.GetFile(baseFilePath);
            if (baseFile != null) {
                var baseFileHandle = CreateFileHandleForStream(baseFilePath, baseFilePath, baseFile, null);
                if (baseFileHandle == null) {
                    Logger.Warn("Failed to load base file " + baseFilePath);
                } else {
                    handle.DiffHandler.LoadBase(workspace, baseFileHandle);
                }
            } else {
                handle.DiffHandler.LoadBase(workspace, handle);
            }
        } else {
            handle.DiffHandler.LoadBase(workspace, handle);
        }
        return handle;
    }

    private FileHandle CreateRawStreamFileHandle(string filepath, string? targetPath, Stream stream, bool allowDisposeStream = true, FileHandleType? handleTypeOverride = null)
    {
        FileHandleType handleType;
        string? fileSource = null;
        if (handleTypeOverride.HasValue) {
            handleType = handleTypeOverride.Value;
        } else if (stream is MemoryStream) {
            handleType = FileHandleType.Memory;
            fileSource = "PAK file";
        } else {
            var bundleRoot = workspace.BundleManager.AppBundlePath;
            if (filepath.StartsWith(bundleRoot)) {
                handleType = FileHandleType.Bundle;
                fileSource = Path.GetRelativePath(bundleRoot, filepath).Replace('\\', '/');
                fileSource = fileSource.Split('/', 2).First();
            } else if (filepath.IsNativePath()) {
                handleType = FileHandleType.LooseFile;
                fileSource = filepath;
            } else {
                handleType = FileHandleType.Disk;
                fileSource = filepath;
            }
        }

        targetPath ??= PreprocessTargetFilepath(filepath);
        var handle = new FileHandle(filepath, stream.ToMemoryStream(disposeStream: allowDisposeStream, forceCopy: true), handleType, UnknownStreamFileLoader.Instance) {
            TargetPath = targetPath,
            ResourcePath = targetPath == null ? null : workspace.Env.GetResourcePath(targetPath).ToString(),
            FileSource = fileSource,
        };

        return handle;
    }

    /// <summary>
    /// Attempt to create a file handle for an arbitrary file stream and store it in the open files list. Does not load the base file so diffs won't work with the file.
    /// </summary>
    public FileHandle CreateCustomFileHandle(string filepath, string? nativeOrTargetPath, Stream stream, bool allowDispose = true, bool keepFileReference = true)
    {
        if (nativeOrTargetPath != null && nativeOrTargetPath.StartsWith("natives/", StringComparison.OrdinalIgnoreCase)) {
            nativeOrTargetPath = PathUtils.RemovePlatformPrefix(nativeOrTargetPath).ToString();
        }
        var handle = CreateFileHandleForStream(filepath.NormalizeFilepath(), nativeOrTargetPath, stream, null, allowDispose);
        if (handle == null) {
            throw new NotSupportedException($"File not supported or not found: {filepath}");
        }
        string filekey = handle.TargetPath ?? handle.Filepath;
        if (keepFileReference && !openFiles.TryAdd(filekey, handle)) {
            var prev = openFiles[filekey];
            CloseFile(prev);
            openFiles.TryAdd(filekey, handle);
        }
        return handle;
    }

    public (FileHandle, TFileType) GetFileHandleAndContents<TFileType>(string filepath) where TFileType : BaseFile
    {
        var handle = ReadOrGetFileResource(filepath, null);
        if (handle == null) {
            throw new NotSupportedException($"File not supported or not found: {filepath}");
        }
        var file = handle.GetFile<TFileType>();
        return (handle, file);
    }

    /// <summary>
    /// Resolve the file path for a specific file type and get its contents.
    /// </summary>
    /// <param name="filepath">The file path to load.</param>
    /// <param name="markModified">Whether to mark the file as modified. If false, the file will be auto-closed afterwards (removed from the open file list) if no other references are active.</param>
    /// <returns></returns>
    public TFileType GetFileContents<TFileType>(string filepath, bool markModified = false) where TFileType : BaseFile
    {
        var (handle, file) = GetFileHandleAndContents<TFileType>(filepath);
        if (handle == null) {
            throw new NotSupportedException($"File not supported or not found: {filepath}");
        }

        if (markModified) {
            handle.Modified = true;
        } else {
            CloseFile(handle, true);
        }

        return file;
    }

    public bool TryResolveResourceFile<TFileType>(string filename, [MaybeNullWhen(false)] out TFileType file) where TFileType : BaseFile
    {
        if (!TryResolveGameFile(filename, out var handle)) {
            file = null;
            return false;
        }

        file = handle.GetFile<TFileType>();
        return true;
    }

    public void MarkFileResourceModified(string filepath, bool markModified)
    {
        filepath = PreprocessTargetFilepath(filepath) ?? filepath;
        if (openFiles.TryGetValue(filepath, out var fileResource)) {
            fileResource.Modified = markModified;
        }
    }

    public FileHandle? GetFileHandle(string filepath, string? targetPath = null)
    {
        return ReadOrGetFileResource(filepath, targetPath);
    }

    public IEnumerable<FileHandle> GetOpenFiles() => openFiles.Values;

    /// <summary>
    /// Gets all files from the active bundle's resource listing that match any of the specified file formats.
    /// </summary>
    public IEnumerable<string> GetBundleFilesByFormats(params KnownFileFormats[] formats)
    {
        if (bundles == null || activeBundle == null || !activeBundle.HasFiles) {
            yield break;
        }

        if (formats.Length == 0) {
            foreach (var entry in activeBundle.Files) {
                yield return entry.Target;
            }
            yield break;
        }

        foreach (var entry in activeBundle.Files) {
            var fileFormat = PathUtils.ParseFileFormat(entry.Target);
            if (formats.Contains(fileFormat.format)) {
                yield return entry.Target;
            }
        }
    }

    public IEnumerable<FileHandle> GetModifiedResourceFiles()
    {
        foreach (var file in openFiles) {
            if (file.Value.Modified) {
                yield return file.Value;
            }
        }
    }

    public void CloseFile(FileHandle file, bool onlyIfNoReferences = false)
    {
        if (onlyIfNoReferences && file.References.Count > 0) {
            // TODO maybe add a IsPatcher check so we don't always open every entity file twice?
            return;
        }

        if (!openFiles.Remove(file.Filepath, out _) && file.TargetPath != null) {
            openFiles.Remove(file.TargetPath, out _);
        }
        foreach (var rf in file.References.ToList()) {
            rf.Close();
        }
        file.Dispose();
    }

    public void CloseAllFiles()
    {
        var files = openFiles.Keys.ToList();
        foreach (var file in files) {
            if (openFiles.TryGetValue(file, out var fh)) {
                CloseFile(fh);
            }
        }
    }

    public void Dispose()
    {
        CloseAllFiles();
    }

    public bool IsFileOpen(FileHandle file)
    {
        return openFiles.ContainsKey(file.Filepath) || file.TargetPath != null && openFiles.ContainsKey(file.TargetPath);
    }

    public EntityConfig? GetEntityConfig(string? entityType)
    {
        if (entityType == null) return null;
        return entities.GetValueOrDefault(entityType)?.config;
    }

    public ResourceConfig? GetResourceConfig(string? resourceTypeID)
    {
        if (resourceTypeID == null) return null;
        return resources.GetValueOrDefault(resourceTypeID)?.config;
    }

    public long GetEntityZeroId(string? entityType)
    {
        if (string.IsNullOrEmpty(entityType)) return -1;
        return entities.GetValueOrDefault(entityType)?.config.ZeroEntity?.id ?? -1;
    }

    /// <summary>
    /// "Hybrid" path comparer that uses case sensitive comparison for absolute disk file paths (C:/games/...),
    /// but case-insensitive PAK style hashed comparison for relative game paths.
    /// </summary>
    private sealed class HybridPakPathEqualityComparer : IEqualityComparer<string>
    {
        public static readonly HybridPakPathEqualityComparer Instance = new();

        public bool Equals(string? x, string? y)
        {
            var q1 = Path.IsPathRooted(x);
            var q2 = Path.IsPathRooted(y);
            if (q1 != q2) {
                return false;
            }

            if (q1 && q2) {
                return x == y;
            }

            return x?.Equals(y, StringComparison.OrdinalIgnoreCase) == true;
        }

        public int GetHashCode([DisallowNull] string str)
        {
            if (Path.IsPathRooted(str)) {
                return str.GetHashCode();
            }

            return MurMur3HashUtils.GetPakFilepathHash(str).GetHashCode();
        }
    }
}

public enum ResourceState
{
    Base,
    Active,
}
