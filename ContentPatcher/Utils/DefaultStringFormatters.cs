using System.Collections;
using System.Globalization;
using System.Text.RegularExpressions;
using ContentEditor;
using ContentEditor.Editor;
using ReeLib;
using ReeLib.Common;
using ReeLib.Il2cpp;
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

    public static readonly SmartFormatter DefaultFormatter = ApplyDefaultFormatters(ApplyRszFieldFallback(new SmartFormatter(FormatterSettings.DefaultSettings)));

    public static SmartFormatter CreateFullEntityFormatter(EntityConfig config, ContentWorkspace? workspace = null)
    {
        var fmt = new SmartFormatter(FormatterSettings.DefaultSettings);
        fmt.AddExtensions(new EntityStringFormatterSource(config), new ResourceStringFormatter());
        if (workspace != null) {
            fmt.AddExtensions(new UserDataFileFormatter(workspace));
        }
        ApplyDefaultFormatters(fmt);
        if (workspace != null) ApplyWorkspaceFormatters(fmt, workspace);
        else {
            fmt.AddExtensions(new RszFieldStringFormatterSource(null));
        }
        fmt.AddExtensions(new NullFallbackSource());
        return fmt;
    }

    public static SmartFormatter CreateResourceFormatter(ResourceConfig resource, ContentWorkspace workspace)
    {
        var fmt = new SmartFormatter(FormatterSettings.DefaultSettings);
        fmt.AddExtensions(new ResourceStringFormatter(), new UserDataFileFormatter(workspace));
        ApplyDefaultFormatters(fmt);
        if (workspace != null) ApplyWorkspaceFormatters(fmt, workspace);
        else {
            fmt.AddExtensions(new RszFieldStringFormatterSource(null));
        }
        fmt.AddExtensions(new NullFallbackSource());
        return fmt;
    }

    public static SmartFormatter CreateWorkspaceFormatter(ContentWorkspace workspace)
    {
        var fmt = new SmartFormatter(FormatterSettings.DefaultSettings);
        ApplyDefaultFormatters(fmt);
        ApplyWorkspaceFormatters(fmt, workspace);
        fmt.AddExtensions(new NullFallbackSource());
        return fmt;
    }

    private static SmartFormatter ApplyDefaultFormatters(SmartFormatter formatter)
    {
        formatter.AddExtensions(new RszFieldArrayStringFormatterSource());
        formatter.AddExtensions(new DefaultFormatter(), new NullFormatter());
        formatter.AddExtensions(
            PathFormatter.Instance,
            LowerCaseFormatter.Instance,
            UpperCaseFormatter.Instance,
            RegexPatternFormatter.Instance,
            StringHashFormatter.Instance
        );
        // @: used for RszFieldStringFormatterSource classname filtering
        formatter.Settings.Parser.AddCustomSelectorChars(['@']);
        return formatter;
    }

    private static SmartFormatter ApplyRszFieldFallback(SmartFormatter formatter)
    {
        formatter.AddExtensions(new RszFieldStringFormatterSource(null));
        return formatter;
    }

    private static SmartFormatter ApplyWorkspaceFormatters(SmartFormatter formatter, ContentWorkspace workspace)
    {
        formatter.AddExtensions(new RszFieldStringFormatterSource(workspace));
        formatter.AddExtensions(new TranslateGuidFormatter(workspace.Messages), new EnumLabelFormatter(workspace.Env), new EnumNameFormatter(workspace.Env), new TranslateFormattedString(workspace.Messages), new EntityLabelFormatter(workspace));
        formatter.AddExtensions(new FindEntityFormatter(workspace));
        return formatter;
    }
}

static class FormatterExtension
{
    internal static void WriteOrFormat(this IFormattingInfo formattingInfo, object? value)
    {
        if (formattingInfo.Format != null && formattingInfo.Format.HasNested) {
            formattingInfo.FormatAsChild(formattingInfo.Format, value);
        } else {
            formattingInfo.Write(value?.ToString() ?? "");
        }
    }
}

public class NullFallbackSource : ISource
{
    public bool TryEvaluateSelector(ISelectorInfo selectorInfo)
    {
        if (selectorInfo.CurrentValue == null || selectorInfo.CurrentValue as string == "" || selectorInfo.SelectorOperator.Contains('?')) {
            selectorInfo.Result = null;
            return true;
        }
        return false;
    }
}
public class RszFieldStringFormatterSource(ContentWorkspace? workspace) : ISource
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
        var fieldIndex = instance.RszClass.IndexOfField(field);
        if (fieldIndex != -1) {
            var value = instance.Values[fieldIndex];
            if (workspace != null && value is RszInstance rszValue && !string.IsNullOrEmpty(rszValue?.RSZUserData?.Path)) {
                if (workspace.ResourceManager.TryResolveGameFile(rszValue.RSZUserData.Path, out var resolved)) {
                    selectorInfo.Result = resolved.GetFile<UserFile>().Instance;
                } else {
                    selectorInfo.Result = value;
                }
            } else {
                selectorInfo.Result = value;
            }
            return true;
        }

        if (selectorInfo.SelectorOperator.Contains('?')) return false;
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
            if (index < 0 || index >= instances.Count) {
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

        if (selectorInfo.SelectorOperator.Contains('?')) return false;

        throw new Exception($"Invalid field {selectorInfo.SelectorText} for entity type {entity.Type}");
    }
}

