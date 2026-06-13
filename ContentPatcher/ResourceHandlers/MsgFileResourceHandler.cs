using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using ReeLib;
using ReeLib.Msg;

namespace ContentPatcher;

[ResourcePatcher("msg", nameof(Deserialize))]
public class MsgFileResourceHandler : ResourceHandler
{
    private Regex? keyFormat;

    public static MsgFileResourceHandler Deserialize(string resourceTypeId, Dictionary<string, object> data)
    {
        var files = new List<string>(((IEnumerable<object>)data["files"]).Cast<string>());
        var keyFormat = data.GetValueOrDefault("key") as string ?? "";
        return new MsgFileResourceHandler() {
            Files = files,
            keyFormat = string.IsNullOrEmpty(keyFormat) ? null : new Regex(keyFormat),
            ResourceTypeID = resourceTypeId,
        };
    }

    public override void ReadResources(ContentWorkspace workspace, ClassConfig config, Dictionary<long, IContentResource> dict)
    {
        foreach (var file in Files) {
            var msg = workspace.ResourceManager.ReadFileResource<MsgFile>(file);

            var langs = msg.Languages!;
            foreach (var entry in msg.Entries) {
                var id = entry.Header.EntryHash;
                if (keyFormat?.IsMatch(entry.Name) == false) continue;

                var msgData = new MessageData() { ResourceTypeID = ResourceTypeID, FilePath = file, MessageKey = entry.Name, Guid = entry.Guid };
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

    public override void ModifyResources(ContentWorkspace workspace, ClassConfig config, IEnumerable<KeyValuePair<long, IContentResource>> resources)
    {
        var msgFile = workspace.ResourceManager.ReadFileResource<MsgFile>(Files[0]);
        if (msgFile == null) {
            throw new NullReferenceException("Msg file was missing??");
        }

        foreach (var (hash, entry) in resources) {
            var data = ((MessageData)entry);
            var msgEntry = msgFile.FindEntryByKeyHash((uint)hash);
            if (msgEntry == null) {
                // TODO figure out if we need the "unknown" field
                msgEntry = msgFile.AddNewEntry(data.MessageKey);
            }

            foreach (var (lang, text) in ((MessageData)entry).Messages) {
                var langIndex = (int)Enum.Parse<Language>(lang);
                msgEntry.Strings[langIndex] = text;
            }
        }
    }
}
