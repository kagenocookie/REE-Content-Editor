namespace ContentPatcher;

using System.Text.Json;
using System.Text.Json.Nodes;
using ReeLib;

public static class ReeJsonConverter
{
    private static readonly JsonSerializerOptions baseRszJsonOptions = new() {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault,
    };

    public static JsonObject ToJson(this RszInstance instance, Workspace env)
    {
        return (JsonObject)JsonSerializer.SerializeToNode(instance, env.JsonOptions)!;
    }
}