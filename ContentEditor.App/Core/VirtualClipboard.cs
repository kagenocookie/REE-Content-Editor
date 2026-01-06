using System.Diagnostics.CodeAnalysis;
using ContentEditor.App.Windowing;

namespace ContentEditor.App;

internal static class VirtualClipboard
{
    private static readonly Dictionary<int, object> Entries = new();
    private static int nextId = 1;

    public static int Store(object data)
    {
        var id = nextId++;
        Entries[id] = data;
        return id;
    }

    public static void CopyToClipboard(object data, string? extraNote = null)
    {
        var id = Store(data);
        EditorWindow.CurrentWindow!.CopyToClipboard($"CE_VCP:{id}:{data.GetType().Name}|{data}", $"Copied to clipboard:\n{data}{(extraNote == null ? "" : $"\n\nNOTE: {extraNote}")}");
    }

    public static bool VerifyClipboardType<T>()
    {
        var str = EditorWindow.CurrentWindow?.GetClipboard();
        if (string.IsNullOrEmpty(str) || !str.StartsWith("CE_VCP:")) return false;

        var colon = str.IndexOf(':');
        var colon2 = str.IndexOf(':', colon + 1) + 1;
        if (colon2 == 0) return false;

        var idStr = str.AsSpan()[colon..colon2];
        if (!int.TryParse(idStr, out var id)) {
            return false;
        }

        return Entries.GetValueOrDefault(id) is T;
    }

    public static bool TryGetFromClipboard<T>([MaybeNullWhen(false)] out T value)
    {
        value = default;
        var str = EditorWindow.CurrentWindow?.GetClipboard();
        if (string.IsNullOrEmpty(str) || !str.StartsWith("CE_VCP:")) return false;

        var colon = str.IndexOf(':');
        var colon2 = str.IndexOf(':', colon + 1);
        if (colon2 == -1) return false;

        var idStr = str.AsSpan()[(colon + 1)..colon2];
        if (!int.TryParse(idStr, out var id)) {
            return false;
        }

        return TryGetById<T>(id, out value);
    }

    public static bool TryGetById<T>(int id, [MaybeNullWhen(false)] out T value)
    {
        if (!Entries.TryGetValue(id, out var entry) || entry is not T cast) {
            value = default;
            return false;
        }

        value = cast;
        return true;
    }
}
