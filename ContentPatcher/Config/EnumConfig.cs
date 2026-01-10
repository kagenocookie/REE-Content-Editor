
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ContentPatcher;

public class EnumConfig
{
    [JsonPropertyName("enumName")]
    public string EnumName { get; set; } = string.Empty;

    [JsonPropertyName("backingType")]
    public string? BackingType { get; set; }

    [JsonPropertyName("displayLabels")]
    public Dictionary<string, string> DisplayLabels { get; set; } = new Dictionary<string, string>();

    [JsonPropertyName("flags")]
    public bool IsFlags { get; set; }

    [JsonPropertyName("values")]
    public Dictionary<string, JsonElement>? Values { get; set; }
}
