using System.Text.Json;
using System.Text.Json.Serialization;
using ContentEditor.Core;
using ContentPatcher;
using ReeLib;

namespace ContentEditor.App.FileLoaders;

public class MeshCollectionLoader : IFileLoader
{
    int IFileLoader.Priority => 25;

    public static readonly MeshCollectionLoader Instance = new();

    public bool CanHandleFile(string filepath, REFileFormat format, FileHandle? file)
    {
        file?.Stream.Seek(0, SeekOrigin.Begin);
        return filepath.EndsWith(".collection.json", StringComparison.OrdinalIgnoreCase) && file != null && (!file.Stream.TryDeserializeJson(out CollectionId? coll, out _) || coll!.Type == MeshCollection.TypeName);
    }

    public IResourceFilePatcher? CreateDiffHandler() => null;

    public IResourceFile? CreateNewFile(ContentWorkspace workspace, FileHandle handle) => null;

    public IResourceFile? Load(ContentWorkspace workspace, FileHandle handle)
    {
        handle.Stream.Seek(0, SeekOrigin.Begin);
        var typeMarker = handle.Stream.ReadByte();
        handle.Stream.Seek(0, SeekOrigin.Begin);
        if (typeMarker == '[') {
            // legacy pure array
            var list = JsonSerializer.Deserialize<List<MeshCollectionItem>>(handle.Stream);
            return new MeshCollection() { Items = list ?? [] };
        }

        return JsonSerializer.Deserialize<MeshCollection>(handle.Stream);
    }

    public bool Save(ContentWorkspace workspace, FileHandle handle, string outputPath)
    {
        handle.GetResource<MeshCollection>().WriteTo(outputPath);
        return true;
    }

    private sealed record class CollectionId
    {
        #pragma warning disable 0649
        [JsonPropertyName("$file")] public string? Type { get; set; }
    }
}

public abstract class CollectionJson : IResourceFile
{
    [JsonPropertyName("$file")]
    public abstract string CollectionType { get; }

    public void WriteTo(string filepath)
    {
        using var fs = File.Create(filepath);
        JsonSerializer.Serialize(fs, this, GetType());
    }
}

public class MeshCollection : CollectionJson
{
    public const string TypeName = "mesh_collection";

    public List<MeshCollectionItem> Items { get; set; } = new();

    public MeshCollection() { }

    public MeshCollection(List<MeshCollectionItem> items)
    {
        Items = items;
    }

    [JsonPropertyName("$file")]
    public override string CollectionType => TypeName;
}

public class MeshCollectionItem
{
    [JsonPropertyName("mesh")]
    public string? Mesh { get; set; } = "";

    [JsonPropertyName("material")]
    public string? Material { get; set; } = "";

    [JsonPropertyName("skeleton")]
    public string? Skeleton { get; set; } = "";

    [JsonPropertyName("owner")]
    public string? Owner { get; set; } = "";

    [JsonPropertyName("parentJoint")]
    public string? ParentJoint { get; set; } = "";
}