namespace ContentEditor.Core;

using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

public class RuntimeBundle : BaseBundle
{
    [JsonPropertyName("data")]
    public List<JsonObject>? RuntimeEntities { get; set; }

    public void CopyFrom(RuntimeBundle other)
    {
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
        RuntimeEntities = other.RuntimeEntities;
        GameVersion = other.GameVersion;
        InitialInsertIds = other.InitialInsertIds;
    }

    public void CopyFrom(Bundle main)
    {
        Author = main.Author;
        Description = main.Description;
        Name = main.Name;
        Version = main.Version;
        Homepage = main.Homepage;
        ImagePath = main.ImagePath;
        CreatedAt = main.CreatedAt;
        UpdatedAt = main.UpdatedAt;
        UpdatedAtTime = main.UpdatedAtTime;
        DependsOn = main.DependsOn;
        BundleVersion = main.BundleVersion;
        InitialInsertIds = main.InitialInsertIds;
    }

    public bool RemoveEntity(string type, long id)
    {
        if (RuntimeEntities == null) return false;

        for (int i = 0; i < RuntimeEntities.Count; i++) {
            var other = RuntimeEntities[i];
            if (!(other.TryGetPropertyValue("type", out var tstr) && tstr?.GetValue<string>() == type)) {
                continue;
            }
            if (!(other.TryGetPropertyValue("id", out var tid) && tid?.GetValue<long>() == id)) {
                continue;
            }
            RuntimeEntities.RemoveAt(i);
            return true;
        }
        return false;
    }

    public void Save()
    {
        Touch();
        Directory.CreateDirectory(Path.GetDirectoryName(StoragePath)!);
        using var fs = File.Create(StoragePath);
        JsonSerializer.Serialize(fs, this, jsonOptions);
    }

    public override string ToString() => Name;
}
