
using System.Text.Json.Serialization;
using VYaml.Annotations;

namespace ContentPatcher;

public class EnumConfig
{
    [JsonPropertyName("enumName")]
    public string EnumName { get; set; } = string.Empty;

    [JsonPropertyName("displayLabels")]
    public Dictionary<string, string> DisplayLabels { get; set; } = new Dictionary<string, string>();
}