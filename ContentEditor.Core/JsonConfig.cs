namespace ContentEditor.Core;

using System.Text.Json;

public static class JsonConfig
{
    public static readonly JsonSerializerOptions jsonOptions = new() {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault,
    };

    public static readonly JsonSerializerOptions jsonOptionsIncludeFields = new(jsonOptions) {
        IncludeFields = true,
    };

    public static readonly JsonSerializerOptions jsonOptionsIncludeAllFields = new(jsonOptions) {
        IncludeFields = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never,
    };

    public static readonly JsonSerializerOptions configJsonOptions = new() {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never,
        IgnoreReadOnlyProperties = false,
    };

    public static readonly JsonSerializerOptions luaJsonOptions = new() {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };
}
