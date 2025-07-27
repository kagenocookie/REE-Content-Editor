using ReeLib;
using ReeLib.Msg;

namespace ContentPatcher;

[ResourcePatcher("msg-file", nameof(Deserialize))]
public class SingleMsgHandler : ResourceHandler
{
    public static SingleMsgHandler Deserialize(string resourceKey, Dictionary<string, object> data)
    {
        return new SingleMsgHandler() { file = (string)data["file"] };
    }

    public override void ReadResources(ContentWorkspace workspace, ClassConfig config, Dictionary<long, IContentResource> dict)
    {
        var msg = workspace.ResourceManager.ReadFileResource<MsgFile>(file!);

        var langs = msg.Languages!;
        if (msg.AttributeItems.Count > 0) throw new NotImplementedException("Message files with attributes not yet supported");
        foreach (var entry in msg.Entries) {
            var id = entry.Header.EntryHash;
            var msgData = new MessageData() { ResourceIdentifier = file!, FilePath = file!, MessageKey = entry.Name, Guid = entry.Guid };
            for (int i = 0; i < entry.Strings.Length; ++i) {
                var str = entry.Strings[i];
                if (string.IsNullOrEmpty(str)) continue;

                var lang = langs[i];
                msgData.Messages[lang.ToString()] = str;
            }
            dict.Add(id, msgData);
        }
    }

    public override void ModifyResources(ContentWorkspace workspace, ClassConfig config, IEnumerable<KeyValuePair<long, IContentResource>> resources)
    {
        var msgFile = workspace.ResourceManager.GetOpenFile<MsgFile>(file!, true);
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
