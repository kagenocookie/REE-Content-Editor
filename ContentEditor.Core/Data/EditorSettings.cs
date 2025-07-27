namespace ContentEditor.Core;

using System.Text.Json;
using System.Text.Json.Serialization;

public class EditorSettings
{
    [JsonPropertyName("bundle_order")]
    public List<string> BundleOrder { get; set; } = new();

    [JsonPropertyName("bundle_settings")]
    public Dictionary<string, BundleSettings> BundleSettings { get; set; } = new();

    [JsonPropertyName("disable_injection_cache")]
    public bool DisableInjectionCache { get; set; }

    [JsonPropertyName("editor")]
    public JsonDocument? IngameEditor { get; set; }
}

public class BundleSettings
{
    [JsonPropertyName("disabled")]
    public bool Disabled { get; set; }

    public override string ToString() => $"Disabled: {Disabled}";
}
