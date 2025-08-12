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