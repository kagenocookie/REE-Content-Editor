namespace ContentEditor.Core;

using System;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

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
    public Dictionary<string, object>? Enums { get; set; }

    [JsonPropertyName("game_version")]
    public string? GameVersion { get; set; }

    [JsonPropertyName("initial_insert_ids")]
    public Dictionary<string, long>? InitialInsertIds { get; set; }

    [JsonIgnore]
    public string? SaveFilepath { get; set; }

    public override string ToString() => Name;

    [JsonIgnore]
    private Dictionary<string, string>? _nativeToLocalResourcePathCache;

    public void Touch()
    {
        UpdatedAt = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss \\U\\T\\C");
        UpdatedAtTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (CreatedAt == null) CreatedAt = UpdatedAt;
        _nativeToLocalResourcePathCache = null;
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
        _nativeToLocalResourcePathCache = null;
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

    public void AddResource(string localFilepath, string nativeFilepath, bool replace = false)
    {
        ResourceListing ??= new();
        nativeFilepath = nativeFilepath.Replace('\\', '/').ToLowerInvariant();
        if (TryFindResourceByNativePath(nativeFilepath, out var prevLocal)) {
            Logger.Error("Bundle already contains the file " + nativeFilepath + "\nBundle local filepath: " + prevLocal);
            return;
        }
        ResourceListing[localFilepath] = new ResourceListItem() { Target = nativeFilepath, Replace = replace };
        if (_nativeToLocalResourcePathCache != null) {
            _nativeToLocalResourcePathCache[nativeFilepath] = localFilepath;
        }
    }

    public bool TryFindResourceByLocalPath(string localPath, [MaybeNullWhen(false)] out ResourceListItem item)
    {
        ResourceListing ??= new();
        return ResourceListing.TryGetValue(localPath, out item);
    }

    public bool TryFindResourceByNativePath(string nativePath, [MaybeNullWhen(false)] out string localPath)
    {
        if (_nativeToLocalResourcePathCache == null) {
            _nativeToLocalResourcePathCache = ResourceListing?
                .GroupBy(k => k.Value.Target)
                .ToDictionary(k => k.First().Value.Target, k => k.First().Key) ?? new(0);
        }

        return _nativeToLocalResourcePathCache.TryGetValue(nativePath, out localPath);
    }

    public bool TryFindResourceListing(string nativePath, [MaybeNullWhen(false)] out ResourceListItem resourceListing)
    {
        if (_nativeToLocalResourcePathCache == null) {
            _nativeToLocalResourcePathCache = ResourceListing?
                .GroupBy(k => k.Value.Target)
                .ToDictionary(k => k.First().Value.Target, k => k.First().Key) ?? new(0);
        }

        resourceListing = _nativeToLocalResourcePathCache.TryGetValue(nativePath, out var localPath) ? ResourceListing![localPath] : null;
        return resourceListing != null;
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