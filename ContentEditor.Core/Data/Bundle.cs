namespace ContentEditor.Core;

using System;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using ReeLib;
using ReeLib.Common;

public class Bundle : BaseBundle
{
    [JsonPropertyName("resource_listing")]
    public SortedDictionary<string, ResourceListItem>? ResourceListing { get; set; }

    [JsonPropertyName("enums")]
    public Dictionary<string, Dictionary<string, JsonElement>>? Enums { get; set; }

    [JsonPropertyName("entities")]
    public List<Entity> Entities { get; set; } = new();

    [JsonIgnore]
    public RuntimeBundle? RuntimeBundle { get; internal set; }

    public bool HasFiles => ResourceListing?.Count > 0;

    [JsonIgnore]
    public IEnumerable<(string localPath, ResourceListItem resource)> ResourcesEntries => ResourceListing?.Select(kv => (kv.Key, kv.Value)) ?? [];

    [JsonIgnore]
    public IEnumerable<ResourceListItem> Files => ResourceListing?.Values ?? Enumerable.Empty<ResourceListItem>();

    [JsonIgnore]
    public IEnumerable<string> ResourceLocalPaths => ResourceListing?.Keys ?? Enumerable.Empty<string>();

    [JsonIgnore]
    private Dictionary<string, string>? _targetToLocalPathCache;

    private Dictionary<string, string> TargetToLocalPathCache =>
        _targetToLocalPathCache ??= ResourceListing?
            .GroupBy((Func<KeyValuePair<string, ResourceListItem>, ulong>)(k => MurMur3HashUtils.GetPakFilepathHash(k.Value.Target)))
            .ToDictionary(grp => grp.First().Value.Target, grp => grp.First().Key, PakHashedPathComparer.Instance) ?? new(0, (IEqualityComparer<string>)PakHashedPathComparer.Instance);

    [JsonIgnore]
    private Dictionary<string, string>? _localToTargetPathCache;

    private Dictionary<string, string> LocalToTargetPathCache =>
        _localToTargetPathCache ??= ResourceListing?
            .ToDictionary(item => item.Key, item => item.Value.Target, PakHashedPathComparer.Instance) ?? new(0, PakHashedPathComparer.Instance);

    public override void Touch()
    {
        base.Touch();
        _targetToLocalPathCache = null;
        _localToTargetPathCache = null;
    }

    public IEnumerable<Entity> GetEntities(string type)
    {
        foreach (var entity in Entities) {
            if (entity.Type == type) {
                yield return entity;
            }
        }
    }

    public Entity? GetEntity(string type, long id)
    {
        foreach (var entity in Entities) {
            if (entity.Type == type && entity.Id == id) {
                return entity;
            }
        }
        return null;
    }

    /// <summary>
    /// Update / replace an existing entity or add it to the list.
    /// </summary>
    /// <param name="updated"></param>
    /// <returns></returns>
    public EntityRecordUpdateType RecordEntity(Entity updated)
    {
        _targetToLocalPathCache = null;
        _localToTargetPathCache = null;
        for (int i = 0; i < Entities.Count; i++) {
            var entity = Entities[i];
            if (entity.Type == updated.Type && entity.Id == updated.Id) {
                if (entity == updated) return EntityRecordUpdateType.AlreadyRecorded;
                Entities[i] = updated;
                return EntityRecordUpdateType.Updated;
            }
        }
        Entities.Add(updated);
        return EntityRecordUpdateType.Added;
    }

    public Dictionary<string, JsonElement> AddEnumData(string enumClassname)
    {
        Enums ??= new();
        if (!Enums.TryGetValue(enumClassname, out var eee)) {
            Enums[enumClassname] = eee = new();
        }

        return eee;
    }

    public bool ContainsEntity(Entity e) => FindEntity(e.Type, e.Id) != null;
    public bool ContainsEntity(string type, long id) => FindEntity(type, id) != null;

    public Entity? FindEntity(string type, long id)
    {
        foreach (var entity in Entities) {
            if (entity.Type == type && entity.Id == id) {
                return entity;
            }
        }
        return null;
    }

    public ResourceListItem AddResource(string localFilepath, string targetPath, bool replace = false)
    {
        ResourceListing ??= new();
        targetPath = targetPath.NormalizeFilepath();
        if (TryFindResource(targetPath, out var prevLocal, out var prevLocalPath) && prevLocalPath == localFilepath) {
            Logger.Error("Bundle already contains the file " + targetPath + "\nBundle local filepath: " + prevLocalPath);
            return prevLocal;
        }
        if (TryFindResourceByLocalPath(localFilepath, out var prev, out prevLocalPath)) {
            if (prevLocalPath == localFilepath) {
                Logger.Error($"File {localFilepath} is already in the bundle!");
                return prev;
            }
            // if they're not case-sensitive equal, let it re-add with the newly given path
            ResourceListing.Remove(prevLocalPath);
        }
        var item = ResourceListing[localFilepath] = new ResourceListItem() { Target = targetPath, Replace = replace };
        _targetToLocalPathCache![targetPath] = localFilepath;
        if (_localToTargetPathCache != null) {
            _localToTargetPathCache[localFilepath] = targetPath;
        }
        return item;
    }

    public bool ContainsResource(string targetPath)
    {
        return TargetToLocalPathCache.ContainsKey(targetPath);
    }

    public bool TryFindResource(string targetPath, [MaybeNullWhen(false)] out ResourceListItem resourceListing)
    {
        resourceListing = TargetToLocalPathCache.TryGetValue(targetPath, out var localPath) ? ResourceListing![localPath] : null;
        return resourceListing != null;
    }

