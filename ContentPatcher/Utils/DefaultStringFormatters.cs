using System.Collections;
using ReeLib;
using SmartFormat.Core.Extensions;
using SmartFormat.Core.Settings;

namespace ContentPatcher.StringFormatting;

public static class FormatterSettings
{
    public static readonly SmartSettings DefaultSettings = new SmartFormat.Core.Settings.SmartSettings() {
        CaseSensitivity = SmartFormat.Core.Settings.CaseSensitivityType.CaseSensitive,
        StringFormatCompatibility = false,
    };
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

        if (instance.TryGetFieldValue(selectorInfo.SelectorText, out var value)) {
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

public class TranslateGuidformatter(MessageManager msg) : IFormatter
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
        var label = enumDesc.GetDisplayLabel(formattingInfo.CurrentValue);
        formattingInfo.Write(label ?? formattingInfo.CurrentValue.ToString() ?? string.Empty);
        return true;
    }
}