public class ResourceStringFormatter() : ISource
{
    public bool TryEvaluateSelector(ISelectorInfo selectorInfo)
    {
        if (selectorInfo.CurrentValue is not IContentResource resource) {
            return false;
        }

        if (selectorInfo.SelectorText == "id" && resource is IAddressableContentResource addressable) {
            selectorInfo.Result = addressable.ID;
            return true;
        }

        if (resource is IPropertyContainer props) {
            selectorInfo.Result = props.Get(selectorInfo.SelectorText);
            return selectorInfo.Result != null;
        }

        if (selectorInfo.SelectorOperator.Contains('?')) return false;

        throw new Exception($"Invalid field \"{selectorInfo.SelectorText}\" for resource {resource}");
    }
}

public class UserDataFileFormatter(ContentWorkspace workspace) : ISource
{
    public bool TryEvaluateSelector(ISelectorInfo selectorInfo)
    {
        if (selectorInfo.CurrentValue is not RszInstance rsz || rsz.RSZUserData == null) {
            return false;
        }

        var userPath = rsz.RSZUserData.Path;
        if (selectorInfo.SelectorText == "path") {
            selectorInfo.Result = userPath ?? "";
            return true;
        }

        if (selectorInfo.SelectorText == "file") {
            if (string.IsNullOrEmpty(userPath)) {
                selectorInfo.Result = null;
                return true;
            }

            if (workspace.ResourceManager.TryResolveGameFile(userPath, out var handle)) {
                selectorInfo.Result = handle.GetFile<UserFile>().Instance;
                return true;
            }

            selectorInfo.Result = null;
            return true;
        }

        return false;
    }
}

public class PathFormatter : IFormatter
{
    public string Name { get; set; } = "path";
    public bool CanAutoDetect { get; set; } = false;
    public static readonly PathFormatter Instance = new();

    public bool TryEvaluateFormat(IFormattingInfo formattingInfo)
    {
        if (formattingInfo.CurrentValue == null) {
            return true;
        }

        if (formattingInfo.CurrentValue is RszInstance instance) {
            var field = formattingInfo.FormatterOptions;
            if (!string.IsNullOrEmpty(field)) {
                formattingInfo.Write((instance.GetFieldValue(field) as RszInstance)?.RSZUserData?.Path ?? "");
            } else {
                formattingInfo.Write(instance.RSZUserData?.Path ?? "");
            }
            return true;
        }
        return false;
    }
}
public class TranslateGuidFormatter(MessageManager msg) : IFormatter
{
    public string Name { get; set; } = "translate";
    public bool CanAutoDetect { get; set; } = false;

