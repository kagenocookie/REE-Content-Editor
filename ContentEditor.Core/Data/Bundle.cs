namespace ContentEditor.Core;

using System;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using ReeLib;
using ReeLib.Common;

public class Bundle
{
    [JsonPropertyName("author")]
    public string? Author { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("homepage")]
    public string? Homepage { get; set; }

    [JsonPropertyName("version")]
    public string? Version { get; set; }

    [JsonPropertyName("image")]
    public string? ImagePath { get; set; }

    [JsonPropertyName("created_at")]
    public string? CreatedAt { get; set; }

    [JsonPropertyName("updated_at")]
    public string? UpdatedAt { get; set; }

    [JsonPropertyName("updated_at_time")]
    public long UpdatedAtTime { get; set; }

    [JsonPropertyName("is_folder_bundle")]
    public string? IsFolderBundle { get; set; }

    [JsonPropertyName("depends_on")]
    public List<string>? DependsOn { get; set; }

    [JsonPropertyName("data")]
    public List<JsonObject>? LegacyData { get; set; }

    [JsonPropertyName("entities")]
    public List<Entity> Entities { get; set; } = new();

    [JsonPropertyName("resource_listing")]
    public SortedDictionary<string, ResourceListItem>? ResourceListing { get; set; }

    [JsonPropertyName("enums")]
    public Dictionary<string, Dictionary<string, JsonElement>>? Enums { get; set; }

    [JsonPropertyName("game_version")]
    public string? GameVersion { get; set; }

    [JsonPropertyName("bundle_version")]
    public int BundleVersion { get; set; }

    [JsonPropertyName("initial_insert_ids")]
    public Dictionary<string, long>? InitialInsertIds { get; set; }

    [JsonIgnore]
    public string StorageFolder { get; set; } = "";

    public bool HasResources => ResourceListing?.Count > 0;

    [JsonIgnore]
    public IEnumerable<(string localPath, ResourceListItem resource)> ResourcesEntries => ResourceListing?.Select(kv => (kv.Key, kv.Value)) ?? [];

    [JsonIgnore]
    public IEnumerable<ResourceListItem> Resources => ResourceListing?.Values ?? Enumerable.Empty<ResourceListItem>();

    [JsonIgnore]
    public IEnumerable<string> ResourceLocalPaths => ResourceListing?.Keys ?? Enumerable.Empty<string>();

    [JsonIgnore]
    private Dictionary<string, string>? _targetToLocalPathCache;

    private Dictionary<string, string> TargetToLocalPathCache =>
        _targetToLocalPathCache ??= ResourceListing?
            .GroupBy(k => MurMur3HashUtils.GetPakFilepathHash_FastAscii(k.Value.Target))
            .ToDictionary(grp => grp.First().Value.Target, grp => grp.First().Key, PakHashedPathComparer.Instance) ?? new(0, PakHashedPathComparer.Instance);

    [JsonIgnore]
    private Dictionary<string, string>? _localToTargetPathCache;

    private Dictionary<string, string> LocalToTargetPathCache =>
        _localToTargetPathCache ??= ResourceListing?
            .ToDictionary(item => item.Key, item => item.Value.Target, PakHashedPathComparer.Instance) ?? new(0, PakHashedPathComparer.Instance);

    private static readonly JsonSerializerOptions jsonOptions = new() {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        IgnoreReadOnlyProperties = true,
        IgnoreReadOnlyFields = true,
    };

    public void Touch()
    {
        UpdatedAt = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss \\U\\T\\C");
        UpdatedAtTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (CreatedAt == null) CreatedAt = UpdatedAt;
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
        return EntityRecordUpdateType.Addded;
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

    public void AddResource(string localFilepath, string targetPath, bool replace = false)
    {
        ResourceListing ??= new();
        targetPath = targetPath.NormalizeFilepath();
        if (TryFindResource(targetPath, out var prevLocal, out var prevLocalPath) && prevLocalPath == localFilepath) {
            Logger.Error("Bundle already contains the file " + targetPath + "\nBundle local filepath: " + prevLocalPath);
            return;
        }
        if (TryFindResourceByLocalPath(localFilepath, out var prev, out prevLocalPath)) {
            if (prevLocalPath == localFilepath) {
                Logger.Error($"File {localFilepath} is already in the bundle!");
                return;
            }
            // if they're not case-sensitive equal, let it re-add with the newly given path
            ResourceListing.Remove(prevLocalPath);
        }
        ResourceListing[localFilepath] = new ResourceListItem() { Target = targetPath, Replace = replace };
        _targetToLocalPathCache![targetPath] = localFilepath;
        if (_localToTargetPathCache != null) {
            _localToTargetPathCache[localFilepath] = targetPath;
        }
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

    public void Save()
    {
        Touch();
        var outfilepath = Path.Combine(StorageFolder, "bundle.json");
        Directory.CreateDirectory(Path.GetDirectoryName(outfilepath)!);
        using var fs = File.Create(outfilepath);
        JsonSerializer.Serialize(fs, this, jsonOptions);
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
        Addded,
    }
}

public class ResourceListItem
{
    [JsonPropertyName("target")]
    public string Target { get; set; } = string.Empty;

    // ignore the diff system and always replace the target file
    [JsonPropertyName("replace")]
    public bool Replace { get; set; } = false;

    [JsonPropertyName("diff")]
    public JsonNode? Diff { get; set; }

    [JsonPropertyName("diff_time")]
    public DateTime DiffTime { get; set; }

    public override string ToString() => Target;
}
