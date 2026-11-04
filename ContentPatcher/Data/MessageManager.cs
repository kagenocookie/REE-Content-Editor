using ContentEditor;
using ReeLib;
using ReeLib.Common;
using ReeLib.Msg;

namespace ContentPatcher;

public class MessageManager(Workspace env)
{
    public Workspace Env { get; } = env;

    private readonly Dictionary<Guid, string> guidMessages = new();
    private readonly Dictionary<uint, string> keyMessages = new();
    private Language language = Language.English; // TODO customizable UI language
    private HashSet<string> loadedFiles = new();
    private bool loaded = false;
    public bool IsLoaded => loaded;

    public async Task<string?> GetTextAsync(Guid messageGuid)
    {
        if (loaded) {
            return GetText(messageGuid);
        } else {
            Logger.Info("Loading translations ...");
            var text = await Task.Run(() => GetText(messageGuid));
            return text;
        }
    }

    public string? GetText(Guid messageGuid)
    {
        if (!loaded) LoadLanguage(language);
        if (guidMessages.TryGetValue(messageGuid, out var msg)) {
            return msg;
        }

        return null;
    }

    public void GetTextCallback(string messageGuid, Action<string?> callback)
    {
        if (loaded) {
            callback.Invoke(GetText(messageGuid));
        } else {
            Logger.Info("Loading translations ...");
            Task.Run(() => callback.Invoke(GetText(messageGuid)));
        }
    }

    public string? GetText(string messageKey)
    {
        if (!loaded) LoadLanguage(language);
        if (keyMessages.TryGetValue(MurMur3HashUtils.GetHash(messageKey), out var msg)) {
            return msg;
        }

        return null;
    }

    /// <summary>
    /// Attempts to fully resolve any dynamic reference tags inside a translation string and removes all markup tags.
    /// </summary>
    public string ResolveMessageText(string text)
    {
        if (!loaded) LoadLanguage(language);

        var start = text.IndexOf('<');
        while (start != -1) {
            var end = text.IndexOf('>', start);
            if (end == -1) {
                return text;
            }

            var space = text.IndexOf(' ', start);
            var span = text.AsSpan();
            if (space == -1 || space > end) {
                // single <CMD> strings or </COLOR> closing tags
                text = string.Concat(span[0..start], span[(end + 1)..]);
            } else {
                var cmd = span[(start + 1)..space];
                if (cmd.SequenceEqual("REF")) {
                    // example: <REF item_name_1234>
                    var refstr = span[(space + 1)..end].Trim().ToString();
                    var sub = GetText(refstr);
                    text = string.Concat(span[0..start], sub, span[(end + 1)..]);
                } else {
                    // also get rid of anything else (<COLOR FF0000>, <EQABI MAS_SIN>, ...)
                    text = string.Concat(span[0..start], span[(end + 1)..]);
                }
            }
            start = text.IndexOf('<', start);
        }

        return text;
    }

    public void LoadLanguage(Language language)
    {
        if (loaded && this.language == language) {
            return;
        }

        loaded = true;
        keyMessages.Clear();
        guidMessages.Clear();
        foreach (var (path, stream) in Env.GetFilesWithExtension("msg")) {
            var msg = new MsgFile(new FileHandler(stream, path));
            if (!msg.Read()) continue;

            foreach (var entry in msg.Entries) {
                var text = entry.GetMessage(language);
                keyMessages[entry.Header.EntryHash] = text;
                guidMessages[entry.Header.guid] = text;
            }
        }
    }

    public void UpdateMessage(MessageData msg)
    {
        if (!loaded) {
            LoadLanguage(language);
        }
        if (msg.Messages.TryGetValue(language.ToString(), out var text)) {
            if (msg.Guid != Guid.Empty) {
                guidMessages[msg.Guid] = text;
            }
            if (!string.IsNullOrEmpty(msg.MessageKey)) {
                keyMessages[MurMur3HashUtils.GetHash(msg.MessageKey)] = text;
            }
        }
    }

    public void Update(MsgFile msg)
    {
        if (!loaded) LoadLanguage(language);
        foreach (var entry in msg.Entries) {
            var text = entry.GetMessage(language);
            keyMessages[entry.Header.EntryHash] = text;
            guidMessages[entry.Header.guid] = text;
        }
    }
}