    public bool TryEvaluateFormat(IFormattingInfo formattingInfo)
    {
        if (formattingInfo.CurrentValue is not Guid guid) {
            return true;
        }

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

public class TranslateFormattedString(MessageManager msg) : IFormatter
{
    public string Name { get; set; } = "translate_fmt";
    public bool CanAutoDetect { get; set; } = false;

    public bool TryEvaluateFormat(IFormattingInfo formattingInfo)
    {
        var str = formattingInfo.CurrentValue?.ToString();
        if (string.IsNullOrEmpty(str)) {
            formattingInfo.Write(str ?? "");
            return true;
        }

        var fmt = formattingInfo.FormatterOptions;
        var formatted = fmt.Replace("$0", str);
        var text = msg.GetText(formatted);
        if (string.IsNullOrEmpty(text)) {
            formattingInfo.Write(str);
        } else {
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
        var nextValue = label ?? formattingInfo.CurrentValue.ToString() ?? string.Empty;
        formattingInfo.WriteOrFormat(nextValue);
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

        EnumDescriptor? enumDesc;
        var rawLabel = formattingInfo.FormatterOptions.StartsWith('#');
        string classname;
        if (rawLabel) {
            classname = formattingInfo.FormatterOptions.Substring(1);
        } else {
            classname = formattingInfo.FormatterOptions;
        }

        enumDesc = env.TypeCache.GetEnumDescriptor(classname);
        if (enumDesc == null) {
            formattingInfo.Write(formattingInfo.CurrentValue.ToString() ?? string.Empty);
            return true;
        }

        var label = rawLabel
            ? enumDesc.GetLabel(Convert.ChangeType(formattingInfo.CurrentValue, enumDesc.BackingType))
            : enumDesc.GetDisplayLabel(Convert.ChangeType(formattingInfo.CurrentValue, enumDesc.BackingType));
        if (!string.IsNullOrEmpty(label)) {
            formattingInfo.WriteOrFormat(label);
        } else {
            formattingInfo.WriteOrFormat(formattingInfo.CurrentValue.ToString() ?? string.Empty);
        }
        return true;
    }
}

public class EntityLabelFormatter(ContentWorkspace workspace) : IFormatter
{
    public string Name { get; set; } = "entity";
    public bool CanAutoDetect { get; set; } = false;

    public bool TryEvaluateFormat(IFormattingInfo formattingInfo)
    {
        if (formattingInfo.CurrentValue == null) {
            return true;
        }

        var entityType = formattingInfo.FormatterOptions;
        var id = Convert.ToInt64(formattingInfo.CurrentValue);
        var entity = workspace.ResourceManager.GetActiveEntityInstance(entityType, id);
        if (entity == null) {
            formattingInfo.Write(formattingInfo.CurrentValue.ToString() ?? string.Empty);
            return true;
        }

        formattingInfo.Write(entity.Label);
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

public class RegexPatternFormatter : IFormatter
{
    public string Name { get; set; } = "parse";
    public bool CanAutoDetect { get; set; } = false;
    public static readonly RegexPatternFormatter Instance = new();

    private readonly Dictionary<string, Regex> _regexes = new();

    public bool TryEvaluateFormat(IFormattingInfo formattingInfo)
    {
        var regex = _regexes.GetValueOrDefault(formattingInfo.FormatterOptions)
            ?? (_regexes[formattingInfo.FormatterOptions] = new Regex(formattingInfo.FormatterOptions));
        var str = formattingInfo.CurrentValue?.ToString() ?? string.Empty;
        var match = regex.Match(str);
        if (match.Success) {
            formattingInfo.WriteOrFormat(match.Value);
            return true;
        } else {
            formattingInfo.WriteOrFormat(str);
            return true;
        }
    }
}

public class StringHashFormatter : IFormatter
{
    public string Name { get; set; } = "hash";
    public bool CanAutoDetect { get; set; } = false;
    public static readonly StringHashFormatter Instance = new();

    public bool TryEvaluateFormat(IFormattingInfo formattingInfo)
    {
        var str = formattingInfo.CurrentValue?.ToString() ?? string.Empty;
        formattingInfo.Write(MurMur3HashUtils.GetHash(str).ToString(CultureInfo.InvariantCulture));
        return true;
    }
}

public class FindEntityFormatter(ContentWorkspace workspace) : IFormatter
{
    public string Name { get; set; } = "findEntity";
    public bool CanAutoDetect { get; set; } = false;
    private Dictionary<string, (StringFormatter entityFmt, StringFormatter resultFmt)> LookupFormatters = new();

    public bool TryEvaluateFormat(IFormattingInfo formattingInfo)
    {
        if (formattingInfo.CurrentValue == null) {
            return true;
        }

        var opts = formattingInfo.FormatterOptions.Split('|', StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries);
        if (opts.Length < 2) {
            return false;
        }

        var entityType = opts[0];
        var entities = workspace.ResourceManager.GetEntityInstances(entityType);
        for (int i = 1; i < opts.Length; i++) {
            var filter = opts[i];
            var eq = filter.IndexOf('=');
            if (eq == -1) {
                Logger.Error($"Invalid findEntity filter {filter}");
                return true;
            }

            var prop = filter.Substring(0, eq);
            var valueStr = filter.Substring(eq + 1);
            var value = formattingInfo.CurrentValue switch {
                ResourceEntity e => e.GetProperty(valueStr) ?? valueStr,
                _ => valueStr,
            };
            entities = entities.Where(e => {
                var propVal = e.Value.GetProperty(prop);
                if (propVal == null) return valueStr == null || valueStr == "null";

                return Convert.ChangeType(value, propVal.GetType()).Equals(propVal);
            });
        }

        var match = entities.FirstOrDefault().Value;
        var formats = formattingInfo.Format?.Split('|');
        if (match == null) {
            if (formats?.Count >= 2) {
                formattingInfo.FormatAsChild(formats[1], formattingInfo.CurrentValue);
            } else {
                formattingInfo.Write("");
            }
        } else if (formats?.Count >= 1) {
            formattingInfo.FormatAsChild(formats[0], match);
        } else {
            formattingInfo.Write(match?.ToString() ?? "");
        }

        return true;
    }
}
