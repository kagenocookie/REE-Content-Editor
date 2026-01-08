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
        for (int i = 0; i < entry.AttributeValues?.Length; i++) {
            var value = entry.AttributeValues[i].ToString();
            var name = entry.AttributeItems[i].Name;
            Attributes[string.IsNullOrEmpty(name) ? i.ToString() : name] = value ?? "";
        }
        ResourceIdentifier = resourceIdentifier;
    }

    public Guid Guid { get; set; }
    public required string MessageKey { get; set; } = string.Empty;
    public Dictionary<string, string> Messages { get; set; } = new((int)Language.Max);
    public Dictionary<string, string> Attributes { get; set; } = new();

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
        return FromJson(obj);
    }

    public static MessageData FromJson(JsonObject? obj)
    {
        return new MessageData() {
            FilePath = "",
            ResourceIdentifier = "",
            MessageKey = obj?[nameof(MessageKey)]?.AsValue()?.GetValue<string>() ?? "",
            Guid = obj?[nameof(Guid)]?.AsValue()?.GetValue<string>() is string str && Guid.TryParse(str, out var gg) ? gg : Guid.NewGuid(),
            Messages = obj?[nameof(Messages)].Deserialize<Dictionary<string, string>>() ?? new(),
            Attributes = obj?[nameof(Attributes)].Deserialize<Dictionary<string, string>>() ?? new(),
        };
    }

    public void MessagesToEntry(MessageEntry entry)
    {
        foreach (var msg in Messages) {
            var index = Enum.Parse<Language>(msg.Key);
            entry.Strings[(int)index] = msg.Value;
        }
        foreach (var attr in Attributes) {
            if (!int.TryParse(attr.Key, out var index)) {
                index = entry.AttributeItems.FindIndex(it => it.Name == attr.Key);
            }
            if (index == -1 || index >= entry.AttributeItems.Count) {
                continue;
            }

            entry.AttributeValues ??= new object[entry.AttributeItems.Count];
            switch (entry.AttributeItems[index].ValueType) {
                case AttributeValueType.String:
                    entry.AttributeValues[index] = attr.Value;
                    break;
                case AttributeValueType.Long:
                    entry.AttributeValues[index] = long.TryParse(attr.Value, out var ll) ? ll : 0L;
                    break;
                case AttributeValueType.Double:
                    entry.AttributeValues[index] = double.TryParse(attr.Value, out var dd) ? dd : 0.0;
                    break;
            }
        }
    }

    public override string ToString()
        => Messages.GetValueOrDefault(Language.English.ToString())
        ?? Messages.GetValueOrDefault(Language.Japanese.ToString())
        ?? Messages.Values.FirstOrDefault()
        ?? $"MessageData: {MessageKey}"
        ?? "MessageData";

    public JsonNode ToJson() => JsonSerializer.SerializeToNode(new { MessageKey, Guid, Messages, Attributes }, JsonConfig.jsonOptions)!;
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
