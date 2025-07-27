using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Nodes;
using ContentEditor.Core;
using ReeLib;
using ReeLib.Msg;

namespace ContentPatcher;

public class MessageData : IContentResource
{
    public MessageData()
    {
    }

    [SetsRequiredMembers]
    public MessageData(MessageEntry entry, string filename, string resourceIdentifier)
    {
        MessageKey = entry.Name;
        Guid = entry.Guid;
        FilePath = filename;
        for (int i = 0; i < entry.Strings.Length; i++) {
            var str = entry.Strings[i];
            if (!string.IsNullOrEmpty(str)) {
                Set((Language)i, str);
            }
        }
        ResourceIdentifier = resourceIdentifier;
    }

    public Guid Guid { get; set; }
    public required string MessageKey { get; set; } = string.Empty;
    public Dictionary<string, string> Messages { get; set; } = new((int)Language.Max);

    public required string ResourceIdentifier { get; set; }

    public required string FilePath { get; set; }

    public string? Get(Language lang) => Messages.GetValueOrDefault(lang.ToString());
    public string? Get(string lang) => Messages.GetValueOrDefault(lang);

    public void Set(Language lang, string msg)
    {
        Messages[lang.ToString()] = msg;
    }

    public void Set(string lang, string msg)
    {
        Messages[lang] = msg;
    }

    public IContentResource Clone()
    {
        return new MessageData() { MessageKey = MessageKey, Guid = Guid, Messages = Messages.ToDictionary(), ResourceIdentifier = ResourceIdentifier, FilePath = FilePath };
    }
    public static MessageData FromJson(string json)
    {
        var obj = JsonSerializer.Deserialize<JsonObject>(json);

        var msg = new MessageData() {
            FilePath = "",
            ResourceIdentifier = "",
            MessageKey = obj?[nameof(MessageKey)]?.AsValue()?.GetValue<string>() ?? "",
            Guid = obj?[nameof(Guid)]?.AsValue()?.GetValue<string>() is string str && Guid.TryParse(str, out var gg) ? gg : Guid.NewGuid(),
            Messages = obj?[nameof(Messages)].Deserialize<Dictionary<string, string>>() ?? new(),
        };
        return msg;
    }

    public void MessagesToEntry(MessageEntry entry)
    {
        foreach (var msg in Messages) {
            var index = Enum.Parse<Language>(msg.Key);
            entry.Strings[(int)index] = msg.Value;
        }
    }

    public override string ToString()
        => Messages.GetValueOrDefault(Language.English.ToString())
        ?? Messages.GetValueOrDefault(Language.Japanese.ToString())
        ?? Messages.Values.FirstOrDefault()
        ?? $"MessageData: {MessageKey}"
        ?? "MessageData";

    public JsonNode ToJson() => JsonSerializer.SerializeToNode(new { MessageKey, Guid, Messages }, JsonConfig.jsonOptions)!;
    public JsonNode ToJson(Workspace env) => ToJson();
}

public class BaseFileResource<T>(T file) : IResourceFile where T : BaseFile
{
    public T File { get; } = file;

    public void WriteTo(string filepath)
    {
        File.WriteTo(filepath);
    }
}
