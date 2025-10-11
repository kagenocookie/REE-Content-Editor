using System.Collections;
using System.Globalization;
using ContentEditor.Editor;
using ReeLib;
using SmartFormat;
using SmartFormat.Core.Extensions;
using SmartFormat.Core.Settings;
using SmartFormat.Extensions;

namespace ContentPatcher.StringFormatting;

public static class FormatterSettings
{
    public static readonly SmartSettings DefaultSettings = new SmartFormat.Core.Settings.SmartSettings() {
        CaseSensitivity = SmartFormat.Core.Settings.CaseSensitivityType.CaseSensitive,
        StringFormatCompatibility = false,
    };

    public static readonly SmartFormatter DefaultFormatter = ApplyDefaultFormatters(new SmartFormatter(FormatterSettings.DefaultSettings));

    public static SmartFormatter CreateFullEntityFormatter(EntityConfig config, ContentWorkspace? workspace = null)
    {
        var fmt = new SmartFormatter(FormatterSettings.DefaultSettings);
        fmt.AddExtensions(new EntityStringFormatterSource(config));
        ApplyDefaultFormatters(fmt);
        if (workspace != null) ApplyWorkspaceFormatters(fmt, workspace);
        fmt.AddExtensions(new NullFallbackSource());
        return fmt;
    }

    public static SmartFormatter CreateWorkspaceFormatter(ContentWorkspace workspace)
    {
        var fmt = new SmartFormatter(FormatterSettings.DefaultSettings);
        ApplyDefaultFormatters(fmt);
        ApplyWorkspaceFormatters(fmt, workspace);
        return fmt;
    }

    private static SmartFormatter ApplyDefaultFormatters(SmartFormatter formatter)
    {
        formatter.AddExtensions(new RszFieldStringFormatterSource(), new RszFieldArrayStringFormatterSource());
        formatter.AddExtensions(new DefaultFormatter(), new NullFormatter(), LowerCaseFormatter.Instance, UpperCaseFormatter.Instance);
        // @: used for RszFieldStringFormatterSource classname filtering
        formatter.Settings.Parser.AddCustomSelectorChars(['@']);
        return formatter;
    }

    private static SmartFormatter ApplyWorkspaceFormatters(SmartFormatter formatter, ContentWorkspace workspace)
    {
        formatter.AddExtensions(new TranslateGuidFormatter(workspace.Messages), new EnumLabelFormatter(workspace.Env), new EnumNameFormatter(workspace.Env));
        formatter.AddExtensions(new EntityReverseLookupFormatter(workspace));
        return formatter;
    }
}

public class NullFallbackSource : ISource
{
    public bool TryEvaluateSelector(ISelectorInfo selectorInfo)
    {
        return selectorInfo.CurrentValue == null && selectorInfo.SelectorOperator.StartsWith('?');
    }
}
public class RszFieldStringFormatterSource : ISource
{
    public bool TryEvaluateSelector(ISelectorInfo selectorInfo)
    {
        var instance = (selectorInfo.CurrentValue as RSZObjectResource)?.Instance ?? selectorInfo.CurrentValue as RszInstance;
        if (instance == null) return false;

        if (selectorInfo.SelectorText == "classname") {
            selectorInfo.Result = instance.RszClass.name;
            return true;
        }

        if (selectorInfo.SelectorText == "class_shortname") {
            selectorInfo.Result = instance.RszClass.ShortName;
            return true;
        }

        var field = selectorInfo.SelectorText;
        var at = selectorInfo.SelectorText.IndexOf('@');
        if (at != -1) {
            var cls = selectorInfo.SelectorText.AsSpan()[(at+1)..];
            if (!instance.RszClass.name.AsSpan().Contains(cls, StringComparison.InvariantCulture)) {
                selectorInfo.Result = null;
                return true;
            }
            field = selectorInfo.SelectorText[0..at];
        }
        if (instance.TryGetFieldValue(field, out var value)) {
            selectorInfo.Result = value;
            return true;
        }

        throw new Exception($"Invalid field {selectorInfo.SelectorText} for class {instance.RszClass.name}");
    }
}

public class RszFieldArrayStringFormatterSource : ISource
{
    public bool TryEvaluateSelector(ISelectorInfo selectorInfo)
    {
        var instances = (selectorInfo.CurrentValue as RSZObjectListResource)?.Instances ?? selectorInfo.CurrentValue as IList;
        if (instances == null) return false;

        int index = selectorInfo.SelectorText == "*" ? 0 : -1;
        if (index >= 0 || int.TryParse(selectorInfo.SelectorText, out index)) {
            if (index < 0 || index > instances.Count) {
                selectorInfo.Result = null;
            } else {
                selectorInfo.Result = instances[index];
            }
            return true;
        }

        if (selectorInfo.SelectorText == "Count") {
            selectorInfo.Result = instances.Count;
            return true;
        }

        throw new Exception($"Invalid field {selectorInfo.SelectorText} index {index} for string format {selectorInfo.Placeholder}");
    }
}

public class EntityStringFormatterSource(EntityConfig config) : ISource
{
    public bool TryEvaluateSelector(ISelectorInfo selectorInfo)
    {
        if (selectorInfo.CurrentValue is not ResourceEntity entity) {
            return false;
        }

        if (selectorInfo.SelectorText == "id") {
            selectorInfo.Result = entity.Id;
            return true;
        }
        if (selectorInfo.SelectorText == "label") {
            selectorInfo.Result = entity.Label;
            return true;
        }

        var target = config.GetField(selectorInfo.SelectorText);
        if (target != null) {
            selectorInfo.Result = entity.Get(selectorInfo.SelectorText);
            return true;
        }

        throw new Exception($"Invalid field {selectorInfo.SelectorText} for entity type {entity.Type}");
    }
}

