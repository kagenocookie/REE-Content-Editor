namespace ContentEditor.Core;

using System;
using System.Text.Json;
using System.Text.Json.Serialization;

public class BaseBundle
{
    public const int CurrentBundleVersion = 2;

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

    [JsonPropertyName("depends_on")]
    public List<string>? DependsOn { get; set; }

    [JsonPropertyName("game_version")]
    public string? GameVersion { get; set; }

    [JsonPropertyName("bundle_version")]
    public int BundleVersion { get; set; } = CurrentBundleVersion;

    [JsonPropertyName("initial_insert_ids")]
    public Dictionary<string, long>? InitialInsertIds { get; set; }

    [JsonIgnore]
    public string StoragePath { get; set; } = "";

    protected static readonly JsonSerializerOptions jsonOptions = new() {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        IgnoreReadOnlyProperties = true,
        IgnoreReadOnlyFields = true,
    };

    public virtual void Touch()
    {
        UpdatedAt = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss \\U\\T\\C");
        UpdatedAtTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (CreatedAt == null) CreatedAt = UpdatedAt;
    }

    public override string ToString() => Name;
}
