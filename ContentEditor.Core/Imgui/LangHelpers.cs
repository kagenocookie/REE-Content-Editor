using System.Text;
using ContentEditor.Editor;

namespace ContentEditor.Core;

public sealed class IconString(string format, char icon) : FixedInterpolatedString<char>(format, icon);

public class FixedInterpolatedString<T> : TranslatableBase where T : IComparable<T>
{
    private readonly T param;
    private string _formattedString;
    private byte[] bytes;

    public override ReadOnlySpan<byte> UTF8 => bytes;
    public override string String => _formattedString;

    public FixedInterpolatedString(string format, T param) : base(format)
    {
        this.param = param;
        _formattedString = string.Format(format, param);
        bytes = Encoding.UTF8.GetBytes(_formattedString);
    }

    protected override void Reset()
    {
        bytes = Encoding.UTF8.GetBytes(string.Format(Format, param));
    }
}

public sealed class FixedString(string fmt) : TranslatableBase(fmt)
{
    private byte[] bytes = GetNullTerminatedUTF8(fmt);
    public override ReadOnlySpan<byte> UTF8 => bytes;
    public override string String => Format;

    protected override void Reset()
    {
        bytes = GetNullTerminatedUTF8(Format);
    }

    private static readonly Dictionary<string, FixedString> _cached = new();
    public static FixedString Cached(string str) => _cached.GetValueOrDefault(str) ?? (_cached[str] = GetTranslation(str) ?? new FixedString(str));
    public static TranslatableBase CachedNullFallback(string? str, TranslatableBase fallback) => str == null ? fallback : Cached(str);
    public static FixedString Cached(string str, string context) => _cached.GetValueOrDefault(str) ?? (_cached[str] = GetTranslation(str, context) ?? new FixedString(str));

    private static Dictionary<string, string> _plainTranslations = new();
    private static Dictionary<string, Dictionary<string, string>> _contextTranslations = new();

    public static string GetTranslation(string text) => _plainTranslations.GetValueOrDefault(text) ?? text;
    public static string GetTranslation(string text, string context)
        => _contextTranslations.GetValueOrDefault(context)?.GetValueOrDefault(text)
        ?? _plainTranslations.GetValueOrDefault(text)
        ?? text;

    public static void SetTranslations(Dictionary<string, string> plainTranslations, Dictionary<string, Dictionary<string, string>> contextSpecificTranslations)
    {
        _plainTranslations = plainTranslations;
        _contextTranslations = contextSpecificTranslations;
    }

    public static void OverrideTranslation(string source, string target)
    {
        _plainTranslations[source] = target;
        if (_cached.TryGetValue(source, out var cached)) {
            cached.Format = target;
        }
    }

    public static implicit operator FixedString(string str) => new (str);
    // public static implicit operator byte[](FixedString pp) => pp.bytes;
}

public sealed class FixedStringUTF8(byte[] bytes) : TranslatableBase("")
{
    public override ReadOnlySpan<byte> UTF8 => bytes;
    public override string String {
        get {
            if (string.IsNullOrEmpty(Format)) {
                Format = Encoding.UTF8.GetString(bytes);
            }
            return Format;
        }
    }

    protected override void Reset()
    {
    }
}

public sealed class ObjectString(object? target = null) : TranslatableBase("")
{
    private byte[]? bytes;
    public override ReadOnlySpan<byte> UTF8 => GetUTF8(Target);
    public override string String => Target?.ToString() ?? "";

    public object? Target { get; set; } = target;

    protected override void Reset()
    {
        // since we're not caching the results for this type, do nothing here
    }

    private ReadOnlySpan<byte> GetUTF8() => GetUTF8(Target);

    public override ReadOnlySpan<byte> GetUTF8(object? param)
    {
        var str = param?.ToString() ?? "";
        var byteCount = Encoding.UTF8.GetByteCount(str) + 1;
        if (bytes == null || byteCount > bytes.Length) {
            bytes = new byte[byteCount];
        }
        Encoding.UTF8.GetBytes(str, bytes);
        return bytes.AsSpan(0, byteCount);
    }

    public override string GetString(object? param) => param?.ToString() ?? "";
}

public sealed class FormattedObjectString(StringFormatter formatter, object? target = null) : TranslatableBase("")
{
    private byte[]? bytes;
    public override ReadOnlySpan<byte> UTF8 => GetUTF8(Target);
    public override string String => Target == null ? Format : Formatter.GetString(Target);

    public StringFormatter Formatter { get; } = formatter;
    public object? Target { get; set; } = target;