public class TranslateGuidFormatter(MessageManager msg) : IFormatter
{
    public string Name { get; set; } = "translate";
    public bool CanAutoDetect { get; set; } = false;

    public bool TryEvaluateFormat(IFormattingInfo formattingInfo)
    {
        if (formattingInfo.CurrentValue is not Guid guid) return false;

        if (guid == Guid.Empty) {
            return true;
        }

        var text = msg.GetText(guid);
        if (text != null) {
            formattingInfo.Write(text);
        }

        return true;
    }
}

public class EnumNameFormatter(Workspace env) : IFormatter
{
    public string Name { get; set; } = "enum_name";
    public bool CanAutoDetect { get; set; } = false;

    public bool TryEvaluateFormat(IFormattingInfo formattingInfo)
    {
        if (formattingInfo.CurrentValue == null) {
            return true;
        }

        var enumDesc = env.TypeCache.GetEnumDescriptor(formattingInfo.FormatterOptions);
        if (enumDesc == null) {
            formattingInfo.Write(formattingInfo.CurrentValue.ToString() ?? string.Empty);
            return true;
        }

        // should probably also handle enumDesc.IsFlags somehow
        var label = enumDesc.GetLabel(Convert.ChangeType(formattingInfo.CurrentValue, enumDesc.BackingType));
        formattingInfo.Write(label ?? formattingInfo.CurrentValue.ToString() ?? string.Empty);
        return true;
    }
}

public class EnumLabelFormatter(Workspace env) : IFormatter
{
    public string Name { get; set; } = "enum";
    public bool CanAutoDetect { get; set; } = false;

    public bool TryEvaluateFormat(IFormattingInfo formattingInfo)
    {
        if (formattingInfo.CurrentValue == null) {
            return true;
        }

        var enumDesc = env.TypeCache.GetEnumDescriptor(formattingInfo.FormatterOptions);
        if (enumDesc == null) {
            formattingInfo.Write(formattingInfo.CurrentValue.ToString() ?? string.Empty);
            return true;
        }

        // should probably also handle enumDesc.IsFlags somehow
        var label = enumDesc.GetDisplayLabel(Convert.ChangeType(formattingInfo.CurrentValue, enumDesc.BackingType));
        formattingInfo.Write(label ?? formattingInfo.CurrentValue.ToString() ?? string.Empty);
        return true;
    }
}

public class LowerCaseFormatter : IFormatter
{
    public string Name { get; set; } = "lower";
    public bool CanAutoDetect { get; set; } = false;
    public static readonly LowerCaseFormatter Instance = new();

    public bool TryEvaluateFormat(IFormattingInfo formattingInfo)
    {
        formattingInfo.Write(formattingInfo.CurrentValue?.ToString()?.ToLowerInvariant() ?? string.Empty);
        return true;
    }
}

public class UpperCaseFormatter : IFormatter
{
    public string Name { get; set; } = "upper";
    public bool CanAutoDetect { get; set; } = false;
    public static readonly UpperCaseFormatter Instance = new();

    public bool TryEvaluateFormat(IFormattingInfo formattingInfo)
    {
        formattingInfo.Write(formattingInfo.CurrentValue?.ToString()?.ToUpperInvariant() ?? string.Empty);
        return true;
    }
}

public class EntityReverseLookupFormatter(ContentWorkspace env) : IFormatter
{
    public string Name { get; set; } = "reverseLookupEntity";
    public bool CanAutoDetect { get; set; } = false;
    private Dictionary<string, (StringFormatter entityFmt, StringFormatter resultFmt)> LookupFormatters = new();

    public bool TryEvaluateFormat(IFormattingInfo formattingInfo)
    {
        if (formattingInfo.CurrentValue == null) {
            return true;
        }

        var opts = formattingInfo.FormatterOptions.Split('|', StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries);
        if (opts.Length is < 2 or > 4) {
            return true;
        }

        var entityType = opts[0];
        var path = opts[1];
        var resultFormat = opts.Length > 2 ? opts[2] : "{label}";
        var fallbackString = opts.Length > 3 ? opts[3] : "";
        if (!LookupFormatters.TryGetValue(path, out var formatters)) {
            var settings = FormatterSettings.CreateFullEntityFormatter(env.Config.GetEntityConfig(entityType)!, env);
            LookupFormatters[path] = formatters = (new StringFormatter(path, settings), new StringFormatter(resultFormat, settings));
        }
        var valueStr = Convert.ToString(formattingInfo.CurrentValue, CultureInfo.InvariantCulture)!;

        var instances = env.ResourceManager.GetEntityInstances(entityType);
        foreach (var entity in instances) {
            // NOTE: should we cache the results somewhere for faster lookups?
            var entityVal = formatters.entityFmt.GetString(entity.Value);
            if (valueStr.Equals(entityVal, StringComparison.InvariantCultureIgnoreCase)) {
                formattingInfo.Write(formatters.resultFmt.GetString(entity.Value));
                return true;
            }
        }

        formattingInfo.Write(fallbackString);
        return true;
    }
}
