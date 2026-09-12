namespace ContentPatcher;

using ContentEditor.Editor;
using ReeLib;
using VYaml.Annotations;

public class ClassConfig
{
    public Dictionary<string, ClassFieldConfig>? Fields { get; set; }
    public Dictionary<string, SubclassConfig>? Subclasses { get; set; }
    public List<LinkedResourceData>? Resources { get; set; }
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
    public string? Type;
    public string? ResourceType;
    public string? Tooltip;
    public string? Label;
    public string? TranslateGuid;
    public string? TranslateFallbackEnum;
    public string? Handler;
    public bool ReadOnly;
    public Dictionary<string, string?>? OneOf { get; set; }
}

[YamlObject]
public partial class SubclassConfig
{
    public Dictionary<string, ClassFieldConfig>? Fields { get; set; }
    public ResourceHandler? Patcher { get; set; }
}

[YamlObject]
public partial class LinkedResourceData
{
    public string key = string.Empty;
    public string type = string.Empty;
    public string? name;
    public string? field;
    public ResourceConditionData? when;
}
