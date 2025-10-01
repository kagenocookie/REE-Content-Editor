using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using ContentEditor;
using ContentEditor.Core;
using ReeLib;
using ReeLib.Mot;
using ReeLib.Msg;

namespace ContentPatcher;

public class MotionDataResource : IContentResource
{
    public MotionDataResource()
    {
        MotName = "";
        MotDataBase64 = "";
        MotType = KnownFileFormats.Motion;
    }

    public MotionDataResource(MotFile mot)
    {
        MotName = mot.Header.motName;
        MotVersion = mot.Header.version;
        FilePath = mot.FileHandler.FilePath + "|" + MotName;
        MotType = KnownFileFormats.Motion;

        var stream = new MemoryStream();
        var handler = new FileHandler(stream);
        mot.WriteTo(handler, false);
        stream.Seek(0, SeekOrigin.Begin);
        MotDataBase64 = Convert.ToBase64String(stream.GetBuffer().AsSpan(0, (int)stream.Length));
    }

    [JsonPropertyName("name")]
    public string MotName { get; set; }
    [JsonPropertyName("version")]
    public MotVersion MotVersion { get; set; }
    [JsonPropertyName("type")]
    public KnownFileFormats MotType { get; set; }
    [JsonPropertyName("motDataBase64")]
    public string MotDataBase64 { get; set; }

    [JsonIgnore]
    public string ResourceIdentifier => "mot_data";

    [JsonIgnore]
    public string? FilePath { get; private set; }

    private static JsonSerializerOptions jsonOptions = new(JsonConfig.jsonOptions);
    static MotionDataResource()
    {
        jsonOptions.Converters.Add(new JsonStringEnumConverter<KnownFileFormats>());
        jsonOptions.Converters.Add(new JsonStringEnumConverter<MotVersion>());
    }

    public IContentResource Clone()
    {
        return new MotionDataResource() {
            MotDataBase64 = MotDataBase64,
            MotName = MotName,
            MotType = MotType,
            MotVersion = MotVersion,
        };
    }

    public string ToJsonString(Workspace env) => ToJson(env).ToJsonString(jsonOptions);

    public JsonNode ToJson(Workspace env)
    {
        return JsonSerializer.SerializeToNode(this, jsonOptions)!;
    }

    public static bool TryDeserialize(string json, [MaybeNullWhen(false)] out MotionDataResource data, out string? error)
    {
        return json.TryDeserializeJson(out data, out error, jsonOptions);
    }

    public static bool TryDeserialize(JsonNode json, [MaybeNullWhen(false)] out MotionDataResource data, out string? error)
    {
        try {
            data = json.Deserialize<MotionDataResource>(jsonOptions);
            error = null;
            return data != null;
        } catch (Exception e) {
            data = null;
            error = e.Message;
            return false;
        }
    }

    public MotFileBase? ToMotFile()
    {
        if (MotType != KnownFileFormats.Motion) {
            throw new NotImplementedException("Unsupported motion type " + MotType);
        }

        // copying the bytes over so the stream stays expandable if we need to later write new data to the same stream
        var bytes = Convert.FromBase64String(MotDataBase64);
        var stream = new MemoryStream(bytes.Length);
        stream.Write(bytes);
        stream.Seek(0, SeekOrigin.Begin);
        var mot = new MotFile(new FileHandler(stream));
        mot.Header.version = MotVersion;
        if (!mot.Read()) {
            Logger.Error("Failed to read mot file data");
            return null;
        }

        return mot;
    }
}