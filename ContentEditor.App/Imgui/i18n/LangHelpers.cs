using System.Text;

namespace ContentEditor.App.Internationalization;

public class IconString(string format, char icon) : FixedInterpolatedStringProvider<char>(format, icon);

public class FixedInterpolatedStringProvider<T>(string format, T param) : TranslatableBase(format) where T : IComparable<T>
{
    private byte[] bytes = Encoding.UTF8.GetBytes(string.Format(format, param));

    public ReadOnlySpan<byte> UTF8 => bytes;

    protected override void Reset()
    {
        bytes = Encoding.UTF8.GetBytes(string.Format(Format, param));
    }

    public static implicit operator byte[](FixedInterpolatedStringProvider<T> pp) => pp.bytes;
}

public class FixedString(string fmt) : TranslatableBase(fmt)
{
    private byte[] bytes = Encoding.UTF8.GetBytes(fmt);
    public ReadOnlySpan<byte> UTF8 => bytes;
    public string String => Format;

    protected override void Reset()
    {
        bytes = Encoding.UTF8.GetBytes(Format);
    }

    public static implicit operator FixedString(string str) => new (str);
    public static implicit operator byte[](FixedString pp) => pp.bytes;
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
    internal string Format {
        get => _format;
        set {
            _format = value;
            Reset();
        }
    }

    protected TranslatableBase(string format)
    {
        _format = format;
    }

    protected abstract void Reset();
}

public class SingleInterpolatedStringProvider<T>(string format) where T : IComparable<T>
{
    private SortedList<T, byte[]> Bytes { get; } = new SortedList<T, byte[]>();

    public ReadOnlySpan<byte> Format(T num)
    {
        if (!Bytes.TryGetValue(num, out var bytes)) {
            Bytes[num] = bytes = Encoding.UTF8.GetBytes(string.Format(format, num));
        }

        return bytes;
    }
}
