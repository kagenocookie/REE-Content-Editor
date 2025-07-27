namespace ContentEditor.Core;

using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

public class Entity
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("label")]
    public string Label { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("data")]
    public Dictionary<string, JsonNode?>? Data { get; set; }

    public override string ToString() => $"[{Type} {Id}]:{Label}";
}
