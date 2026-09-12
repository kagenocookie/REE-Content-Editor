using System.Runtime.InteropServices;
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

    public override void ReadResources(ContentWorkspace workspace, Dictionary<long, IContentResource> dict)
    {
        foreach (var file in Files) {
            var msg = workspace.ResourceManager.ReadFileResource<MsgFile>(file);

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
            var msgFile = workspace.ResourceManager.ReadFileResource<MsgFile>(file);
            if (msgFile == null) {
                Logger.Warn($"Could not load msg file {file}");
                continue;
            }

            foreach (var (hash, entry) in resources) {
                var data = ((MessageData)entry);
                var msgEntry = msgFile.FindEntryByKeyHash((uint)hash);
                if (msgEntry == null) {
                    if (!string.IsNullOrEmpty(data.FileResourcePath)) {
                        continue;
                    }

                    data.FileResourcePath = file;
                    msgEntry = msgFile.AddNewEntry(data.MessageKey);
                }

                foreach (var (lang, text) in ((MessageData)entry).Messages) {
                    var langIndex = (int)Enum.Parse<Language>(lang);
                    msgEntry.Strings[langIndex] = text;
                }
            }
        }
    }
}
