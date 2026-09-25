using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ContentEditor;
using ReeLib;
using ReeLib.Msg;

namespace ContentPatcher;

[ResourcePatcher("msg")]
public class MsgFileResourceHandler : ResourceHandler, IResourceHandlerStatic
{
    private Regex? keyFormat;

    public override EntityFieldValueHandler CreateValueHandler(EntityField field) => new KeyedMessage();

    public static ResourceHandler Deserialize(ResourceConfig resource, ResourceConfigSerialized data, ContentWorkspace workspace)
    {
        var files = data.TargetFiles.ToList();
        var keyFormat = data.Params?.GetValueOrDefault("key_pattern") as string;
        return new MsgFileResourceHandler() {
            Config = resource,
            Files = files,
            keyFormat = string.IsNullOrEmpty(keyFormat) ? null : new Regex(keyFormat),
        };
    }

    public override IContentResource ApplyResourceData(ContentWorkspace workspace, IContentResource? resource, JsonNode? data, ResourceEntity? entity)
    {
        if (resource is not MessageData msgData) {
            msgData = MessageData.FromJson(data as JsonObject, Config);
            msgData.FileResourcePath = Files[0];
            if (string.IsNullOrEmpty(msgData.MessageKey)) {
                throw new NotImplementedException($"Can't create blank new resources of type {Config.Resource} ({Config.Type})");
            }
        }

        workspace.Diff.ApplyDiff(msgData, data);
        return msgData;
    }

    public override IContentResource CreateResource(ContentWorkspace workspace, long id, JsonNode? initialData)
    {
        return ApplyResourceData(workspace, null, initialData, null);
    }

    public override void ReadResources(ContentWorkspace workspace, Dictionary<long, IContentResource> dict)
    {
        foreach (var file in Files) {
            var msg = workspace.ResourceManager.GetFileContents<MsgFile>(file);

            var langs = msg.Languages!;
            foreach (var entry in msg.Entries) {
                var id = entry.Header.EntryHash;
                if (keyFormat != null && !keyFormat.IsMatch(entry.Name)) continue;

                var msgData = new MessageData() {
                    ResourceType = Config,
                    FileResourcePath = file,
                    MessageKey = entry.Name,
                    Guid = entry.Guid,
                    SoundID = entry.Header.soundId,
                };
                for (int i = 0; i < entry.Strings.Length; ++i) {
                    var str = entry.Strings[i];
                    if (string.IsNullOrEmpty(str)) continue;

                    var lang = langs[i];
                    msgData.Messages[lang.ToString()] = str;
                }
                if (entry.AttributeValues != null) {
                    for (int i = 0; i < entry.AttributeValues.Length; ++i) {
                        var attr = entry.AttributeItems[i];
                        var attrVal = entry.AttributeValues[i];

                        msgData.Attributes[attr.Name] = attrVal?.ToString() ?? "";
                    }
                }
                dict[id] = msgData;
            }
        }
    }

    public override void ModifyResources(ContentWorkspace workspace, IEnumerable<KeyValuePair<long, IContentResource>> resources)
    {
        foreach (var file in Files) {
            var msgFile = workspace.ResourceManager.GetFileContents<MsgFile>(file, true);
            if (msgFile == null) {
                Logger.Warn($"Could not load msg file {file}");
                continue;
            }

            foreach (var (hash, entry) in resources) {
                var msgEntry = msgFile.FindEntryByKeyHash((uint)hash);
                if (entry is NulledResource) {
                    if (msgEntry != null) {
                        msgFile.Entries.Remove(msgEntry);
                    }
                    continue;
                }

                var data = ((MessageData)entry);
                if (msgEntry == null) {
                    if (!string.IsNullOrEmpty(data.FileResourcePath) && !data.FileResourcePath.Equals(file, StringComparison.OrdinalIgnoreCase)) {
                        continue;
                    }

                    data.FileResourcePath = file;
                    msgEntry = msgFile.AddNewEntry(data.MessageKey);
                }

                foreach (var (lang, text) in data.Messages) {
                    var langIndex = (int)Enum.Parse<Language>(lang);
                    msgEntry.Strings[langIndex] = text;
                }

                foreach (var (attr, value) in data.Attributes) {
                    msgEntry.SetAttribute(attr, value);
                }

                if (data.SoundID != 0) {
                    msgEntry.Header.soundId = data.SoundID;
                }
                if (data.Guid == Guid.Empty) {
                    if (data.Guid == Guid.Empty) data.Guid = Guid.NewGuid();
                    msgEntry.Header.guid = data.Guid;
                }
            }
        }
    }
}
