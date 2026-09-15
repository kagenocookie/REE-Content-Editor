namespace ContentPatcher;

using ContentEditor.Editor;
using ReeLib;
using VYaml.Annotations;

public class ClassConfig
{
    public required RszClass Class { get; init; }
    public required ClassConfigSerialized SourceConfig { get; init; }

    public Dictionary<string, ClassFieldConfig>? Fields { get; set; }
    // public Dictionary<string, SubclassConfig>? Subclasses { get; set; }


    public StringFormatter? StringFormatter { get; set; }

    public void MergeIntoSubclass(string subclass, ClassConfig subConfig)
    {
        subConfig.Fields = subConfig.Fields ?? Fields;
    }
}

[YamlObject]
public partial class ClassConfigSerialized
{
    public Dictionary<string, ClassFieldConfig>? Fields { get; set; }
    [YamlMember("to_string")]
    public string? To_String { get; set; }

    internal void Merge(ClassConfigSerialized source)
    {
        To_String = source.To_String ?? To_String;
        if (source.Fields == null) return;
        foreach (var (f, newConf) in source.Fields) {
            Fields ??= new();
            if (!Fields.TryGetValue(f, out var myConf)) {
                Fields[f] = newConf;
                continue;
            }

            if (!string.IsNullOrEmpty(newConf.Enum)) myConf.Enum = newConf.Enum;
            if (!string.IsNullOrEmpty(newConf.Handler)) myConf.Handler = newConf.Handler;
            if (!string.IsNullOrEmpty(newConf.Label)) myConf.Label = newConf.Label;
            if (!string.IsNullOrEmpty(newConf.TranslateFallbackEnum)) myConf.TranslateFallbackEnum = newConf.TranslateFallbackEnum;
            if (!string.IsNullOrEmpty(newConf.TranslateGuid)) myConf.TranslateGuid = newConf.TranslateGuid;
            if (!string.IsNullOrEmpty(newConf.Tooltip)) myConf.Tooltip = newConf.Tooltip;
            if (newConf.ReadOnly) myConf.ReadOnly = newConf.ReadOnly;
            if (newConf.Switch != null) myConf.Switch = newConf.Switch;
        }
    }

    public void MergeIntoRuntimeConfig(ContentWorkspace workspace, RszClass? cls, ClassConfig target)
    {
        if (Fields != null) {
            target.Fields ??= new();
            foreach (var field in Fields) {
                if (target.Fields.ContainsKey(field.Key)) continue;

                target.Fields.Add(field.Key, field.Value);
            }
        }

        // if (Subclasses != null) {
        //     target.Subclasses ??= new();
        //     foreach (var (subclass, subdata) in Subclasses) {
        //         if (target.Subclasses.ContainsKey(subclass)) continue;

        //         target.Subclasses.Add(subclass, subdata.ToRuntimeConfig(subclass));
        //     }
        // }
    }
}

[YamlObject]
public partial class ClassFieldConfig
{
    public string? Enum { get; set; }
    public string? Tooltip { get; set; }
    public string? Label { get; set; }
    public string? TranslateGuid { get; set; }
    public string? TranslateFallbackEnum { get; set; }
    public string? Handler { get; set; }
    public bool ReadOnly { get; set; }

    public ConditionalClassSwitchConfig? Switch { get; set; }
}

[YamlObject]
public partial class ConditionalClassSwitchConfig
{
    public string? Property { get; set; }
    public Dictionary<object, ClassFieldConfig>? Cases { get; set; }
}

[YamlObject]
public partial class ConditionalFieldConfig
{
    public ResourceConditionData when = null!;
}

[YamlObject]
public partial class SubclassConfig
{
}

[YamlObject]
public partial class LinkedResourceData
{
    public string type = string.Empty;
    public ResourceConditionData? when;
}
