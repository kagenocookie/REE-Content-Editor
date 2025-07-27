namespace ContentEditor.Core;

using System.Text.Json;
using System.Text.Json.Serialization;

public class EnumData
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("label")]
    public string Label { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;
}

public class SerializedEnum
{
    [JsonPropertyName("enumName")]
    public string EnumName { get; set; } = string.Empty;

    [JsonPropertyName("displayLabels")]
    public Dictionary<string, string>? DisplayLabels { get; set; }

    [JsonPropertyName("values")]
    public Dictionary<string, JsonElement>? Values { get; set; }

    [JsonPropertyName("isVirtual")]
    public bool IsVirtual { get; set; }
}
