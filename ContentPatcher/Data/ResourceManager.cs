using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.Json.Nodes;
using ContentEditor;
using ContentEditor.Core;
using ReeLib;

namespace ContentPatcher;

public sealed class ResourceManager(PatchDataContainer config) : IDisposable
{
    private readonly Dictionary<string, ResourceData> resources = new();
    private readonly Dictionary<string, EntityData> entities = new();
    private readonly ConcurrentDictionary<string, FileHandle> openFiles = new();
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
        KnownFileFormats.Texture,
        KnownFileFormats.MaterialDefinition,
    ];

    public bool HasAnyActivatedEntities => entities.Values.Any(entityData => entityData.activatedInstances.Count != 0);

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
                if (field is ICustomResourceField custom && custom.ResourceIdentifier != null) {
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
        if (bundle.ResourceListing != null) {
            var bundleBasepath = workspace.BundleManager.GetBundleFolder(bundle);
            foreach (var (localFile, resInfo) in bundle.ResourceListing) {
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

        if (!success) {
            Logger.Error($"Loading of bundle {bundle.Name} was not fully successful.");
        }
    }

    private bool ApplyBundleFile(string bundleBasepath, string localFile, ResourceListItem resourceEntry, bool markModifiedOnChange)
    {
        var fullLocalFilepath = Path.Combine(bundleBasepath, localFile);
        if (resourceEntry.Replace) {
            if (File.Exists(fullLocalFilepath)) {
                if (TryLoadUniqueFile(fullLocalFilepath, out var local, resourceEntry.Target)) {
                    local.Modified = true;
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
            var tempFile = FileHandle.FromDiskFilePath(fullLocalFilepath, file.Loader);
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
            Logger.Error(e, $"Failed to apply patch for file {localFile}. This could indicate issues with the patch generation or . Attempting simple replacement...");

            if (File.Exists(fullLocalFilepath)) {
                var local = ReadOrGetFileResource(fullLocalFilepath, resourceEntry.Target);
                if (local != null) {
                    openFiles[resourceEntry.Target] = local;
                    return true;
                }

                Logger.Error($"Could not load source file {fullLocalFilepath}.");
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

        if (fieldResource.FilePath == null) {
            // ignore - there's no file here
        } else if (openFiles.TryGetValue(fieldResource.FilePath, out var file)) {
            file.Modified = true;
        } else {
            throw new Exception("New resource file should've been opened, wtf?");
        }
        AddResource(fieldResource.ResourceIdentifier, resourceId, fieldResource, state);
        return fieldResource;
    }

    public TResourceType CreateEntityResource<TResourceType>(ResourceEntity entity, CustomField field, ResourceState state, string? resourceKeyOverride = null) where TResourceType : IContentResource
    {
        var key = resourceKeyOverride ?? field.ResourceIdentifier;
        if (key == null) return Activator.CreateInstance<TResourceType>();
        if (resources.TryGetValue(key, out var data)) {
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
            data.config.PrimaryEnum?.UpdateEnum(workspace, entity);
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
            data.config.PrimaryEnum?.UpdateEnum(workspace, entity);
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
            if (field.ResourceIdentifier == null || field.Condition?.IsEnabled(entity) == false) {
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
        entity.Label = data.config.StringFormatter?.GetString(entity) ?? $"{type} {id}";
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
        fileHandle = ReadOrGetFileResource(filename, null);
        return fileHandle != null;
    }

    /// <summary>
    /// Loads a disk file fully ignoring any in-memory, PAK or bundle changes, and does not store the file in the resource manager.
    /// The caller must take care of disposing the file handle.
    /// </summary>
    public bool TryLoadUniqueFile(string filepath, [MaybeNullWhen(false)] out FileHandle fileHandle, string? nativePathOverride = null)
    {
        try {
            using var fs = File.OpenRead(filepath);
            fileHandle = CreateFileHandleInternal(filepath, nativePathOverride, fs, true);
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
    public bool TryResolveFile(string filename, [MaybeNullWhen(false)] out FileHandle file)
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
            if (!TryResolveFile(item.filename, out var mainRes)) continue;
            linkedResourceQueue.Enqueue(item.filename);

            var preloadedResources = 0;
            while (linkedResourceQueue.Count != 0) {
                var next = linkedResourceQueue.Dequeue();
                var fmt = PathUtils.ParseFileFormat(next);
                if (PreloadFormats.Contains(fmt.format)) {
                    if (!TryResolveFile(next, out var linked)) continue;
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
        string? sourceNativePath = null;
        if (mainFile.NativePath != null) {
            sourceNativePath = mainFile.NativePath;
        } else {
            sourceNativePath = PathUtils.GetNativeFromFullFilepath(mainFile.Filepath);
        }
        if (string.IsNullOrEmpty(sourceNativePath)) {
            file = null;
            return false;
        }
        sourceNativePath = sourceNativePath.Replace("natives/stm/", "natives/stm/streaming/");

        return TryResolveStreamingBufferFile(sourceNativePath, mainFile.Filepath.Replace("natives/stm/", "natives/stm/streaming/"), out file);
    }

    /// <summary>
    /// Attemps to resolve and load the file as a raw data buffer stream.
    /// </summary>
    public bool TryResolveStreamingBufferFile(string streamingNativePath, string filepath, [MaybeNullWhen(false)] out FileHandle file)
    {
        var rawStream = workspace.Env.FindSingleFile(streamingNativePath, out var resolvedPath);
        if (rawStream != null && resolvedPath != null) {
            if (openFiles.TryGetValue(resolvedPath, out file)) {
                rawStream.Dispose();
                return true;
            }
            openFiles[resolvedPath] = file = CreateRawStreamFileHandle(resolvedPath, streamingNativePath, rawStream, true);
            AttemptResolveBundleFile(ref file, streamingNativePath, streamingNativePath, streamingNativePath);
            return true;
        }
        foreach (var candidate in workspace.Env.FindPossibleFilepaths(streamingNativePath)) {
            if (openFiles.TryGetValue(candidate, out file)) {
                return true;
            }

            file = ReadOrGetFileResource(candidate, null);
            if (file != null) {
                AttemptResolveBundleFile(ref file, filepath, candidate, candidate);
                openFiles[candidate] = file;
                return true;
            }
        }

        file = null;
        return false;
    }

    private FileHandle? ReadOrGetFileResource(string filepath, string? nativePath)
    {
        filepath = filepath.NormalizeFilepath();
        nativePath ??= PreprocessNativeFilepath(filepath);
        if (openFiles.TryGetValue(nativePath ?? filepath, out var handle)) {
            return handle;
        }

        return ReadFileResource(filepath, nativePath, true);
    }

    private string? PreprocessNativeFilepath(string filepath)
    {
        filepath = filepath.NormalizeFilepath();
        filepath = workspace.Env.PrependBasePath(filepath);
        if (activeBundle?.ResourceListing != null && Path.IsPathFullyQualified(filepath) && filepath.StartsWith(workspace.BundleManager.GetBundleFolder(activeBundle))) {
            var localPath = Path.GetRelativePath(workspace.BundleManager.GetBundleFolder(activeBundle), filepath);
            if (activeBundle.ResourceListing.TryGetValue(localPath, out var resourceList)) {
                return resourceList.Target;
            }
        }
        return filepath.IsNativePath() ? filepath : null;
    }

    private FileHandle? ReadFileResource(string filepath, string? nativePath, bool includeActiveBundle)
    {
        FileHandle? handle = null;
        filepath = filepath.NormalizeFilepath();
        var stream = workspace.Env.FindSingleFile(filepath, out var resolvedFilename);
        if (stream != null) {
            handle = CreateFileHandleInternal(filepath, nativePath ?? (resolvedFilename != null && !Path.IsPathFullyQualified(resolvedFilename) ? resolvedFilename : null), stream);
        }

        if (includeActiveBundle) {
            if (!AttemptResolveBundleFile(ref handle, filepath, nativePath, resolvedFilename)) {
                // try loose file as fallback only
                var looseStream = workspace.Env.FindSingleFile(resolvedFilename ?? filepath, out resolvedFilename, Workspace.FileSourceType.Loose);
                if (looseStream != null) {
                    var useRawHandler = handle?.Loader is UnknownStreamFileLoader;
                    var loosePath = Path.Combine(workspace.Env.Config.GamePath, resolvedFilename!);
                    var looseHandle = useRawHandler ? CreateRawStreamFileHandle(loosePath, resolvedFilename, looseStream) : CreateFileHandleInternal(loosePath, resolvedFilename, looseStream)!;
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
        }

        if (handle == null) return null;
        openFiles.TryAdd(handle.NativePath ?? handle.Filepath, handle);
        return handle;
    }

    private bool AttemptResolveBundleFile([NotNullIfNotNull(nameof(handle))] ref FileHandle? handle, string filepath, string? nativePath, string? resolvedFilename)
    {
        // TODO should include dependency bundles as well here
        if (activeBundle?.ResourceListing != null && activeBundle.TryFindResourceByNativePath(filepath, out var bundleLocalResource)) {
            // we can treat the given handle as a "temporary" file and now load the active file

            var resourceListing = activeBundle.ResourceListing[bundleLocalResource];
            var fullBundleFilePath = workspace.BundleManager.ResolveBundleLocalPath(activeBundle, bundleLocalResource).NormalizeFilepath();
            if (File.Exists(fullBundleFilePath)) {
                using var bundleStream = File.OpenRead(fullBundleFilePath);
                var useRawHandler = handle?.Loader is UnknownStreamFileLoader;
                var activeHandle = useRawHandler ? CreateRawStreamFileHandle(fullBundleFilePath, resourceListing.Target, bundleStream) : CreateFileHandleInternal(fullBundleFilePath, resourceListing.Target, bundleStream);
                // FileHandle? activeHandle = null; // testing diff apply behavior
                if (handle != null) {
                    if (activeHandle != null) {
                        // reuse the temp file's diff handler since it should have internalized the source file within itself
                        // this way we can use it as the diff base easily
                        activeHandle.DiffHandler = handle.DiffHandler ?? activeHandle.DiffHandler;
                    } else {
                        // if bundle file fails to load, it may be outdated - try patching it with the diff instead
                        if (handle.DiffHandler != null && resourceListing.Diff != null) {
                            handle.DiffHandler.ApplyDiff(resourceListing.Diff);
                            // re-open the original file, this way we have a distinct pristine base file in the differ
                            // (ApplyDiff likely reused existing instances from the previous base file, meaning we'd have the same instances in the base and in the patched file)
                            var newBaseHandle = useRawHandler ? CreateRawStreamFileHandle(fullBundleFilePath, resourceListing.Target, bundleStream) : CreateFileHandleInternal(filepath, nativePath ?? resolvedFilename, workspace.Env.GetRequiredFile(resolvedFilename!))!;
                            activeHandle = new FileHandle(fullBundleFilePath, new MemoryStream(), FileHandleType.Memory, handle.Loader) {
                                NativePath = handle.NativePath,
                                DiffHandler = newBaseHandle.DiffHandler,
                                Resource = handle.Resource,
                            };
                            activeHandle.Modified = true;
                            Logger.Warn("Bundle file could not be loaded directly, using the last partial patch instead:\n" + fullBundleFilePath);
                        } else {
                            Logger.Error("Failed to load bundle file - it may be outdated and no partial patch is available:\n" + fullBundleFilePath);
                            handle.Modified = true;
                        }
                    }
                }
                handle = activeHandle;
                if (handle != null) {
                    handle.FileSource = activeBundle.Name;
                }
                return activeHandle != null;
            }
        }
        return false;
    }

    public bool CanLoadFile(string filepath)
    {
        return GetLoaderForFile(filepath, PathUtils.ParseFileFormat(filepath)) != null;
    }

    private IFileLoader? GetLoaderForFile(string filepath, REFileFormat format)
    {
        foreach (var candidate in FileLoaders) {
            if (candidate.CanHandleFile(filepath, format)) {
                return candidate;
            }
        }
        return null;
    }
    private FileHandle? CreateFileHandleInternal(string filepath, string? nativePath, Stream stream, bool allowDisposeStream = true)
    {
        var format = PathUtils.ParseFileFormat(filepath);
        IFileLoader? loader = GetLoaderForFile(filepath, format);

        if (format.version != -1) {
            var ext = PathUtils.GetFilenameExtensionWithoutSuffixes(filepath);
            if (workspace.Env.TryGetFileExtensionVersion(ext.ToString(), out var expectedVersion) && expectedVersion != format.version) {
                Logger.Warn($"Unexpected file version .{ext}.{format.version} for type {format.format}. Game {workspace.Game} expects version {expectedVersion}. File may or may not work correctly.");
            }
        }
        var handle = CreateRawStreamFileHandle(filepath, nativePath, stream, allowDisposeStream);
        handle.Loader = loader ?? UnknownStreamFileLoader.Instance;

        try {
            if (!handle.Load(workspace)) {
                Logger.Error($"Failed to load file {filepath}");
                return null;
            }
        } catch (NotSupportedException e) {
            Logger.Error($"Unsupported file {filepath}: {e.Message}");
            return null;
        } catch (Exception e) {
            Logger.Error(e, $"Failed to load file {filepath}");
            return null;
        }
        handle.DiffHandler = handle.Loader.CreateDiffHandler();
        if (handle.DiffHandler != null && handle.NativePath != null && handle.HandleType is FileHandleType.Disk or FileHandleType.Bundle) {
            var baseFile = workspace.Env.GetFile(handle.NativePath!);
            if (baseFile != null) {
                var baseFileHandle = CreateFileHandleInternal(handle.NativePath!, handle.NativePath!, baseFile)!;
                handle.DiffHandler.LoadBase(workspace, baseFileHandle);
            } else {
                handle.DiffHandler.LoadBase(workspace, handle);
            }
        } else {
            handle.DiffHandler?.LoadBase(workspace, handle);
        }
        return handle;
    }

    private FileHandle CreateRawStreamFileHandle(string filepath, string? nativePath, Stream stream, bool allowDisposeStream = true)
    {
        FileHandleType handleType;
        string? fileSource = null;
        if (stream is MemoryStream) {
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

        var handle = new FileHandle(filepath, stream.ToMemoryStream(disposeStream: allowDisposeStream, forceCopy: true), handleType, UnknownStreamFileLoader.Instance) {
            NativePath = nativePath ?? PreprocessNativeFilepath(filepath),
            FileSource = fileSource,
        };

        return handle;
    }

    public FileHandle CreateFileHandle(string filepath, string? nativePath, Stream stream, bool allowDispose = true, bool keepFileReference = true)
    {
        var handle = this.CreateFileHandleInternal(filepath, nativePath, stream, allowDispose);
        if (handle == null) {
            throw new NotSupportedException();
        }
        string filekey = handle.NativePath ?? handle.Filepath;
        if (keepFileReference && !openFiles.TryAdd(filekey, handle)) {
            var prev = openFiles[filekey];
            CloseFile(prev);
            openFiles.TryAdd(filekey, handle);
        }
        return handle;
    }

    public TFileType ReadFileResource<TFileType>(string filepath, bool markModified = false) where TFileType : BaseFile
    {
        var resource = ReadOrGetFileResource(filepath, null);
        if (resource == null) {
            throw new NotSupportedException();
        }
        var file = resource.GetFile<TFileType>();

        if (markModified) {
            resource.Modified = true;
        }

        return file;
    }

    public bool TryResolveResourceFile<TFileType>(string filename, [MaybeNullWhen(false)] out TFileType file) where TFileType : BaseFile
    {
        if (!TryResolveFile(filename, out var handle)) {
            file = null;
            return false;
        }

        file = handle.GetFile<TFileType>();
        return true;
    }

    /// <summary>
    /// Loads a file from its base state (directly from PAK / loose file), ignoring existing open files.
    /// </summary>
    public FileHandle GetBaseFile(string filepath)
    {
        var handle = ReadFileResource(filepath, null, false);
        if (handle == null) {
            throw new NotSupportedException();
        }
        return handle;
    }

    public void MarkFileResourceModified(string filepath, bool markModified)
    {
        filepath = PreprocessNativeFilepath(filepath) ?? filepath;
        if (openFiles.TryGetValue(filepath, out var fileResource)) {
            fileResource.Modified = markModified;
        }
    }

    public FileHandle GetFileHandle(string filepath, string? nativePath = null)
    {
        var resource = ReadOrGetFileResource(filepath, nativePath);
        if (resource == null) {
            throw new NotSupportedException();
        }
        return resource;
    }

    public TFileType? GetOpenFile<TFileType>(string filepath, bool markModified = false) where TFileType : BaseFile
    {
        filepath = PreprocessNativeFilepath(filepath) ?? filepath;
        if (openFiles?.TryGetValue(filepath, out var handle) == true) {
            var file = handle.GetFile<TFileType>();
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
        foreach (var file in openFiles) {
            if (file.Value.Modified) {
                yield return file.Value;
            }
        }
    }

    public void CloseFile(FileHandle file)
    {
        if (!openFiles.Remove(file.Filepath, out _) && file.NativePath != null) {
            openFiles.Remove(file.NativePath, out _);
        }
        foreach (var rf in file.References) {
            rf.Close();
        }
        file.Dispose();
    }

    public void CloseAllFiles()
    {
        var files = openFiles.Keys.ToList();
        foreach (var file in files) {
            CloseFile(openFiles[file]);
        }
    }

    public void Dispose()
    {
        CloseAllFiles();
    }
}

public enum ResourceState
{
    Base,
    Active,
}
