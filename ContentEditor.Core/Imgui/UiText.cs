using System.Globalization;

namespace ContentEditor.Core;

/// <summary>Localizes presentation text only. Values, resource names and paths remain untouched.</summary>
public static class UiText
{
    private static IReadOnlyDictionary<string, string> translations = new Dictionary<string, string>();
    private static System.Collections.Concurrent.ConcurrentDictionary<string, byte[]> utf8 = new();
    internal static int Revision { get; private set; }

    public static void SetTranslations(IReadOnlyDictionary<string, string> values)
    {
        translations = values.ToDictionary(pair => NormalizeLineEndings(pair.Key), pair => pair.Value, StringComparer.Ordinal);
        utf8 = new();
        Revision++;
    }

    public static ReadOnlySpan<byte> Utf8(string source)
        => utf8.GetOrAdd(T(source), TranslatableBase.GetNullTerminatedUTF8);

    public static ReadOnlySpan<byte> LabelUtf8(string source)
        => utf8.GetOrAdd(Label(source), TranslatableBase.GetNullTerminatedUTF8);

    public static string T(string source)
    {
        if (translations.TryGetValue(NormalizeLineEndings(source), out var translated)) return translated;
        return source;
    }

    private static string NormalizeLineEndings(string text)
        => text.Contains('\r') ? text.Replace("\r\n", "\n", StringComparison.Ordinal) : text;

    public static string F(FormattableString source)
        => string.Format(CultureInfo.CurrentCulture, T(source.Format), source.GetArguments());

    public static string Label(string source)
    {
        if (source.StartsWith("##", StringComparison.Ordinal)) return source;
        var separator = source.IndexOf("##", StringComparison.Ordinal);
        var visible = separator < 0 ? source : source[..separator];
        var translated = T(visible);
        // ### resets ImGui's hash: both languages use an ID derived from the source.
        var identity = source.Contains("###", StringComparison.Ordinal)
            ? source[(source.IndexOf("###", StringComparison.Ordinal) + 3)..] : source;
        return translated + "###" + identity;
    }

    public static string FormatLabel(FormattableString source)
    {
        var original = source.ToString(CultureInfo.CurrentCulture);
        if (original.StartsWith("##", StringComparison.Ordinal)) return original;
        var separator = source.Format.IndexOf("##", StringComparison.Ordinal);
        var visibleFormat = separator < 0 ? source.Format : source.Format[..separator];
        var translated = string.Format(CultureInfo.CurrentCulture, T(visibleFormat), source.GetArguments());
        var identity = original.Contains("###", StringComparison.Ordinal)
            ? original[(original.IndexOf("###", StringComparison.Ordinal) + 3)..] : original;
        return translated + "###" + identity;
    }
}
