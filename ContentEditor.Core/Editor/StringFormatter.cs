using System.Globalization;
using SmartFormat;

namespace ContentEditor.Editor;

public class StringFormatter(string format, SmartFormatter formatter)
{
    public string GetString(object target) => formatter.Format(CultureInfo.InvariantCulture, format, target);
}