    protected override void Reset()
    {
        // since we're not caching the results for this type, do nothing here
    }

    private ReadOnlySpan<byte> GetUTF8() => GetUTF8(Target);

    public override ReadOnlySpan<byte> GetUTF8(object? param)
    {
        var str = Formatter.GetString(param);
        var byteCount = Encoding.UTF8.GetByteCount(str) + 1;
        if (bytes == null || byteCount > bytes.Length) {
            bytes = new byte[byteCount];
        }
        Encoding.UTF8.GetBytes(str, bytes);
        return bytes.AsSpan(0, byteCount);
    }
    public override string GetString(object? param) => Formatter.GetString(param!);
}

public class TextTooltip : TranslatableGroupKnownCount
{
    public readonly FixedString Text;
    public readonly FixedString Tooltip;

    public TextTooltip(string defaultText, string defaultTooltip)
    {
        Text = defaultText;
        Tooltip = defaultTooltip;
        Keys = [(nameof(Text), Text), (nameof(Tooltip), Tooltip)];
    }
}

public class TranslatableGroupKnownCount : TranslatableGroup
{
    protected IEnumerable<(string key, TranslatableBase text)> Keys { get; init; } = [];

    public override IEnumerable<(string key, TranslatableBase text)> Translatables => Keys;
}

public abstract class TranslatableGroup
{
    public abstract IEnumerable<(string key, TranslatableBase text)> Translatables { get; }
}

public abstract class TranslatableBase
{
    private string _format;
    public string Format {
        get => _format;
        set {
            _format = value;
            Reset();
        }
    }

    public abstract string String { get; }
    public abstract ReadOnlySpan<byte> UTF8 { get; }

    public virtual ReadOnlySpan<byte> GetUTF8(object? param) => UTF8;
    public virtual string GetString(object? param) => String;

    protected TranslatableBase(string format)
    {
        _format = format;
    }

    internal static byte[] GetNullTerminatedUTF8(string str)
    {
        int byteCount = Encoding.UTF8.GetByteCount(str);
        var bytes = new byte[byteCount + 1];
        Encoding.UTF8.GetBytes(str, bytes);
        return bytes;
    }

    protected abstract void Reset();

    public static implicit operator ReadOnlySpan<byte>(TranslatableBase pp) => pp.UTF8;

    public static implicit operator TranslatableBase(string str) => FixedString.Cached(str);

    public static bool operator==(TranslatableBase a, TranslatableBase b) => a.String == b.String;
    public static bool operator!=(TranslatableBase a, TranslatableBase b) => a.String != b.String;
    public override bool Equals(object? obj) => (obj as TranslatableBase)?.String == String;
    public override int GetHashCode() => String.GetHashCode();
    public override string ToString() => String;

    public static implicit operator TranslatableBase?(StringFormatter? formatter)
        => formatter == null ? null : new FormattedObjectString(formatter, null);

}

public class InterpolatedString<T>(string format) where T : IComparable<T>
{
    private SortedList<T, byte[]> Bytes { get; } = new SortedList<T, byte[]>();

    public Func<T, string> Converter { get; init; } = static (a) => a.ToString() ?? "";

    public byte[] Format(T value)
    {
        if (!Bytes.TryGetValue(value, out var bytes)) {
            Bytes[value] = bytes = TranslatableBase.GetNullTerminatedUTF8(string.Format(format, Converter(value)));
        }

        return bytes;
    }

    public FixedStringUTF8 FormatRef(T value) => new FixedStringUTF8(Format(value));
    public static implicit operator InterpolatedString<T>(string str) => new (str);
}

public class InterpolatedString<T1, T2>(string format)
    where T1 : IComparable<T1>
    where T2 : IComparable<T2>
{
    private SortedList<(T1, T2), byte[]> Bytes { get; } = new SortedList<(T1, T2), byte[]>();

    public Func<T1, string> Converter1 { get; init; } = static (a) => a.ToString() ?? "";
    public Func<T2, string> Converter2 { get; init; } = static (a) => a.ToString() ?? "";

    public byte[] Format(T1 value1, T2 value2)
    {
        if (!Bytes.TryGetValue((value1, value2), out var bytes)) {
            Bytes[(value1, value2)] = bytes = TranslatableBase.GetNullTerminatedUTF8(string.Format(format, Converter1(value1), Converter2(value2)));
        }

        return bytes;
    }

    public FixedStringUTF8 FormatRef(T1 value1, T2 value2) => new FixedStringUTF8(Format(value1, value2));
    public static implicit operator InterpolatedString<T1, T2>(string str) => new (str);
}
