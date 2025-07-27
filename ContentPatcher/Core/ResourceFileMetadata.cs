namespace ContentPatcher;

using System.Text.Json.Nodes;

public class ResourceFileMetadata
{
    public DateTime DiffTime { get; set; }
    public JsonNode? Diff { get; set; }
    public string? VersionHash { get; set; }
}