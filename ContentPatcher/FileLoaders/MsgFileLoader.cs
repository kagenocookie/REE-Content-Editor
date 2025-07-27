using System.Text.Json.Nodes;
using ReeLib;
using ReeLib.Msg;

namespace ContentPatcher;

public class MsgFileLoader : DefaultFileLoader<MsgFile>
{
    public MsgFileLoader() : base(KnownFileFormats.Message, () => new MsgFilePatcher()) { }
}

public class MsgFilePatcher : IResourceFilePatcher
{
    private MsgFile baseFile = null!;
    private ContentWorkspace workspace = null!;

    public IResourceFile LoadBase(ContentWorkspace workspace, FileHandle handle)
    {
        this.workspace = workspace;
        baseFile = handle.GetContent<MsgFile>();
        return handle.Resource;
    }

    public JsonNode? FindDiff(FileHandle file)
    {
        var newfile = file.GetContent<MsgFile>();

        var missing = baseFile.Entries.ToDictionary(e => e.Name);
        JsonObject? messageDiffs = null;
        foreach (var entry in newfile.Entries) {
            if (!missing.Remove(entry.Name, out var baseEntry)) {
                messageDiffs ??= new();
                messageDiffs.Add(entry.Name, new MessageData(entry, newfile.FileHandler.FilePath!, "").ToJson());
            } else {
                JsonObject? diff = null;

                if (baseEntry.Guid != entry.Guid) {
                    diff ??= new();
                    diff[nameof(Guid)] = entry.Guid;
                }

                for (int i = 0; i < entry.Strings.Length; i++) {
                    var curstr = entry.Strings[i];
                    if (baseEntry.Strings[i] != curstr) {
                        diff ??= new();
                        diff["Messages"] ??= new JsonObject();
                        diff["Messages"]![((Language)i).ToString()] = curstr;
                    }
                }

                for (int i = 0; i < entry.AttributeValues!.Length; i++) {
                    var curAttr = entry.AttributeValues[i];
                    if (!baseEntry.AttributeValues![i].Equals(curAttr)) {
                        diff ??= new();
                        diff["Attributes"] ??= new JsonObject();
                        diff["Attributes"]![i] = curAttr switch {
                            long l => l,
                            double d => d,
                            string s => s,
                            _ => string.Empty,
                        };
                    }
                }

                if (diff != null) {
                    messageDiffs ??= new();
                    messageDiffs.Add(entry.Name, diff);
                }
            }
        }

        if (missing.Count > 0) messageDiffs ??= new();
        foreach (var (key, msg) in missing) {
            messageDiffs!.Add(key, JsonValue.Create("$d"));
        }

        if (messageDiffs != null) {
            return new JsonObject([new ("Entries", messageDiffs)]);
        }

        return messageDiffs;
    }

    public void ApplyDiff(JsonNode diff)
    {
        if (diff is JsonObject dobj) {
            if (dobj.TryGetPropertyValue("Entries", out var entriesNode) && entriesNode is JsonObject entriesObj) {

                foreach (var (name, jsonEntry) in entriesObj) {
                    if (jsonEntry == null) continue;
                    MessageEntry? entry = baseFile.FindEntryByKey(name);

                    if (jsonEntry.GetValueKind() == System.Text.Json.JsonValueKind.String) {
                        if (jsonEntry.GetValue<string>() == "$d") {
                            if (entry != null) baseFile.Entries.Remove(entry);
                        }
                        continue;
                    }

                    if (jsonEntry is not JsonObject obj) continue;

                    entry ??= baseFile.AddNewEntry(name, Guid.Empty);
                    if (obj["Guid"]?.GetValue<string>() is string guidStr && Guid.TryParse(guidStr, out var guid)) {
                        entry.Header.guid = guid;
                    } else if (entry.Header.guid == Guid.Empty) {
                        entry.Header.guid = Guid.NewGuid();
                    }

                    if (obj.TryGetPropertyValue("Messages", out var msgs) && msgs is JsonObject msgDiff) {
                        foreach (var (langStr, langText) in msgDiff) {
                            var langIndex = (int)Enum.Parse<Language>(langStr);
                            if (langText != null) {
                                entry.Strings[langIndex] = langText.GetValue<string>();
                            }
                        }
                    }

                    // TODO Attributes
                }
            }
        }
    }

    public MsgFile GetFile() => baseFile;
}
