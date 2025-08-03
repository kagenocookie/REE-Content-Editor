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

    public void AddResource(string localFilepath, string nativeFilepath)
    {
        ResourceListing ??= new();
        ResourceListing[localFilepath] = new ResourceListItem() { Target = nativeFilepath };
        _nativeToLocalResourcePathCache = null;
    }

    public bool TryFindResourceByNativePath(string nativePath, [MaybeNullWhen(false)] out string localPath)
    {
        if (_nativeToLocalResourcePathCache == null) {
            _nativeToLocalResourcePathCache = ResourceListing?.ToDictionary(k => k.Value.Target, k => k.Key) ?? new(0);
        }

        return _nativeToLocalResourcePathCache.TryGetValue(nativePath, out localPath);
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