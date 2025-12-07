using System.Numerics;

namespace ContentEditor.App;

public static class DebugUI
{
    private static readonly Dictionary<string, string> keys = new();
    private static readonly List<string> requestedKeys = new();
    private static readonly HashSet<string> unrequestedKeys = new();
    private static readonly List<(string text, DateTime end)> timedStrings = new();

    private const float HeightPerMessage = 20;

    public static void DrawKeyed<T>(string key, T text) => DrawKeyed(key, text?.ToString() ?? "NULL");
    public static void DrawKeyed(string key, string text)
    {
        keys[key] = text;
        requestedKeys.Add(key);
        unrequestedKeys.Remove(key);
    }

    public static void Draw(string text, float durationSeconds = 0.1f)
    {
        timedStrings.Add((text, DateTime.Now + TimeSpan.FromSeconds(durationSeconds)));
    }

    internal static void Render()
    {
        foreach (var unreq in unrequestedKeys) {
            keys.Remove(unreq);
        }
        unrequestedKeys.Clear();

        var pos = new Vector2(6, 30);

        var fdl = ImGui.GetForegroundDrawList();
        foreach (var key in requestedKeys) {
            fdl.AddText(pos, 0xffffffff, $"{key} | {keys[key]}");
            pos.Y += HeightPerMessage;
            unrequestedKeys.Add(key);
        }
        requestedKeys.Clear();

        var now = DateTime.Now;
        for (int i = 0; i < timedStrings.Count; ++i) {
            var entry = timedStrings[i];
            fdl.AddText(pos, 0xffffffff, entry.text);
            pos.Y += HeightPerMessage;
            if (entry.end < now) {
                timedStrings.RemoveAt(i--);
            }
        }
    }
}