    public bool TryFindResource(string targetPath, [MaybeNullWhen(false)] out ResourceListItem resourceListing, [MaybeNullWhen(false)] out string localPath)
    {
        resourceListing = TargetToLocalPathCache.TryGetValue(targetPath, out localPath) ? ResourceListing![localPath] : null;
        return resourceListing != null;
    }

    public bool TryFindResourceByLocalPath(string localPath, [MaybeNullWhen(false)] out ResourceListItem item)
    {
        if (LocalToTargetPathCache.TryGetValue(localPath, out var targetPath) && TargetToLocalPathCache.TryGetValue(targetPath, out var realLocalPath)) {
            item = ResourceListing![realLocalPath];
            return true;
        }

        item = null;
        return false;
    }

    public bool TryFindResourceByLocalPath(string localPath, [MaybeNullWhen(false)] out ResourceListItem item, [MaybeNullWhen(false)] out string realLocalPath)
    {
        if (LocalToTargetPathCache.TryGetValue(localPath, out var targetPath) && TargetToLocalPathCache.TryGetValue(targetPath, out realLocalPath)) {
            item = ResourceListing![realLocalPath];
            return true;
        }

        item = null;
        realLocalPath = null;
        return false;
    }

    public bool RemoveResource(string localPath)
    {
        if (LocalToTargetPathCache.Remove(localPath, out var targetPath) && TargetToLocalPathCache.Remove(targetPath, out var realLocalPath)) {
            ResourceListing?.Remove(realLocalPath);
            return true;
        }

        Logger.Warn("Couldn't find local file to remove from bundle: " + localPath);
        return false;
    }

    public string ToModConfigIni()
    {
        var desc = Description;
        if (!string.IsNullOrEmpty(Homepage)) {
            desc = "Homepage: " + Homepage + "\n\n" + desc;
        }

        desc = (desc ?? "").Trim();
        return $"""
            name={Name}
            version={Version}
            description={desc.Replace("\n", "\\n")}
            author={Author}
            {(string.IsNullOrEmpty(ImagePath) ? "" : $"screenshot={ImagePath}")}
            """;
    }

    public void CopyFrom(Bundle other)
    {
        ResourceListing = other.ResourceListing;
        _localToTargetPathCache = null;
        _targetToLocalPathCache = null;
        Author = other.Author;
        Description = other.Description;
        Name = other.Name;
        Version = other.Version;
        Homepage = other.Homepage;
        ImagePath = other.ImagePath;
        CreatedAt = other.CreatedAt;
        UpdatedAt = other.UpdatedAt;
        UpdatedAtTime = other.UpdatedAtTime;
        DependsOn = other.DependsOn;
        Entities = other.Entities;
        Enums = other.Enums;
        GameVersion = other.GameVersion;
        InitialInsertIds = other.InitialInsertIds;
    }

    public void CopyFrom(RuntimeBundle other)
    {
        CreatedAt = other.CreatedAt;
        ImagePath = other.ImagePath;
        UpdateFrom(other);
    }

    public void UpdateFrom(RuntimeBundle other)
    {
        Author = other.Author;
        Description = other.Description;
        Name = other.Name;
        Version = other.Version;
        Homepage = other.Homepage;
        UpdatedAt = other.UpdatedAt;
        UpdatedAtTime = other.UpdatedAtTime;
        DependsOn = other.DependsOn;
        InitialInsertIds = other.InitialInsertIds;
    }

    public void Save()
    {
        Touch();
        var outfilepath = Path.Combine(StoragePath, "bundle.json");
        if (!Directory.Exists(StoragePath)) {
            if (RuntimeBundle != null && File.Exists(RuntimeBundle.StoragePath)) {
                Logger.Info($"Creating main desktop bundle counterpart for runtime-only bundle {Name}");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(outfilepath)!);
        }
        using var fs = File.Create(outfilepath);
        JsonSerializer.Serialize(fs, this, jsonOptions);
        if (RuntimeBundle != null) {
            RuntimeBundle.CopyFrom(this);
            RuntimeBundle.Save();
        }
    }

    public void Init(BundleManager bundleManager)
    {
        if (ResourceListing != null) {
            ResourceListing = new SortedDictionary<string, ResourceListItem>(ResourceListing, PakHashedPathComparer.Instance);
        }
        if (BundleVersion < 2) {
            if (ResourceListing?.Count > 0) {
                foreach (var (local, resource) in ResourceListing) {
                    if (resource.Target.StartsWith("natives/", StringComparison.OrdinalIgnoreCase)) {
                        resource.Target = resource.Target.Substring(resource.Target.IndexOf('/', "natives/".Length) + 1);
                    }
                }
            }
            BundleVersion = 2;
        }
    }

    public override string ToString() => Name;

    public enum EntityRecordUpdateType
    {
        AlreadyRecorded,
        Updated,
        Added,
    }
}

public class ResourceListItem
{
    [JsonPropertyName("target")]
    public string Target { get; set; } = string.Empty;

    [JsonPropertyName("baseFile")]
    public string? BaseFile { get; set; }

    // ignore the diff system and always replace the target file
    [JsonPropertyName("replace")]
    public bool Replace { get; set; } = false;

    [JsonPropertyName("diff")]
    public JsonNode? Diff { get; set; }

    [JsonPropertyName("diff_time")]
    public DateTime DiffTime { get; set; }

    public override string ToString() => Target;
}
