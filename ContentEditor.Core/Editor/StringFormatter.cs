using System.Globalization;
using SmartFormat;
using SmartFormat.Core.Formatting;

namespace ContentEditor.Editor;

public class StringFormatter(string format, SmartFormatter formatter)
{
    public string GetString(object target)
    {
        try {
            return formatter.Format(CultureInfo.InvariantCulture, format, target);
        } catch (FormattingException e) {
            Logger.Error("Failed to evaluate object string representation: " + e.Message);
            return target.ToString() ?? target.GetType().Name;
        }
    }

    public override string ToString() => $"{format} [Sources:\n{string.Join(",\n", formatter.GetSourceExtensions())}\nFormatters:\n{string.Join(",\n", formatter.GetFormatterExtensions())}]";
}
